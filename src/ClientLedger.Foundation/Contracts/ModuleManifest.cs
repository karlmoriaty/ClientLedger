namespace ClientLedger.Foundation.Contracts;

/// <summary>
/// 模块身份。宿主用 Id 做隔离（表前缀、迁移记账、权限命名空间、审计归属）。
/// </summary>
public sealed record ModuleManifest
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required Version Version { get; init; }

    /// <summary>低于此宿主版本时拒绝加载，避免模块使用不存在的契约。</summary>
    public required Version MinimumHostVersion { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>模块声明它需要哪些权限令牌。宿主据此在权限页展示，不做静默授予。</summary>
    public IReadOnlyList<string> DeclaredPermissions { get; init; } = [];
}