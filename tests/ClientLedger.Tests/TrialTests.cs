using System.IO;
using ClientLedger.Foundation.Platform;
using ClientLedger.Licensing;

namespace ClientLedger.Tests;

public sealed class TrialTests
{
    [Fact]
    public void FreshInstall_IsWithinTrialWindow()
    {
        using var scope = new TestScope();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero));
        var store = new TrialStore(scope.Paths, new TestIdentity(), clock, scope.Log);

        var record = store.Load();

        Assert.False(store.IsExpired(record));
        Assert.Equal(30, store.DaysLeft(record));
    }

    [Fact]
    public void AfterThirtyOneDays_TrialIsExpired()
    {
        using var scope = new TestScope();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero));
        var store = new TrialStore(scope.Paths, new TestIdentity(), clock, scope.Log);

        var record = store.Load();
        clock.UtcNow = clock.UtcNow.AddDays(31);

        Assert.True(store.IsExpired(record));
        Assert.Equal(0, store.DaysLeft(record));
    }

    [Fact]
    public void ClockRollback_IsDetected()
    {
        using var scope = new TestScope();
        var start = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(start);
        var store = new TrialStore(scope.Paths, new TestIdentity(), clock, scope.Log);

        store.Load();

        clock.UtcNow = start.AddDays(5);
        store.Load();

        clock.UtcNow = start.AddDays(1);
        var rolledBack = store.Load();

        Assert.True(rolledBack.ClockRollbackDetected);
        Assert.True(store.IsExpired(rolledBack));
    }

    [Fact]
    public void DeletingOneCopy_DoesNotResetTrial()
    {
        using var scope = new TestScope();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero));
        var store = new TrialStore(scope.Paths, new TestIdentity(), clock, scope.Log);

        var first = store.Load();

        var localCopy = Path.Combine(scope.Paths.DataDirectory, "trial.bin");
        File.Delete(localCopy);

        clock.UtcNow = clock.UtcNow.AddDays(10);
        var second = store.Load();

        Assert.Equal(first.FirstRunUtc, second.FirstRunUtc);
        Assert.Equal(20, store.DaysLeft(second));
    }
}