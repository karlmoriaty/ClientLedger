using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Modules.Crm.Data;

public sealed record QuoteQuery
{
    public string? Search { get; init; }

    public QuoteStatus? Status { get; init; }
}

/// <summary>
/// 报价单。保存采用"主表 upsert + 明细整体替换"：
/// 明细行没有独立业务身份，整体替换比逐行 diff 更不容易出现半更新状态，
/// 且 before/after 快照在审计里就是完整的一张单据。
/// </summary>
public sealed class QuoteStore(
    IUnitOfWork unitOfWork,
    IDbConnectionFactory factory,
    IPermissionService permissions,
    IUserContext userContext,
    IAuditWriter audit,
    IClock clock)
{
    private const string QuoteColumns =
        "id, number, customer_id, title, status, currency, discount_percent, tax_percent, valid_until, note, created_utc, updated_utc";

    private const string QuoteSelect = """
        SELECT q.id, q.number, q.customer_id, c.name, q.title, q.status, q.currency,
               q.discount_percent, q.tax_percent, q.valid_until, q.note, q.created_utc, q.updated_utc
        FROM crm_quote q
        JOIN crm_customer c ON c.id = q.customer_id
        """;

    public async Task<IReadOnlyList<QuoteView>> ListAsync(
        QuoteQuery query,
        CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmQuoteRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var conditions = new List<string> { "q.is_deleted = 0", "c.is_deleted = 0" };
        var parameters = new List<(string, object?)>();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("(q.number LIKE $kw ESCAPE '\\' OR q.title LIKE $kw ESCAPE '\\' OR c.name LIKE $kw ESCAPE '\\')");
            parameters.Add(("$kw", $"%{CustomerStore.EscapeLike(query.Search)}%"));
        }

        if (query.Status is { } status)
        {
            conditions.Add("q.status = $status");
            parameters.Add(("$status", status.ToString()));
        }

        if (userContext.Current.Scope == DataScope.Owned)
        {
            conditions.Add("c.owner = $actor");
            parameters.Add(("$actor", userContext.Current.Actor));
        }

        var where = string.Join(" AND ", conditions);

        var quotes = await connection.QueryAsync(
            null,
            $"{QuoteSelect} WHERE {where} ORDER BY q.number DESC;",
            MapQuote,
            cancellationToken,
            parameters.ToArray()).ConfigureAwait(false);

        if (quotes.Count == 0)
        {
            return [];
        }

        var lines = await connection.QueryAsync(
            null,
            $"""
            SELECT l.id, l.quote_id, l.position, l.name, l.spec, l.unit, l.quantity, l.unit_price, l.discount_percent
            FROM crm_quote_line l
            WHERE l.quote_id IN (SELECT q.id FROM crm_quote q JOIN crm_customer c ON c.id = q.customer_id WHERE {where})
            ORDER BY l.quote_id, l.position;
            """,
            MapLine,
            cancellationToken,
            parameters.ToArray()).ConfigureAwait(false);

        var linesByQuote = lines
            .GroupBy(line => line.QuoteId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QuoteLine>)g.ToList(), StringComparer.Ordinal);

        return quotes
            .Select(quote => BuildView(quote, linesByQuote.GetValueOrDefault(quote.Id, [])))
            .ToList();
    }

    public async Task<QuoteView?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmQuoteRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var quote = await connection.QuerySingleAsync(
            null,
            $"{QuoteSelect} WHERE q.id = $id AND q.is_deleted = 0;",
            MapQuote,
            cancellationToken,
            ("$id", id)).ConfigureAwait(false);

        if (quote is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync(
            null,
            """
            SELECT id, quote_id, position, name, spec, unit, quantity, unit_price, discount_percent
            FROM crm_quote_line WHERE quote_id = $id ORDER BY position;
            """,
            MapLine,
            cancellationToken,
            ("$id", id)).ConfigureAwait(false);

        return BuildView(quote, lines);
    }

    public async Task<string> NextNumberAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await NextNumberCoreAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SaveAsync(QuoteView view, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmQuoteWrite);

        return await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await LoadViewAsync(connection, transaction, view.Quote.Id, cancellationToken).ConfigureAwait(false);

            var now = clock.UtcNow;
            var number = string.IsNullOrWhiteSpace(view.Quote.Number)
                ? await NextNumberCoreAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
                : view.Quote.Number;

            var quote = view.Quote with
            {
                Number = number,
                CreatedUtc = existing?.Quote.CreatedUtc ?? now,
                UpdatedUtc = now
            };

            await connection.ExecuteAsync(
                transaction,
                """
                INSERT INTO crm_quote
                    (id, number, customer_id, title, status, currency, discount_percent, tax_percent, valid_until, note, created_utc, updated_utc, is_deleted)
                VALUES
                    ($id, $number, $customer, $title, $status, $currency, $discount, $tax, $valid, $note, $created, $updated, 0)
                ON CONFLICT(id) DO UPDATE SET
                    number = excluded.number,
                    customer_id = excluded.customer_id,
                    title = excluded.title,
                    status = excluded.status,
                    currency = excluded.currency,
                    discount_percent = excluded.discount_percent,
                    tax_percent = excluded.tax_percent,
                    valid_until = excluded.valid_until,
                    note = excluded.note,
                    updated_utc = excluded.updated_utc,
                    is_deleted = 0;
                """,
                cancellationToken,
                ("$id", quote.Id),
                ("$number", quote.Number),
                ("$customer", quote.CustomerId),
                ("$title", quote.Title),
                ("$status", quote.Status.ToString()),
                ("$currency", quote.Currency),
                ("$discount", SqliteValue.Db(quote.DiscountPercent)),
                ("$tax", SqliteValue.Db(quote.TaxPercent)),
                ("$valid", SqliteValue.Db(quote.ValidUntil)),
                ("$note", quote.Note),
                ("$created", SqliteValue.Db(quote.CreatedUtc)),
                ("$updated", SqliteValue.Db(quote.UpdatedUtc))).ConfigureAwait(false);

            await connection.ExecuteAsync(
                transaction,
                "DELETE FROM crm_quote_line WHERE quote_id = $id;",
                cancellationToken,
                ("$id", quote.Id)).ConfigureAwait(false);

            var position = 0;
            foreach (var line in view.Lines)
            {
                await connection.ExecuteAsync(
                    transaction,
                    """
                    INSERT INTO crm_quote_line
                        (id, quote_id, position, name, spec, unit, quantity, unit_price, discount_percent)
                    VALUES
                        ($id, $quote, $position, $name, $spec, $unit, $qty, $price, $discount);
                    """,
                    cancellationToken,
                    ("$id", string.IsNullOrWhiteSpace(line.Id) ? Ids.New("ql") : line.Id),
                    ("$quote", quote.Id),
                    ("$position", position++),
                    ("$name", line.Name),
                    ("$spec", line.Spec),
                    ("$unit", line.Unit),
                    ("$qty", SqliteValue.Db(line.Quantity)),
                    ("$price", SqliteValue.Db(line.UnitPrice)),
                    ("$discount", SqliteValue.Db(line.DiscountPercent))).ConfigureAwait(false);
            }

            var after = BuildView(quote, view.Lines);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = existing is null ? "crm.quote.create" : "crm.quote.update",
                    EntityType = "quote",
                    EntityId = quote.Id,
                    Summary = $"{quote.Number} · {quote.CustomerName} · {Formats.Money(after.Totals.Total)}",
                    BeforeJson = AuditJson.Of(existing),
                    AfterJson = AuditJson.Of(after)
                },
                cancellationToken).ConfigureAwait(false);

            return quote.Id;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmQuoteDelete);

        await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await LoadViewAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return;
            }

            await connection.ExecuteAsync(
                transaction,
                "UPDATE crm_quote SET is_deleted = 1, updated_utc = $now WHERE id = $id;",
                cancellationToken,
                ("$id", id),
                ("$now", SqliteValue.Db(clock.UtcNow))).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = "crm.quote.delete",
                    EntityType = "quote",
                    EntityId = id,
                    Summary = $"{existing.Quote.Number} · {existing.Quote.CustomerName}",
                    BeforeJson = AuditJson.Of(existing)
                },
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<QuoteView?> LoadViewAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string id,
        CancellationToken cancellationToken)
    {
        var quote = await connection.QuerySingleAsync(
            transaction,
            $"{QuoteSelect} WHERE q.id = $id;",
            MapQuote,
            cancellationToken,
            ("$id", id)).ConfigureAwait(false);

        if (quote is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync(
            transaction,
            """
            SELECT id, quote_id, position, name, spec, unit, quantity, unit_price, discount_percent
            FROM crm_quote_line WHERE quote_id = $id ORDER BY position;
            """,
            MapLine,
            cancellationToken,
            ("$id", id)).ConfigureAwait(false);

        return BuildView(quote, lines);
    }

    private static async Task<string> NextNumberCoreAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var prefix = $"Q{DateTime.Now:yyyy-MM}-";

        var last = await connection.QuerySingleAsync(
            transaction,
            "SELECT number FROM crm_quote WHERE number LIKE $prefix ORDER BY number DESC LIMIT 1;",
            reader => reader.GetString(0),
            cancellationToken,
            ("$prefix", prefix + "%")).ConfigureAwait(false);

        var sequence = 1;
        if (last is not null
            && last.Length > prefix.Length
            && int.TryParse(last[prefix.Length..], out var parsed))
        {
            sequence = parsed + 1;
        }

        return prefix + sequence.ToString("000");
    }

    private static QuoteView BuildView(Quote quote, IReadOnlyList<QuoteLine> lines) =>
        new(quote, lines, QuoteCalculator.Calculate(lines, quote.DiscountPercent, quote.TaxPercent));

    private static Quote MapQuote(SqliteDataReader reader) => new()
    {
        Id = reader.Str(0),
        Number = reader.Str(1),
        CustomerId = reader.Str(2),
        CustomerName = reader.Str(3),
        Title = reader.Str(4),
        Status = Enum.TryParse<QuoteStatus>(reader.Str(5), out var status) ? status : QuoteStatus.Draft,
        Currency = reader.Str(6),
        DiscountPercent = reader.Dec(7),
        TaxPercent = reader.Dec(8),
        ValidUntil = reader.DtoOrNull(9),
        Note = reader.Str(10),
        CreatedUtc = reader.Dto(11),
        UpdatedUtc = reader.Dto(12)
    };

    private static QuoteLine MapLine(SqliteDataReader reader) => new()
    {
        Id = reader.Str(0),
        QuoteId = reader.Str(1),
        Position = reader.GetInt32(2),
        Name = reader.Str(3),
        Spec = reader.Str(4),
        Unit = reader.Str(5),
        Quantity = reader.Dec(6),
        UnitPrice = reader.Dec(7),
        DiscountPercent = reader.Dec(8)
    };
}