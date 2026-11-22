using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Modules.Crm.Data;

/// <summary>
/// 联系人。归属权通过客户间接判定：查询一律 JOIN 客户表并过滤已删除客户，
/// 避免"客户被删了，联系人还在列表里飘着"这种典型脏数据。
/// </summary>
public sealed class ContactStore(
    IUnitOfWork unitOfWork,
    IDbConnectionFactory factory,
    IPermissionService permissions,
    IUserContext userContext,
    IAuditWriter audit,
    IClock clock)
{
    private const string SelectSql = """
        SELECT ct.id, ct.customer_id, c.name, ct.name, ct.title, ct.phone, ct.email, ct.wechat,
               ct.is_primary, ct.note, ct.created_utc, ct.updated_utc
        FROM crm_contact ct
        JOIN crm_customer c ON c.id = ct.customer_id
        """;

    public async Task<IReadOnlyList<Contact>> ListAsync(
        string? customerId = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmContactRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var conditions = new List<string> { "ct.is_deleted = 0", "c.is_deleted = 0" };
        var parameters = new List<(string, object?)>();

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            conditions.Add("ct.customer_id = $customer");
            parameters.Add(("$customer", customerId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            conditions.Add("(ct.name LIKE $kw ESCAPE '\\' OR ct.phone LIKE $kw ESCAPE '\\' OR c.name LIKE $kw ESCAPE '\\')");
            parameters.Add(("$kw", $"%{CustomerStore.EscapeLike(search)}%"));
        }

        if (userContext.Current.Scope == DataScope.Owned)
        {
            conditions.Add("c.owner = $actor");
            parameters.Add(("$actor", userContext.Current.Actor));
        }

        var sql = $"{SelectSql} WHERE {string.Join(" AND ", conditions)} ORDER BY ct.is_primary DESC, ct.name;";

        return await connection
            .QueryAsync(null, sql, Map, cancellationToken, parameters.ToArray())
            .ConfigureAwait(false);
    }

    public async Task<Contact?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmContactRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleAsync(
            null,
            $"{SelectSql} WHERE ct.id = $id AND ct.is_deleted = 0;",
            Map,
            cancellationToken,
            ("$id", id)).ConfigureAwait(false);
    }

    public async Task<string> SaveAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmContactWrite);

        return await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await connection.QuerySingleAsync(
                transaction,
                """
                SELECT ct.id, ct.customer_id, c.name, ct.name, ct.title, ct.phone, ct.email, ct.wechat,
                       ct.is_primary, ct.note, ct.created_utc, ct.updated_utc
                FROM crm_contact ct
                JOIN crm_customer c ON c.id = ct.customer_id
                WHERE ct.id = $id;
                """,
                Map,
                cancellationToken,
                ("$id", contact.Id)).ConfigureAwait(false);

            var now = clock.UtcNow;
            var record = contact with
            {
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            };

            await connection.ExecuteAsync(
                transaction,
                """
                INSERT INTO crm_contact
                    (id, customer_id, name, title, phone, email, wechat, is_primary, note, created_utc, updated_utc, is_deleted)
                VALUES
                    ($id, $customer, $name, $title, $phone, $email, $wechat, $primary, $note, $created, $updated, 0)
                ON CONFLICT(id) DO UPDATE SET
                    customer_id = excluded.customer_id,
                    name = excluded.name,
                    title = excluded.title,
                    phone = excluded.phone,
                    email = excluded.email,
                    wechat = excluded.wechat,
                    is_primary = excluded.is_primary,
                    note = excluded.note,
                    updated_utc = excluded.updated_utc,
                    is_deleted = 0;
                """,
                cancellationToken,
                ("$id", record.Id),
                ("$customer", record.CustomerId),
                ("$name", record.Name),
                ("$title", record.Title),
                ("$phone", record.Phone),
                ("$email", record.Email),
                ("$wechat", record.WeChat),
                ("$primary", record.IsPrimary ? 1 : 0),
                ("$note", record.Note),
                ("$created", SqliteValue.Db(record.CreatedUtc)),
                ("$updated", SqliteValue.Db(record.UpdatedUtc))).ConfigureAwait(false);

            // 一个客户只保留一个主联系人，避免后续"到底该打给谁"的歧义。
            if (record.IsPrimary)
            {
                await connection.ExecuteAsync(
                    transaction,
                    "UPDATE crm_contact SET is_primary = 0 WHERE customer_id = $customer AND id <> $id;",
                    cancellationToken,
                    ("$customer", record.CustomerId),
                    ("$id", record.Id)).ConfigureAwait(false);
            }

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = existing is null ? "crm.contact.create" : "crm.contact.update",
                    EntityType = "contact",
                    EntityId = record.Id,
                    Summary = $"{record.Name} @ {record.CustomerName}",
                    BeforeJson = AuditJson.Of(existing),
                    AfterJson = AuditJson.Of(record)
                },
                cancellationToken).ConfigureAwait(false);

            return record.Id;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmContactWrite);

        await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await connection.QuerySingleAsync(
                transaction,
                $"{SelectSql} WHERE ct.id = $id;",
                Map,
                cancellationToken,
                ("$id", id)).ConfigureAwait(false);

            if (existing is null)
            {
                return;
            }

            await connection.ExecuteAsync(
                transaction,
                "UPDATE crm_contact SET is_deleted = 1, updated_utc = $now WHERE id = $id;",
                cancellationToken,
                ("$id", id),
                ("$now", SqliteValue.Db(clock.UtcNow))).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = "crm.contact.delete",
                    EntityType = "contact",
                    EntityId = id,
                    Summary = $"{existing.Name} @ {existing.CustomerName}",
                    BeforeJson = AuditJson.Of(existing)
                },
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Contact Map(SqliteDataReader reader) => new()
    {
        Id = reader.Str(0),
        CustomerId = reader.Str(1),
        CustomerName = reader.Str(2),
        Name = reader.Str(3),
        Title = reader.Str(4),
        Phone = reader.Str(5),
        Email = reader.Str(6),
        WeChat = reader.Str(7),
        IsPrimary = reader.Bool(8),
        Note = reader.Str(9),
        CreatedUtc = reader.Dto(10),
        UpdatedUtc = reader.Dto(11)
    };
}