namespace ClientLedger.Licensing;

/// <summary>
/// 许可证验签公钥（ECDSA P-256，SubjectPublicKeyInfo PEM）。
/// 私钥只存在于签发机（tools/ClientLedger.LicenseForge/keys/），永不入库、永不随安装包分发。
/// </summary>
public static class LicensePublicKey
{
    public const string Pem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+DQioBmkC0Pf1mGT67segbk0jN3U
T92UzKwRlLsfISSx5PrdTDLVUIVBGeqF78SpfSYQAdk4CQJHg4WKYoJlGg==
-----END PUBLIC KEY-----
""";

    public const string ProductId = "clientledger";
}