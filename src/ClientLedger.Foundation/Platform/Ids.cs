namespace ClientLedger.Foundation.Platform;

public static class Ids
{
    /// <summary>短前缀 + Guid，便于在数据库里一眼看出实体类型。</summary>
    public static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    /// <summary>示例数据统一带 demo_ 前缀，一键清除时只删这些，不碰真实数据。</summary>
    public static string Demo(string prefix) => $"demo_{prefix}_{Guid.NewGuid():N}";

    public static bool IsDemo(string id) => id.StartsWith("demo_", StringComparison.Ordinal);
}