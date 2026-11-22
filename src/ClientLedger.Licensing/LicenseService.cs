using System.Text.Json;
using ClientLedger.Foundation.Platform;
using ClientLedger.Licensing;

namespace ClientLedger.Licensing;

public interface ILicenseService
{
    LicenseSnapshot Current { get; }

    event EventHandler? Changed;

    /// <summary>重新读取许可证文件并重算状态。启动时调用一次。</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>导入许可证文件。返回值表示是否导入成功；失败原因写在 Current.Message。</summary>
    Task<bool> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>便捷封装：未授权功能返回 false，调用方据此隐藏或禁用入口。</summary>
    bool HasFeature(string feature);
}

public sealed class LicenseService(
    AppPaths paths,
    IIdentityProvider identity,
    TrialStore trialStore,
    IClock clock,
    IAppLog log) : ILicenseService
{
    public const string ProductId = "clientledger";

    private readonly string _licensePath = Path.Combine(paths.DataDirectory, "license.json");

    public LicenseSnapshot Current { get; private set; } = new()
    {
        Status = LicenseStatus.TrialExpired,
        Message = "尚未初始化。"
    };

    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var trial = trialStore.Load();
        var daysLeft = trialStore.DaysLeft(trial);
        var trialExpired = trialStore.IsExpired(trial);

        if (File.Exists(_licensePath))
        {
            var snapshot = await EvaluateAsync(_licensePath, daysLeft, cancellationToken).ConfigureAwait(false);
            if (snapshot.Status != LicenseStatus.Invalid)
            {
                Publish(snapshot);
                return;
            }

            log.Warn($"许可证无效：{snapshot.Message}");
        }

        Publish(trialExpired
            ? new LicenseSnapshot
            {
                Status = LicenseStatus.TrialExpired,
                TrialDaysLeft = 0,
                Message = trial.ClockRollbackDetected
                    ? "检测到系统时间回拨，试用已终止。请联系支持获取正式许可证。"
                    : "试用期已结束。数据仍然完整保留，导入许可证即可继续使用。"
            }
            : new LicenseSnapshot
            {
                Status = LicenseStatus.TrialActive,
                TrialDaysLeft = daysLeft,
                Message = $"试用版，剩余 {daysLeft} 天。全部功能可用。"
            });
    }

    public async Task<bool> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);

            LicenseDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<LicenseDocument>(json, JsonDefaults.Compact);
            }
            catch (JsonException)
            {
                document = null;
            }

            if (document is null)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                PublishWithNotice("导入失败：文件不是有效的许可证 JSON。");
                return false;
            }

            if (!LicenseVerifier.TryVerify(document, out var payload, out var error) || payload is null)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                PublishWithNotice($"导入失败：{error}");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_licensePath)!);
            await File.WriteAllTextAsync(_licensePath, json, cancellationToken).ConfigureAwait(false);

            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return Current.IsUsable;
        }
        catch (Exception ex)
        {
            log.Error("导入许可证失败。", ex);
            Publish(new LicenseSnapshot
            {
                Status = LicenseStatus.Invalid,
                Message = $"读取许可证文件失败：{ex.Message}"
            });
            return false;
        }
    }

    public bool HasFeature(string feature) => Current.HasFeature(feature);

    private async Task<LicenseSnapshot> EvaluateAsync(
        string path,
        int trialDaysLeft,
        CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new LicenseSnapshot { Status = LicenseStatus.Invalid, Message = $"读取失败：{ex.Message}" };
        }

        LicenseDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<LicenseDocument>(json, JsonDefaults.Compact);
        }
        catch (JsonException)
        {
            return new LicenseSnapshot { Status = LicenseStatus.Invalid, Message = "许可证文件不是有效 JSON。" };
        }

        if (document is null)
        {
            return new LicenseSnapshot { Status = LicenseStatus.Invalid, Message = "许可证文件为空。" };
        }

        if (!LicenseVerifier.TryVerify(document, out var payload, out var error) || payload is null)
        {
            return new LicenseSnapshot { Status = LicenseStatus.Invalid, Message = error };
        }

        if (!string.IsNullOrWhiteSpace(payload.Machine)
            && !string.Equals(payload.Machine, identity.MachineFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new LicenseSnapshot
            {
                Status = LicenseStatus.Invalid,
                Message = "该许可证绑定了另一台机器。请联系支持办理换机。"
            };
        }

        if (payload.IssuedUtc > clock.UtcNow.AddDays(1))
        {
            return new LicenseSnapshot
            {
                Status = LicenseStatus.Invalid,
                Message = "许可证签发时间晚于本机时间，请检查系统时钟。"
            };
        }

        var updateExpired = payload.UpdatesUntil is { } until && clock.UtcNow > until;

        return new LicenseSnapshot
        {
            Status = updateExpired ? LicenseStatus.LicensedUpdateExpired : LicenseStatus.Licensed,
            TrialDaysLeft = trialDaysLeft,
            LicenseId = payload.LicenseId,
            Buyer = payload.Buyer,
            Edition = payload.Edition,
            UpdatesUntil = payload.UpdatesUntil,
            Features = payload.Features,
            Message = updateExpired
                ? "已买断：当前版本永久可用，但免费更新期已过。续期后可安装后续版本。"
                : "已授权，感谢支持。"
        };
    }

    private void Publish(LicenseSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>保留真实状态，只替换提示文案：导入失败不等于许可证失效。</summary>
    private void PublishWithNotice(string notice)
    {
        Current = Current with { Message = notice };
        Changed?.Invoke(this, EventArgs.Empty);
    }
}