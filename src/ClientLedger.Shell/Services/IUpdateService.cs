namespace ClientLedger.Shell.Services;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToRestart,
    NotInstalled,
    Failed
}

public sealed record UpdateSnapshot
{
    public required UpdateState State { get; init; }

    public required string StatusText { get; init; }

    public string? AvailableVersion { get; init; }

    public int ProgressPercent { get; init; }

    public bool IsInstalled { get; init; }

    public bool HasUpdate => State is UpdateState.Available or UpdateState.Downloading or UpdateState.ReadyToRestart;
}

/// <summary>
/// 更新能力的抽象。Shell 只依赖这个接口，具体实现（Velopack）放在宿主项目，
/// 这样以后换成其它分发方案时，UI 与设置页不用改。
/// </summary>
public interface IUpdateService
{
    UpdateSnapshot Current { get; }

    event EventHandler? Changed;

    Task CheckAsync(CancellationToken cancellationToken = default);

    Task DownloadAsync(CancellationToken cancellationToken = default);

    void ApplyAndRestart();
}