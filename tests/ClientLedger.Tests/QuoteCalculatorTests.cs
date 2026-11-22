using ClientLedger.Modules.Crm.Domain;

namespace ClientLedger.Tests;

public sealed class QuoteCalculatorTests
{
    [Fact]
    public void Calculate_AppliesLineDiscountThenOrderDiscountThenTax()
    {
        var lines = new[]
        {
            new QuoteLine { Name = "A", Quantity = 2m, UnitPrice = 100m, DiscountPercent = 10m },
            new QuoteLine { Name = "B", Quantity = 1m, UnitPrice = 50m, DiscountPercent = 0m }
        };

        var totals = QuoteCalculator.Calculate(lines, orderDiscountPercent: 5m, taxPercent: 13m);

        Assert.Equal(230m, totals.Subtotal);
        Assert.Equal(11.50m, totals.OrderDiscount);
        Assert.Equal(218.50m, totals.Taxable);
        Assert.Equal(28.41m, totals.Tax);
        Assert.Equal(246.91m, totals.Total);
    }

    [Fact]
    public void Round_UsesAwayFromZeroAtCentPrecision()
    {
        Assert.Equal(0.13m, QuoteCalculator.Round(0.125m));
        Assert.Equal(2.35m, QuoteCalculator.Round(2.345m));
    }

    [Fact]
    public void Calculate_EmptyLines_ReturnsZero()
    {
        var totals = QuoteCalculator.Calculate([], 10m, 13m);

        Assert.Equal(0m, totals.Subtotal);
        Assert.Equal(0m, totals.Total);
    }

    [Fact]
    public void Calculate_ClampsPercentagesToValidRange()
    {
        var lines = new[] { new QuoteLine { Name = "A", Quantity = 1m, UnitPrice = 100m } };

        var over = QuoteCalculator.Calculate(lines, orderDiscountPercent: 150m, taxPercent: -5m);
        Assert.Equal(100m, over.OrderDiscount);
        Assert.Equal(0m, over.Tax);
        Assert.Equal(0m, over.Total);
    }

    [Fact]
    public void Calculate_LineDiscountOfHundredPercent_ZeroesLine()
    {
        var lines = new[] { new QuoteLine { Name = "赠品", Quantity = 3m, UnitPrice = 80m, DiscountPercent = 100m } };

        var totals = QuoteCalculator.Calculate(lines, 0m, 13m);

        Assert.Equal(0m, totals.Subtotal);
        Assert.Equal(0m, totals.Total);
    }
}