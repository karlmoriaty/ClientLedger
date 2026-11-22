namespace ClientLedger.Foundation.Platform;

/// <summary>
/// 当前操作者身份。审计、数据归属、授权绑定都依赖它，因此必须由宿主提供，模块不得自造。
/// </summary>
public interface IIdentityProvider
{
    /// <summary>可读身份，例如 计算机名\用户名。</summary>
    string Actor { get; }

    /// <summary>Windows 账户 SID，比用户名可靠（用户名可改）。</summary>
    string UserSid { get; }

    string MachineName { get; }

    /// <summary>机器指纹，用于许可证绑定与换机识别。</summary>
    string MachineFingerprint { get; }
}