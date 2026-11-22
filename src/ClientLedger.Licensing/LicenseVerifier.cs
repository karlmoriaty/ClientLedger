using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClientLedger.Foundation.Platform;

namespace ClientLedger.Licensing;

public static class LicenseVerifier
{
    public static bool TryVerify(
        LicenseDocument document,
        out LicensePayload? payload,
        out string error) =>
        TryVerify(document, LicensePublicKey.Pem, out payload, out error);

    /// <summary>
    /// 验证签名并返回正文。签名采用 IEEE P1363 定长格式（r||s），跨平台长度稳定。
    /// publicKeyPem 可显式传入，便于测试时使用临时密钥对。
    /// </summary>
    public static bool TryVerify(
        LicenseDocument document,
        string publicKeyPem,
        out LicensePayload? payload,
        out string error)
    {
        payload = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(document.Payload) || string.IsNullOrWhiteSpace(document.Signature))
        {
            error = "许可证内容为空。";
            return false;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Convert.FromBase64String(document.Payload.Trim());
            signature = Convert.FromBase64String(document.Signature.Trim());
        }
        catch (FormatException)
        {
            error = "许可证不是有效的 Base64 内容。";
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);

            if (!ecdsa.VerifyData(
                    payloadBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                error = "许可证签名校验失败。";
                return false;
            }
        }
        catch (CryptographicException ex)
        {
            error = $"许可证验签异常：{ex.Message}";
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonDefaults.Compact);
        }
        catch (JsonException)
        {
            error = "许可证正文格式无法识别。";
            return false;
        }

        if (payload is null)
        {
            error = "许可证正文为空。";
            return false;
        }

        if (payload.V != 1)
        {
            error = $"许可证版本 {payload.V} 不受当前程序支持。";
            return false;
        }

        if (!string.Equals(payload.Product, LicensePublicKey.ProductId, StringComparison.OrdinalIgnoreCase))
        {
            error = "许可证属于其它产品。";
            return false;
        }

        return true;
    }

    public static byte[] PayloadBytes(LicensePayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.Compact);

    public static string EncodePayload(LicensePayload payload) =>
        Convert.ToBase64String(PayloadBytes(payload));

    public static string SignPayload(byte[] payloadBytes, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        var signature = ecdsa.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return Convert.ToBase64String(signature);
    }

    public static string ExportPublicKeyPem(byte[] privateKeyPkcs8)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        return ecdsa.ExportSubjectPublicKeyInfoPem();
    }
}