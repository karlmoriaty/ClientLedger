using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClientLedger.Foundation.Platform;

/// <summary>
/// 序列化口径集中在这儿。许可证验签依赖"同一份字节"，所以许可证一律用
/// <see cref="Compact"/> 生成 payload，并且验签时只对原始字节做校验，不重新序列化。
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Compact { get; } = Create(writeIndented: false);

    public static JsonSerializerOptions Indented { get; } = Create(writeIndented: true);

    private static JsonSerializerOptions Create(bool writeIndented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = writeIndented,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}