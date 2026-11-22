namespace ClientLedger.Foundation.Platform;

/// <summary>
/// 时间源。所有业务代码通过它取时间，方便测试时固定时钟、也方便以后处理时区与回拨。
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly TodayLocal { get; }

    TimeZoneInfo LocalZone { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayLocal => DateOnly.FromDateTime(DateTime.Now);

    public TimeZoneInfo LocalZone => TimeZoneInfo.Local;
}

/// <summary>测试用可控时钟。</summary>
public sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public DateOnly TodayLocal => DateOnly.FromDateTime(UtcNow.ToLocalTime().DateTime);

    public TimeZoneInfo LocalZone => TimeZoneInfo.Local;
}