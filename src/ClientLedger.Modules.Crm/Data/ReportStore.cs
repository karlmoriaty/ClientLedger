using ClientLedger.Data;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;

namespace ClientLedger.Modules.Crm.Data;

public sealed record FunnelItem(CustomerStatus Status, string Label, int Count, decimal Percent)
{
    public string CountText => Count.ToString();
}

public sealed record TrendPoint(string Label, DateTimeOffset WeekStartUtc, int Count);

public sealed record QuotePipeline(
    int Total,
    int Accepted,
    int Rejected,
    int Pending,
    decimal AcceptedAmount,
    decimal WinRatePercent);

public sealed record MonthAmount(string Label, decimal Amount, int Count);

/// <summary>
/// 报表查询。全部走"先取明细、再内存聚合"：
/// 单机 CRM 的明细量在几千条量级，内存聚合比在 SQL 里拼复杂窗口函数更可读、更好测，
/// 也避免 SQLite 字符串存 decimal 带来的精度陷阱。
/// </summary>
public sealed class ReportStore(
    IDbConnectionFactory factory,
    IPermissionService permissions,
    IUserContext userContext,
    IClock clock)
{
    public async Task<IReadOnlyList<FunnelItem>> FunnelAsync(CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmReportRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var scope = userContext.Current.Scope == DataScope.Owned ? " AND owner = $actor" : string.Empty;
        var parameters = userContext.Current.Scope == DataScope.Owned
            ? new (string, object?)[] { ("$actor", userContext.Current.Actor) }
            : [];

        var rows = await connection.QueryAsync(
            null,
            $"SELECT status, COUNT(1) FROM crm_customer WHERE is_deleted = 0{scope} GROUP BY status;",
            reader => (Status: reader.Str(0), Count: reader.GetInt32(1)),
            cancellationToken,
            parameters).ConfigureAwait(false);

        var total = rows.Sum(r => r.Count);
        var map = rows.ToDictionary(r => r.Status, r => r.Count, StringComparer.Ordinal);

        return CrmLabels.CustomerStatuses
            .Select(status =>
            {
                var count = map.GetValueOrDefault(status.ToString(), 0);
                var percent = total == 0 ? 0m : Math.Round(count * 100m / total, 1, MidpointRounding.AwayFromZero);
                return new FunnelItem(status, CrmLabels.Text(status), count, percent);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<TrendPoint>> ActivityTrendAsync(
        int weeks = 8,
        CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmReportRead);

        var today = DateOnly.FromDateTime(clock.UtcNow.ToLocalTime().DateTime);
        var mondayOffset = ((int)today.DayOfWeek + 6) % 7;
        var currentMonday = today.AddDays(-mondayOffset);
        var firstMonday = currentMonday.AddDays(-7 * (weeks - 1));

        var fromUtc = new DateTimeOffset(
            firstMonday.ToDateTime(TimeOnly.MinValue),
            TimeZoneInfo.Local.GetUtcOffset(firstMonday.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var scope = userContext.Current.Scope == DataScope.Owned ? " AND c.owner = $actor" : string.Empty;
        var parameters = userContext.Current.Scope == DataScope.Owned
            ? new (string, object?)[] { ("$from", SqliteValue.Db(fromUtc)), ("$actor", userContext.Current.Actor) }
            : [("$from", SqliteValue.Db(fromUtc))];

        var occurred = await connection.QueryAsync(
            null,
            $"""
            SELECT a.occurred_utc
            FROM crm_activity a
            JOIN crm_customer c ON c.id = a.customer_id
            WHERE a.is_deleted = 0 AND c.is_deleted = 0 AND a.occurred_utc >= $from{scope};
            """,
            reader => reader.Dto(0),
            cancellationToken,
            parameters).ConfigureAwait(false);

        var points = new List<TrendPoint>(weeks);
        for (var i = 0; i < weeks; i++)
        {
            var start = firstMonday.AddDays(i * 7);
            var end = start.AddDays(7);
            var count = occurred.Count(d =>
            {
                var local = DateOnly.FromDateTime(d.ToLocalTime().DateTime);
                return local >= start && local < end;
            });

            points.Add(new TrendPoint($"{start.Month:00}-{start.Day:00}", start.ToDateTime(TimeOnly.MinValue), count));
        }

        return points;
    }

    public async Task<QuotePipeline> QuotePipelineAsync(CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmReportRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var scope = userContext.Current.Scope == DataScope.Owned ? " AND c.owner = $actor" : string.Empty;
        var parameters = userContext.Current.Scope == DataScope.Owned
            ? new (string, object?)[] { ("$actor", userContext.Current.Actor) }
            : [];

        var rows = await connection.QueryAsync(
            null,
            $"""
            SELECT q.id, q.status, q.discount_percent, q.tax_percent
            FROM crm_quote q
            JOIN crm_customer c ON c.id = q.customer_id
            WHERE q.is_deleted = 0 AND c.is_deleted = 0{scope};
            """,
            reader => (Id: reader.Str(0), Status: reader.Str(1), Discount: reader.Dec(2), Tax: reader.Dec(3)),
            cancellationToken,
            parameters).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return new QuotePipeline(0, 0, 0, 0, 0m, 0m);
        }

        var lines = await connection.QueryAsync(
            null,
            """
            SELECT l.quote_id, l.name, l.spec, l.unit, l.quantity, l.unit_price, l.discount_percent
            FROM crm_quote_line l
            WHERE l.quote_id IN (SELECT id FROM crm_quote WHERE is_deleted = 0);
            """,
            reader => (QuoteId: reader.Str(0), Line: new QuoteLine
            {
                QuoteId = reader.Str(0),
                Name = reader.Str(1),
                Spec = reader.Str(2),
                Unit = reader.Str(3),
                Quantity = reader.Dec(4),
                UnitPrice = reader.Dec(5),
                DiscountPercent = reader.Dec(6)
            }),
            cancellationToken).ConfigureAwait(false);

        var linesByQuote = lines
            .GroupBy(l => l.QuoteId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QuoteLine>)g.Select(x => x.Line).ToList(), StringComparer.Ordinal);

        var accepted = 0;
        var rejected = 0;
        var pending = 0;
        var acceptedAmount = 0m;

        foreach (var row in rows)
        {
            var totals = QuoteCalculator.Calculate(
                linesByQuote.GetValueOrDefault(row.Id, []),
                row.Discount,
                row.Tax);

            switch (Enum.TryParse<QuoteStatus>(row.Status, out var status) ? status : QuoteStatus.Draft)
            {
                case QuoteStatus.Accepted:
                    accepted++;
                    acceptedAmount += totals.Total;
                    break;
                case QuoteStatus.Rejected:
                case QuoteStatus.Expired:
                    rejected++;
                    break;
                default:
                    pending++;
                    break;
            }
        }

        var decided = accepted + rejected;
        var winRate = decided == 0
            ? 0m
            : Math.Round(accepted * 100m / decided, 1, MidpointRounding.AwayFromZero);

        return new QuotePipeline(rows.Count, accepted, rejected, pending, acceptedAmount, winRate);
    }

    public async Task<IReadOnlyList<MonthAmount>> AcceptedByMonthAsync(
        int months = 6,
        CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmReportRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var scope = userContext.Current.Scope == DataScope.Owned ? " AND c.owner = $actor" : string.Empty;
        var parameters = userContext.Current.Scope == DataScope.Owned
            ? new (string, object?)[] { ("$actor", userContext.Current.Actor) }
            : [];

        // 以 updated_utc 近似"成交时间"：M0 没有单独记录状态流转时间，这是已知的精度损失。
        var rows = await connection.QueryAsync(
            null,
            $"""
            SELECT q.id, q.discount_percent, q.tax_percent, q.updated_utc
            FROM crm_quote q
            JOIN crm_customer c ON c.id = q.customer_id
            WHERE q.is_deleted = 0 AND c.is_deleted = 0 AND q.status = 'Accepted'{scope};
            """,
            reader => (Id: reader.Str(0), Discount: reader.Dec(1), Tax: reader.Dec(2), Updated: reader.Dto(3)),
            cancellationToken,
            parameters).ConfigureAwait(false);

        var lines = await connection.QueryAsync(
            null,
            """
            SELECT l.quote_id, l.name, l.spec, l.unit, l.quantity, l.unit_price, l.discount_percent
            FROM crm_quote_line l
            WHERE l.quote_id IN (SELECT id FROM crm_quote WHERE is_deleted = 0 AND status = 'Accepted');
            """,
            reader => (QuoteId: reader.Str(0), Line: new QuoteLine
            {
                QuoteId = reader.Str(0),
                Name = reader.Str(1),
                Spec = reader.Str(2),
                Unit = reader.Str(3),
                Quantity = reader.Dec(4),
                UnitPrice = reader.Dec(5),
                DiscountPercent = reader.Dec(6)
            }),
            cancellationToken).ConfigureAwait(false);

        var linesByQuote = lines
            .GroupBy(l => l.QuoteId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QuoteLine>)g.Select(x => x.Line).ToList(), StringComparer.Ordinal);

        var buckets = new Dictionary<string, (decimal Amount, int Count)>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var totals = QuoteCalculator.Calculate(
                linesByQuote.GetValueOrDefault(row.Id, []),
                row.Discount,
                row.Tax);

            var key = row.Updated.ToLocalTime().ToString("yyyy-MM");
            var current = buckets.GetValueOrDefault(key);
            buckets[key] = (current.Amount + totals.Total, current.Count + 1);
        }

        var firstMonth = clock.TodayLocal.AddMonths(-(months - 1));
        var result = new List<MonthAmount>(months);

        for (var i = 0; i < months; i++)
        {
            var month = firstMonth.AddMonths(i);
            var key = month.ToString("yyyy-MM");
            var bucket = buckets.GetValueOrDefault(key);
            result.Add(new MonthAmount($"{month.Month:00} 月", bucket.Amount, bucket.Count));
        }

        return result;
    }
}