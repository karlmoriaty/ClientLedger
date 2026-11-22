namespace ClientLedger.Modules.Crm.Domain;

public enum CustomerStatus
{
    Lead = 0,
    Qualified = 1,
    Negotiating = 2,
    Won = 3,
    Lost = 4
}

public enum CustomerGrade
{
    A = 0,
    B = 1,
    C = 2,
    D = 3
}

public enum ActivityKind
{
    Call = 0,
    Visit = 1,
    Email = 2,
    WeChat = 3,
    Meeting = 4,
    Other = 5
}

public enum ScheduleKind
{
    Task = 0,
    Meeting = 1,
    FollowUp = 2,
    Reminder = 3
}

public enum ScheduleState
{
    Pending = 0,
    Done = 1,
    Canceled = 2
}

public enum QuoteStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Rejected = 3,
    Expired = 4
}

/// <summary>
/// 枚举 → 中文文案。集中在一处，避免每个页面各写一套翻译；
/// 以后做多语言时只需要把这里换成资源查找。
/// </summary>
public static class CrmLabels
{
    public static string Text(CustomerStatus status) => status switch
    {
        CustomerStatus.Lead => "线索",
        CustomerStatus.Qualified => "已确认",
        CustomerStatus.Negotiating => "洽谈中",
        CustomerStatus.Won => "已成交",
        CustomerStatus.Lost => "已流失",
        _ => "未知"
    };

    public static string Text(CustomerGrade grade) => grade switch
    {
        CustomerGrade.A => "A 类",
        CustomerGrade.B => "B 类",
        CustomerGrade.C => "C 类",
        _ => "D 类"
    };

    public static string Text(ActivityKind kind) => kind switch
    {
        ActivityKind.Call => "电话",
        ActivityKind.Visit => "拜访",
        ActivityKind.Email => "邮件",
        ActivityKind.WeChat => "微信",
        ActivityKind.Meeting => "会谈",
        _ => "其它"
    };

    public static string Text(ScheduleKind kind) => kind switch
    {
        ScheduleKind.Task => "待办",
        ScheduleKind.Meeting => "会议",
        ScheduleKind.FollowUp => "跟进",
        _ => "提醒"
    };

    public static string Text(ScheduleState state) => state switch
    {
        ScheduleState.Pending => "待完成",
        ScheduleState.Done => "已完成",
        _ => "已取消"
    };

    public static string Text(QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => "草稿",
        QuoteStatus.Sent => "已发出",
        QuoteStatus.Accepted => "已接受",
        QuoteStatus.Rejected => "已拒绝",
        _ => "已过期"
    };

    public static IReadOnlyList<CustomerStatus> CustomerStatuses { get; } =
        [CustomerStatus.Lead, CustomerStatus.Qualified, CustomerStatus.Negotiating, CustomerStatus.Won, CustomerStatus.Lost];

    public static IReadOnlyList<CustomerGrade> Grades { get; } =
        [CustomerGrade.A, CustomerGrade.B, CustomerGrade.C, CustomerGrade.D];

    public static IReadOnlyList<ActivityKind> ActivityKinds { get; } =
        [ActivityKind.Call, ActivityKind.Visit, ActivityKind.Email, ActivityKind.WeChat, ActivityKind.Meeting, ActivityKind.Other];

    public static IReadOnlyList<ScheduleKind> ScheduleKinds { get; } =
        [ScheduleKind.Task, ScheduleKind.Meeting, ScheduleKind.FollowUp, ScheduleKind.Reminder];

    public static IReadOnlyList<QuoteStatus> QuoteStatuses { get; } =
        [QuoteStatus.Draft, QuoteStatus.Sent, QuoteStatus.Accepted, QuoteStatus.Rejected, QuoteStatus.Expired];
}

