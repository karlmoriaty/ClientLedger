using ClientLedger.Data;
using ClientLedger.Foundation.Platform;

namespace ClientLedger.Security;

public sealed record UserSession(
    string Actor,
    string Sid,
    UserRole Role,
    DataScope Scope,
    IReadOnlySet<string> Permissions)
{
    public string RoleDisplayName => RoleCatalog.DisplayName(Role);

    public string ScopeDisplayName => Scope == DataScope.Owned ? "仅本人负责" : "全部数据";

    public bool Has(string permission) => Permissions.Contains(permission);
}

public interface IUserContext
{
    UserSession Current { get; }

    event EventHandler? Changed;

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>切换本机用户角色。真实多用户场景下这里应改为读取账户目录，接口保持不变。</summary>
    Task SwitchRoleAsync(UserRole role, CancellationToken cancellationToken = default);
}

/// <summary>
/// 本机用户上下文。默认角色是 Owner（买断制单机软件的第一个用户就是老板），
/// 角色持久化在平台设置里，切换后即刻重建会话并广播。
/// </summary>
public sealed class UserContext : IUserContext
{
    private const string RoleKey = "security.role";

    private readonly ISettingsStore _settings;
    private readonly IIdentityProvider _identity;
    private UserSession _current;

    public UserContext(ISettingsStore settings, IIdentityProvider identity)
    {
        _settings = settings;
        _identity = identity;
        _current = Build(UserRole.Owner);
    }

    public UserSession Current => _current;

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var role = UserRole.Owner;
        var raw = await _settings.GetAsync(RoleKey, cancellationToken).ConfigureAwait(false);

        if (Enum.TryParse<UserRole>(raw, ignoreCase: true, out var parsed))
        {
            role = parsed;
        }

        _current = Build(role);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SwitchRoleAsync(UserRole role, CancellationToken cancellationToken = default)
    {
        await _settings.SetAsync(RoleKey, role.ToString(), cancellationToken).ConfigureAwait(false);
        _current = Build(role);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private UserSession Build(UserRole role) => new(
        _identity.Actor,
        _identity.UserSid,
        role,
        RoleCatalog.ScopeFor(role),
        RoleCatalog.PermissionsFor(role));
}