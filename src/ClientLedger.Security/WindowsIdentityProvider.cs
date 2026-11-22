using System.Security.Principal;
using ClientLedger.Foundation.Platform;
using Microsoft.Win32;

namespace ClientLedger.Security;

/// <summary>
/// Windows 身份与机器指纹。机器指纹只取 MachineGuid 的哈希，不含用户名：
/// 同一台机器换 Windows 账户应当仍能使用同一份许可证。
/// </summary>
public sealed class WindowsIdentityProvider : IIdentityProvider
{
    public string Actor => $"{Environment.UserDomainName}\\{Environment.UserName}";

    public string UserSid
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return identity.User?.Value ?? Environment.UserName;
            }
            catch
            {
                return Environment.UserName;
            }
        }
    }

    public string MachineName => Environment.MachineName;

    public string MachineFingerprint => MachineFingerprintCache.Value;

    private static readonly Lazy<string> MachineFingerprintCache = new(ComputeFingerprint, isThreadSafe: true);

    private static string ComputeFingerprint()
    {
        string seed;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            seed = key?.GetValue("MachineGuid")?.ToString() ?? Environment.MachineName;
        }
        catch
        {
            seed = Environment.MachineName;
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }
}