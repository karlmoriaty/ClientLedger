namespace ClientLedger.Modules.Crm.Domain;

public sealed record QuoteTotals(
    decimal Subtotal,
    decimal OrderDiscount,
    decimal Taxable,
    decimal Tax,
    decimal Total)
{
    public static QuoteTotals Empty { get; } = new(0m, 0m, 0m, 0m, 0m);
}

/// <summary>
/// 报价计算。规则固定、可单测：
/// 行小计 = 数量 × 单价 × (1 - 行折扣%)；
/// 整单折扣作用于行小计之和，税额基于折扣后金额计算。
/// 一律四舍五入到分（MidpointRounding.AwayFromZero），与国内财务习惯一致。
/// </summary>
public static class QuoteCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static QuoteTotals Calculate(
        IEnumerable<QuoteLine> lines,
        decimal orderDiscountPercent,
        decimal taxPercent)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var subtotal = Round(lines.Sum(line => line.LineTotal));
        var orderDiscount = Round(subtotal * Clamp(orderDiscountPercent) / 100m);
        var taxable = Round(subtotal - orderDiscount);
        var tax = Round(taxable * Clamp(taxPercent) / 100m);
        var total = Round(taxable + tax);

        return new QuoteTotals(subtotal, orderDiscount, taxable, tax, total);
    }

    private static decimal Clamp(decimal percent) =>
        percent < 0m ? 0m : percent > 100m ? 100m : percent;

    public static string NextQuoteNumber(DateTimeOffset now, int sequence) =>
        $"Q{now.ToLocalTime():yyyyMM}-{sequence:000}";
}