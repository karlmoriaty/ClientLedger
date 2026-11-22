using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Data;

/// <summary>
/// 手写的轻量查询辅助。刻意不引 ORM：模块边界要能容纳"自带表、自带 SQL"的第三方模块，
/// ORM 的共享模型反而是负担。这里只做三件事：参数化、映射、类型转换。
/// </summary>
public static class SqliteExtensions
{
    public static async Task<int> ExecuteAsync(
        this DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<List<T>> QueryAsync<T>(
        this DbConnection connection,
        DbTransaction? transaction,
        string sql,
        Func<SqliteDataReader, T> map,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var result = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(map(reader));
        }

        return result;
    }

    public static async Task<T?> QuerySingleAsync<T>(
        this DbConnection connection,
        DbTransaction? transaction,
        string sql,
        Func<SqliteDataReader, T> map,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var list = await connection
            .QueryAsync(transaction, sql, map, cancellationToken, parameters)
            .ConfigureAwait(false);

        return list.Count == 0 ? default : list[0];
    }

    public static async Task<long> ScalarLongAsync(
        this DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static async Task<decimal> ScalarDecimalAsync(
        this DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private static SqliteCommand CreateCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        (string Name, object? Value)[] parameters)
    {
        // 连接一定是 SqliteConnection，这里强转是为了拿到 SqliteParameterCollection 的 AddWithValue。
        var command = (SqliteCommand)connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = (SqliteTransaction?)transaction;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}

/// <summary>SQLite 没有 decimal / DateTimeOffset 原生类型，统一在这里做无损转换。</summary>
public static class SqliteValue
{
    private const string IsoPattern = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static object? Db(decimal? value) =>
        value is null ? null : value.Value.ToString(CultureInfo.InvariantCulture);

    public static object? Db(DateTimeOffset? value) =>
        value is null ? null : value.Value.ToUniversalTime().ToString(IsoPattern, CultureInfo.InvariantCulture);

    public static object? DbInt(int? value) => value;

    public static decimal Dec(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? 0m
            : decimal.Parse(reader.GetString(ordinal), NumberStyles.Number, CultureInfo.InvariantCulture);

    public static DateTimeOffset Dto(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? DateTimeOffset.MinValue
            : DateTimeOffset.ParseExact(
                reader.GetString(ordinal),
                IsoPattern,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static DateTimeOffset? DtoOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.Dto(ordinal);

    public static string Str(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    public static string? StrOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static bool Bool(this SqliteDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && reader.GetInt64(ordinal) != 0;
}