using System.Text.Json;
using ClientLedger.Foundation.Platform;

namespace ClientLedger.Data;

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        where T : class;

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 平台级键值设置。只给"宿主级"配置用（角色、许可、界面偏好），
/// 业务数据一律进各自的模块表，不允许塞进这里当 JSON 堆。
/// </summary>
public sealed class SettingsStore(IDbConnectionFactory factory, IClock clock) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var values = await connection.QueryAsync(
            null,
            "SELECT value FROM platform_settings WHERE key = $key;",
            reader => reader.GetString(0),
            cancellationToken,
            ("$key", key)).ConfigureAwait(false);

        return values.Count == 0 ? null : values[0];
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            null,
            """
            INSERT INTO platform_settings (key, value, updated_utc)
            VALUES ($key, $value, $updated)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_utc = excluded.updated_utc;
            """,
            cancellationToken,
            ("$key", key),
            ("$value", value),
            ("$updated", SqliteValue.Db(clock.UtcNow))).ConfigureAwait(false);
    }

    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        var raw = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonDefaults.Compact);
        }
        catch (JsonException)
        {
            // 配置损坏时当作未设置处理，避免整个程序启动失败。
            return null;
        }
    }

    public Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        where T : class =>
        SetAsync(key, JsonSerializer.Serialize(value, JsonDefaults.Compact), cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var pairs = await connection.QueryAsync(
            null,
            "SELECT key, value FROM platform_settings;",
            reader => (Key: reader.GetString(0), Value: reader.GetString(1)),
            cancellationToken).ConfigureAwait(false);

        return pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }
}