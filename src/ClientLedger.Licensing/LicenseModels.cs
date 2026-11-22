namespace ClientLedger.Licensing;

public enum LicenseStatus
{
    /// <summary>试用期内。</summary>
    TrialActive,

    /// <summary>试用已结束，未导入有效许可证。</summary>
    TrialExpired,

    /// <summary>已授权，且在更新期内。</summary>
    Licensed,

    /// <summary>已授权（买断永久可用），但更新期已过：软件照常使用，只能装当前大版本。</summary>
    LicensedUpdateExpired,

    /// <summary>许可证文件存在但无效：被篡改、绑定了其它机器、产品或密钥不匹配。</summary>
    Invalid
}

/// <summary>
/// 许可证正文。字段名刻意保持短小稳定，因为它是签名载荷，改字段等于改协议版本。
/// </summary>
public sealed record LicensePayload
{
    public int V { get; init; } = 1;

    public string Product { get; init; } = "clientledger";

    public string LicenseId { get; init; } = string.Empty;

    public string Edition { get; init; } = "pro";

    public string Buyer { get; init; } = string.Empty;

    public string? Email { get; init; }

    public DateTimeOffset IssuedUtc { get; init; }

    /// <summary>免费更新截止时间。为空表示永久更新。</summary>
    public DateTimeOffset? UpdatesUntil { get; init; }

    /// <summary>机器指纹。为空表示不绑定机器（便于换机，但增加共享风险）。</summary>
    public string? Machine { get; init; }

    public IReadOnlyList<string> Features { get; init; } = [];

    public string? Note { get; init; }
}

/// <summary>
/// 磁盘上的许可证文件：payload 是原始 UTF-8 JSON 的 Base64，签名直接针对这些字节。
/// 验签时绝不允许"反序列化再序列化"，否则字段顺序或空白变化会让合法许可证失效。
/// </summary>
public sealed record LicenseDocument(string Payload, string Signature);

public sealed record LicenseSnapshot
{
    public required LicenseStatus Status { get; init; }

    public int? TrialDaysLeft { get; init; }

    public string? LicenseId { get; init; }

    public string? Buyer { get; init; }

    public string? Edition { get; init; }

    public DateTimeOffset? UpdatesUntil { get; init; }

    public IReadOnlyList<string> Features { get; init; } = [];

    public string Message { get; init; } = string.Empty;

    public bool IsUsable => Status is LicenseStatus.TrialActive or LicenseStatus.Licensed or LicenseStatus.LicensedUpdateExpired;

    public bool IsTrial => Status is LicenseStatus.TrialActive or LicenseStatus.TrialExpired;

    public string StatusText => Status switch
    {
        LicenseStatus.TrialActive => $"试用中 · 剩余 {TrialDaysLeft} 天",
        LicenseStatus.TrialExpired => "试用已结束",
        LicenseStatus.Licensed => "已授权",
        LicenseStatus.LicensedUpdateExpired => "已买断 · 更新期已过",
        _ => "许可证无效"
    };

    public bool HasFeature(string feature) =>
        Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
}