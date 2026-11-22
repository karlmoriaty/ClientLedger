using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClientLedger.Foundation.Platform;
using ClientLedger.Licensing;

namespace ClientLedger.LicenseForge;

/// <summary>
/// 许可证签发工具。只在一台受控机器上运行；私钥写入本地文件，永不入库、永不进安装包。
/// 用法：
///   genkey  --out keys/dev.private.key
///   issue   --key keys/dev.private.key --id CL-2026-0001 --buyer "某某公司" --edition pro
///           [--email a@b.com] [--updates-until 2027-11-22] [--machine 指纹]
///           [--features crm.quote.export,crm.report.advanced] [--note "..."] [--out license.json]
///   verify  --license license.json
///   show    --license license.json
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "genkey" => GenerateKey(args),
                "issue" => Issue(args),
                "verify" => Verify(args, showPayload: false),
                "show" => Verify(args, showPayload: true),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"失败：{ex.Message}");
            return 2;
        }
    }

    private static int GenerateKey(string[] args)
    {
        var output = Arg(args, "--out") ?? Path.Combine("keys", "dev.private.key");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(output, ecdsa.ExportPkcs8PrivateKeyPem(), new UTF8Encoding(false));

        Console.WriteLine($"私钥已写入：{Path.GetFullPath(output)}");
        Console.WriteLine("请立即把下面这段公钥粘贴到 src/ClientLedger.Licensing/LicensePublicKey.cs：");
        Console.WriteLine();
        Console.WriteLine(ecdsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("提醒：私钥必须离线保管，并且已经在 .gitignore 中排除。");
        return 0;
    }

    private static int Issue(string[] args)
    {
        var keyPath = Arg(args, "--key") ?? Path.Combine("keys", "dev.private.key");
        if (!File.Exists(keyPath))
        {
            Console.Error.WriteLine($"找不到私钥：{keyPath}");
            return 2;
        }

        var licenseId = Arg(args, "--id") ?? $"CL-{DateTime.Now:yyyyMMdd-HHmmss}";
        var buyer = Arg(args, "--buyer") ?? "未填写";
        var edition = Arg(args, "--edition") ?? "pro";
        var features = (Arg(args, "--features") ?? "crm")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DateTimeOffset? updatesUntil = null;
        if (Arg(args, "--updates-until") is { } untilText)
        {
            if (!DateTimeOffset.TryParse(untilText, out var parsed))
            {
                Console.Error.WriteLine($"--updates-until 不是合法日期：{untilText}");
                return 2;
            }

            updatesUntil = parsed;
        }

        var payload = new LicensePayload
        {
            LicenseId = licenseId,
            Edition = edition,
            Buyer = buyer,
            Email = Arg(args, "--email"),
            IssuedUtc = DateTimeOffset.UtcNow,
            UpdatesUntil = updatesUntil,
            Machine = Arg(args, "--machine"),
            Features = features,
            Note = Arg(args, "--note")
        };

        var payloadBytes = LicenseVerifier.PayloadBytes(payload);
        var document = new LicenseDocument(
            Convert.ToBase64String(payloadBytes),
            LicenseVerifier.SignPayload(payloadBytes, File.ReadAllText(keyPath)));

        var output = Arg(args, "--out") ?? "license.json";
        File.WriteAllText(
            output,
            JsonSerializer.Serialize(document, JsonDefaults.Indented),
            new UTF8Encoding(false));

        Console.WriteLine($"许可证已签发：{Path.GetFullPath(output)}");
        Console.WriteLine($"  许可证号 : {payload.LicenseId}");
        Console.WriteLine($"  客户     : {payload.Buyer}");
        Console.WriteLine($"  版本     : {payload.Edition}");
        Console.WriteLine($"  更新截止 : {(payload.UpdatesUntil is null ? "永久" : Formats.Day(payload.UpdatesUntil.Value))}");
        Console.WriteLine($"  机器绑定 : {(payload.Machine is null ? "不绑定" : payload.Machine)}");
        Console.WriteLine($"  功能     : {string.Join(", ", payload.Features)}");

        return Verify(["verify", "--license", output], showPayload: false);
    }

    private static int Verify(string[] args, bool showPayload)
    {
        var path = Arg(args, "--license") ?? "license.json";
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"找不到许可证文件：{path}");
            return 2;
        }

        var document = JsonSerializer.Deserialize<LicenseDocument>(File.ReadAllText(path), JsonDefaults.Compact);
        if (document is null)
        {
            Console.Error.WriteLine("许可证文件为空或格式不正确。");
            return 2;
        }

        var ok = LicenseVerifier.TryVerify(document, out var payload, out var error);
        Console.WriteLine(ok ? "签名校验：通过" : $"签名校验：失败（{error}）");

        if (payload is not null)
        {
            Console.WriteLine($"  许可证号 : {payload.LicenseId}");
            Console.WriteLine($"  客户     : {payload.Buyer}");
            Console.WriteLine($"  版本     : {payload.Edition}");
            Console.WriteLine($"  签发时间 : {Formats.Moment(payload.IssuedUtc)}");
            Console.WriteLine($"  更新截止 : {(payload.UpdatesUntil is null ? "永久" : Formats.Day(payload.UpdatesUntil.Value))}");
            Console.WriteLine($"  机器绑定 : {(payload.Machine is null ? "不绑定" : payload.Machine)}");
            Console.WriteLine($"  功能     : {string.Join(", ", payload.Features)}");

            if (showPayload)
            {
                Console.WriteLine();
                Console.WriteLine(Encoding.UTF8.GetString(LicenseVerifier.PayloadBytes(payload)));
            }
        }

        return ok ? 0 : 3;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令：{command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ClientLedger LicenseForge");
        Console.WriteLine("  genkey --out keys/dev.private.key");
        Console.WriteLine("  issue  --key <私钥> --id <许可证号> --buyer <客户> [--edition pro] [--email a@b.com]");
        Console.WriteLine("         [--updates-until 2027-11-22] [--machine <指纹>] [--features a,b] [--note ...] [--out license.json]");
        Console.WriteLine("  verify --license license.json");
        Console.WriteLine("  show   --license license.json");
    }

    private static string? Arg(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}