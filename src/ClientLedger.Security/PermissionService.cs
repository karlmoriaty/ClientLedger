namespace ClientLedger.Security;

public interface IPermissionService
{
    UserSession Session { get; }

    bool Has(string permission);

    /// <summary>在会改数据或泄露数据的路径上强制校验，失败直接抛异常，不返回布尔值。</summary>
    void Demand(string permission);
}

public sealed class PermissionService(IUserContext userContext) : IPermissionService
{
    public UserSession Session => userContext.Current;

    public bool Has(string permission) => userContext.Current.Has(permission);

    public void Demand(string permission)
    {
        if (!Has(permission))
        {
            throw new UnauthorizedAccessException($"当前角色（{userContext.Current.RoleDisplayName}）缺少权限：{permission}");
        }
    }
}