using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClientLedger.Foundation.Platform;

namespace ClientLedger.Licensing;

public sealed record TrialRecord(
    string InstallId,
    DateTimeOffset FirstRunUtc,
    DateTimeOffset LastSeenUtc,
    DateTimeOffset MaxSeenUtc,
    bool ClockRollbackDetected);

/// <summary>
/// 试用记录。写两份（机器共享目录 + 当前用户目录），读取时取"起始时间最早"的那份：
/// 删掉任意一份都不会重置试用，这是最便宜的防重置手段。
/// 内容用 AES-GCM 加密，密钥由机器指纹派生，拷贝到别的机器上无法解密。
/// </summary>
public sealed class TrialStore(AppPaths paths, IIdentityProvider identity, IClock clock, IAppLog log)
{
    private const int TrialDays = 30;
    private const string FileName = "trial.bin";
    private static readonly byte[] Magic = "CLTR"u8.ToArray();
    private static readonly byte[] AssociatedData = "ClientLedger.TrialRecord.v1"u8.ToArray();

    private byte[] Key => SHA256.HashData(
        Encoding.UTF8.GetBytes("ClientLedger.Trial.v1|" + identity.MachineFingerprint));

    public int TrialDaysTotal => TrialDays;

    public TrialRecord Load()
    {
        var candidates = new List<TrialRecord>();

        foreach (var path in CandidatePaths())
        {
            var record = TryRead(path);
            if (record is not null)
            {
                candidates.Add(record);
            }
        }

        TrialRecord current;
        if (candidates.Count == 0)
        {
            current = new TrialRecord(
                Guid.NewGuid().ToString("N"),
                clock.UtcNow,
                clock.UtcNow,
                clock.UtcNow,
                ClockRollbackDetected: false);
        }
        else
        {
            var earliest = candidates.MinBy(r => r.FirstRunUtc)!;
            var latestSeen = candidates.Max(r => r.MaxSeenUtc);

            var rollback = clock.UtcNow < latestSeen.AddHours(-1) || earliest.ClockRollbackDetected;

            current = earliest with
            {
                LastSeenUtc = clock.UtcNow,
                MaxSeenUtc = rollback ? latestSeen : Max(clock.UtcNow, latestSeen),
                ClockRollbackDetected = rollback
            };
        }

        Save(current);
        return current;
    }

    public int DaysLeft(TrialRecord record)
    {
        var elapsed = (clock.UtcNow - record.FirstRunUtc).TotalDays;
        var left = Math.Ceiling(TrialDays - elapsed);
        return (int)Math.Clamp(left, 0, TrialDays);
    }

    public bool IsExpired(TrialRecord record) =>
        record.ClockRollbackDetected || clock.UtcNow - record.FirstRunUtc > TimeSpan.FromDays(TrialDays);

    private IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(paths.DataDirectory, FileName);

        string shared;
        try
        {
            shared = Path.Combine(paths.SharedDirectory, FileName);
            Directory.CreateDirectory(paths.SharedDirectory);
        }
        catch (Exception ex)
        {
            log.Warn("机器共享目录不可用，试用记录仅保存在当前用户目录。", ex);
            yield break;
        }

        yield return shared;
    }

    private void Save(TrialRecord record)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(record, JsonDefaults.Compact);

        try
        {
            var buffer = new byte[5 + 12 + 16 + json.Length];
            Magic.CopyTo(buffer, 0);
            buffer[4] = 1;

            var nonce = buffer.AsSpan(5, 12);
            RandomNumberGenerator.Fill(nonce);

            var tag = buffer.AsSpan(17, 16);
            var cipher = buffer.AsSpan(33);

            using var aes = new AesGcm(Key, 16);
            aes.Encrypt(nonce, json, cipher, tag, AssociatedData);

            foreach (var path in CandidatePaths())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, buffer);
                }
                catch (Exception ex)
                {
                    log.Warn($"写入试用记录失败：{path}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("生成试用记录失败。", ex);
        }
    }

    private TrialRecord? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var buffer = File.ReadAllBytes(path);
            if (buffer.Length < 33 || !buffer.AsSpan(0, 4).SequenceEqual(Magic) || buffer[4] != 1)
            {
                return null;
            }

            var plain = new byte[buffer.Length - 33];
            using var aes = new AesGcm(Key, 16);
            aes.Decrypt(
                buffer.AsSpan(5, 12),
                buffer.AsSpan(33),
                buffer.AsSpan(17, 16),
                plain,
                AssociatedData);

            return JsonSerializer.Deserialize<TrialRecord>(plain, JsonDefaults.Compact);
        }
        catch (Exception ex)
        {
            // 解密失败通常意味着文件被换机拷贝或损坏；按"无记录"处理，由另一份兜底。
            log.Warn($"读取试用记录失败：{path}", ex);
            return null;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}