public sealed record Customer
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Industry { get; init; } = string.Empty;

    public string Scale { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public CustomerGrade Grade { get; init; } = CustomerGrade.C;

    public CustomerStatus Status { get; init; } = CustomerStatus.Lead;

    public string Owner { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Website { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string Tags { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public string StatusText => CrmLabels.Text(Status);

    public string GradeText => CrmLabels.Text(Grade);

    public IReadOnlyList<string> TagList =>
        Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string Initials => string.IsNullOrWhiteSpace(Name)
        ? "?"
        : Name.Length <= 2 ? Name : Name[..2];

    public string OwnerShortName =>
        Owner.Contains('\\') ? Owner[(Owner.LastIndexOf('\\') + 1)..] : Owner;

    /// <summary>下拉框与无障碍朗读都依赖可读文本，不能落回类型名。</summary>
    public override string ToString() => Name;

    public string UpdatedText => Foundation.Platform.Formats.Day(UpdatedUtc);

    /// <summary>语义色令牌，交给 Shell 的转换器映射到主题画刷。</summary>
    public string StatusToken => Status switch
    {
        CustomerStatus.Won => "success",
        CustomerStatus.Lost => "danger",
        CustomerStatus.Negotiating => "accent",
        CustomerStatus.Qualified => "warning",
        _ => "neutral"
    };

    public string GradeToken => Grade switch
    {
        CustomerGrade.A => "accent",
        CustomerGrade.B => "success",
        CustomerGrade.C => "neutral",
        _ => "warning"
    };
}

public sealed record Contact
{
    public required string Id { get; init; }

    public required string CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public required string Name { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string WeChat { get; init; } = string.Empty;

    public bool IsPrimary { get; init; }

    public string Note { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public string PrimaryText => IsPrimary ? "主联系人" : string.Empty;
}

public sealed record Activity
{
    public required string Id { get; init; }

    public required string CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string? ContactId { get; init; }

    public string ContactName { get; init; } = string.Empty;

    public ActivityKind Kind { get; init; } = ActivityKind.Call;

    public DateTimeOffset OccurredUtc { get; init; }

    public required string Summary { get; init; }

    public string NextStep { get; init; } = string.Empty;

    public DateTimeOffset? NextFollowUtc { get; init; }

    public string Owner { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public string KindText => CrmLabels.Text(Kind);

    public string OccurredDay => Foundation.Platform.Formats.Day(OccurredUtc);

    public string OccurredMoment => Foundation.Platform.Formats.Moment(OccurredUtc);
}

public sealed record ScheduleItem
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public ScheduleKind Kind { get; init; } = ScheduleKind.Task;

    public string? CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public DateTimeOffset StartUtc { get; init; }

    public DateTimeOffset? EndUtc { get; init; }

    public int RemindMinutes { get; init; } = 15;

    public ScheduleState State { get; init; } = ScheduleState.Pending;

    public string Note { get; init; } = string.Empty;

    public string Owner { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public string KindText => CrmLabels.Text(Kind);

    public string StateText => CrmLabels.Text(State);

    public string StateToken => State switch
    {
        ScheduleState.Done => "success",
        ScheduleState.Canceled => "neutral",
        _ => "accent"
    };

    public string StartMoment => Foundation.Platform.Formats.Moment(StartUtc);

    public string StartDay => Foundation.Platform.Formats.Day(StartUtc);

    public string TimeRange
    {
        get
        {
            var start = StartUtc.ToLocalTime().ToString("HH:mm");
            return EndUtc is null ? start : $"{start} – {EndUtc.Value.ToLocalTime():HH:mm}";
        }
    }

    public string RemindText => Foundation.Platform.Formats.Duration(RemindMinutes);
}

public sealed record QuoteLine
{
    public string Id { get; init; } = string.Empty;

    public string QuoteId { get; init; } = string.Empty;

    public int Position { get; init; }

    public required string Name { get; init; }

    public string Spec { get; init; } = string.Empty;

    public string Unit { get; init; } = "件";

    public decimal Quantity { get; init; } = 1m;

    public decimal UnitPrice { get; init; }

    public decimal DiscountPercent { get; init; }

    public decimal LineTotal => QuoteCalculator.Round(Quantity * UnitPrice * (1m - DiscountPercent / 100m));

}

public sealed record Quote
{
    public required string Id { get; init; }

    public required string Number { get; init; }

    public required string CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public required string Title { get; init; }

    public QuoteStatus Status { get; init; } = QuoteStatus.Draft;

    public string Currency { get; init; } = "CNY";

    public decimal DiscountPercent { get; init; }

    public decimal TaxPercent { get; init; }

    public DateTimeOffset? ValidUntil { get; init; }

    public string Note { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public string StatusText => CrmLabels.Text(Status);

    public string ValidUntilText => ValidUntil is null ? "不限" : Foundation.Platform.Formats.Day(ValidUntil.Value);

    public string UpdatedText => Foundation.Platform.Formats.Day(UpdatedUtc);

    public string StatusToken => Status switch
    {
        QuoteStatus.Accepted => "success",
        QuoteStatus.Rejected => "danger",
        QuoteStatus.Sent => "accent",
        QuoteStatus.Expired => "warning",
        _ => "neutral"
    };
}

/// <summary>报价单 + 明细 + 合计，列表与编辑页共用。</summary>
public sealed record QuoteView(Quote Quote, IReadOnlyList<QuoteLine> Lines, QuoteTotals Totals)
{
    public decimal TotalAmount => Totals.Total;
}