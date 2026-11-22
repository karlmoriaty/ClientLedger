using System.Text.Json;
using ClientLedger.Foundation.Platform;

namespace ClientLedger.Data.Audit;

/// <summary>
/// 审计快照序列化。超过 8KB 的快照截断保存：
/// 审计表是用来回溯"谁在什么时候改了什么"，不是数据备份，不能无限膨胀。
/// </summary>
public static class AuditJson
{
    private const int MaxLength = 8000;

    public static string? Of(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(value, JsonDefaults.Compact);
        return json.Length <= MaxLength ? json : json[..MaxLength];
    }
}