using ClientLedger.Foundation.Platform;

namespace ClientLedger.Foundation.Contracts;

/// <summary>
/// 模块能看到的宿主能力。故意保持窄：模块拿不到容器、拿不到别的模块、拿不到数据库连接字符串。
/// </summary>
public interface IModuleContext
{
    /// <summary>模块独占的数据目录（备份、附件等）。</summary>
    string DataDirectory { get; }

    IClock Clock { get; }

    IEventBus Events { get; }

    IAuditWriter Audit { get; }

    IAppLog Log { get; }
}