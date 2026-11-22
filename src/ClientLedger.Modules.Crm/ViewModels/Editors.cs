using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientLedger.Modules.Crm.ViewModels;

public sealed partial class CustomerEditor : ObservableObject
{
    public string Id { get; set; } = string.Empty;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _industry = string.Empty;
    [ObservableProperty] private string _scale = string.Empty;
    [ObservableProperty] private string _source = string.Empty;
    [ObservableProperty] private CustomerGrade _grade = CustomerGrade.C;
    [ObservableProperty] private CustomerStatus _status = CustomerStatus.Lead;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _website = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _tags = string.Empty;
    [ObservableProperty] private string _note = string.Empty;

    public bool IsNew => string.IsNullOrEmpty(Id);

    public string Heading => IsNew ? "新建客户" : "编辑客户";

    public IReadOnlyList<CustomerGrade> GradeOptions => CrmLabels.Grades;

    public IReadOnlyList<CustomerStatus> StatusOptions => CrmLabels.CustomerStatuses;

    public static CustomerEditor From(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Industry = customer.Industry,
        Scale = customer.Scale,
        Source = customer.Source,
        Grade = customer.Grade,
        Status = customer.Status,
        Phone = customer.Phone,
        Email = customer.Email,
        Website = customer.Website,
        Address = customer.Address,
        Tags = customer.Tags,
        Note = customer.Note
    };

    public Customer ToRecord(string owner) => new()
    {
        Id = IsNew ? Ids.New("cus") : Id,
        Name = Name.Trim(),
        Industry = Industry.Trim(),
        Scale = Scale.Trim(),
        Source = Source.Trim(),
        Grade = Grade,
        Status = Status,
        Owner = owner,
        Phone = Phone.Trim(),
        Email = Email.Trim(),
        Website = Website.Trim(),
        Address = Address.Trim(),
        Tags = Tags.Trim(),
        Note = Note.Trim()
    };
}

public sealed partial class ContactEditor : ObservableObject
{
    public string Id { get; set; } = string.Empty;

    [ObservableProperty] private string _customerId = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _weChat = string.Empty;
    [ObservableProperty] private bool _isPrimary;
    [ObservableProperty] private string _note = string.Empty;

    public bool IsNew => string.IsNullOrEmpty(Id);

    public string Heading => IsNew ? "新建联系人" : "编辑联系人";

    public static ContactEditor From(Contact contact) => new()
    {
        Id = contact.Id,
        CustomerId = contact.CustomerId,
        Name = contact.Name,
        Title = contact.Title,
        Phone = contact.Phone,
        Email = contact.Email,
        WeChat = contact.WeChat,
        IsPrimary = contact.IsPrimary,
        Note = contact.Note
    };

    public Contact ToRecord() => new()
    {
        Id = IsNew ? Ids.New("ct") : Id,
        CustomerId = CustomerId,
        Name = Name.Trim(),
        Title = Title.Trim(),
        Phone = Phone.Trim(),
        Email = Email.Trim(),
        WeChat = WeChat.Trim(),
        IsPrimary = IsPrimary,
        Note = Note.Trim()
    };
}

public sealed partial class ActivityEditor : ObservableObject
{
    public string Id { get; set; } = string.Empty;

    [ObservableProperty] private string _customerId = string.Empty;
    [ObservableProperty] private string _contactId = string.Empty;
    [ObservableProperty] private ActivityKind _kind = ActivityKind.Call;
    [ObservableProperty] private string _occurredText = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _nextStep = string.Empty;
    [ObservableProperty] private string _nextFollowText = string.Empty;

    public bool IsNew => string.IsNullOrEmpty(Id);

    public string Heading => IsNew ? "记录跟进" : "编辑跟进";

    public IReadOnlyList<ActivityKind> KindOptions => CrmLabels.ActivityKinds;

    public static ActivityEditor From(Activity activity) => new()
    {
        Id = activity.Id,
        CustomerId = activity.CustomerId,
        ContactId = activity.ContactId ?? string.Empty,
        Kind = activity.Kind,
        OccurredText = DateInput.Moment(activity.OccurredUtc),
        Summary = activity.Summary,
        NextStep = activity.NextStep,
        NextFollowText = activity.NextFollowUtc is null ? string.Empty : DateInput.Moment(activity.NextFollowUtc.Value)
    };

    public Activity ToRecord(string owner) => new()
    {
        Id = IsNew ? Ids.New("act") : Id,
        CustomerId = CustomerId,
        ContactId = string.IsNullOrWhiteSpace(ContactId) ? null : ContactId,
        Kind = Kind,
        OccurredUtc = DateInput.ParseMomentOrNow(OccurredText),
        Summary = Summary.Trim(),
        NextStep = NextStep.Trim(),
        NextFollowUtc = DateInput.ParseMomentOrNull(NextFollowText),
        Owner = owner
    };
}

public sealed partial class ScheduleEditor : ObservableObject
{
    public string Id { get; set; } = string.Empty;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private ScheduleKind _kind = ScheduleKind.Task;
    [ObservableProperty] private string _customerId = string.Empty;
    [ObservableProperty] private string _startText = string.Empty;
    [ObservableProperty] private string _endText = string.Empty;
    [ObservableProperty] private int _remindMinutes = 15;
    [ObservableProperty] private ScheduleState _state = ScheduleState.Pending;
    [ObservableProperty] private string _note = string.Empty;

    public bool IsNew => string.IsNullOrEmpty(Id);

    public string Heading => IsNew ? "新建日程" : "编辑日程";

    public IReadOnlyList<ScheduleKind> KindOptions => CrmLabels.ScheduleKinds;

    public IReadOnlyList<int> RemindOptions { get; } = [0, 5, 15, 30, 60, 120, 1440];

    public static ScheduleEditor From(ScheduleItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Kind = item.Kind,
        CustomerId = item.CustomerId ?? string.Empty,
        StartText = DateInput.Moment(item.StartUtc),
        EndText = item.EndUtc is null ? string.Empty : DateInput.Moment(item.EndUtc.Value),
        RemindMinutes = item.RemindMinutes,
        State = item.State,
        Note = item.Note
    };

    public ScheduleItem ToRecord(string owner) => new()
    {
        Id = IsNew ? Ids.New("sch") : Id,
        Title = Title.Trim(),
        Kind = Kind,
        CustomerId = string.IsNullOrWhiteSpace(CustomerId) ? null : CustomerId,
        StartUtc = DateInput.ParseMomentOrNow(StartText),
        EndUtc = DateInput.ParseMomentOrNull(EndText),
        RemindMinutes = RemindMinutes,
        State = State,
        Note = Note.Trim(),
        Owner = owner
    };
}

public sealed partial class QuoteLineEditor : ObservableObject
{
    public string Id { get; set; } = Ids.New("ql");

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _spec = string.Empty;
    [ObservableProperty] private string _unit = "件";
    [ObservableProperty] private decimal _quantity = 1m;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] private decimal _discountPercent;

    public decimal LineTotal => QuoteCalculator.Round(Quantity * UnitPrice * (1m - DiscountPercent / 100m));

    partial void OnQuantityChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));

    partial void OnUnitPriceChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));

    partial void OnDiscountPercentChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));

    public static QuoteLineEditor From(QuoteLine line) => new()
    {
        Id = string.IsNullOrWhiteSpace(line.Id) ? Ids.New("ql") : line.Id,
        Name = line.Name,
        Spec = line.Spec,
        Unit = line.Unit,
        Quantity = line.Quantity,
        UnitPrice = line.UnitPrice,
        DiscountPercent = line.DiscountPercent
    };

    public QuoteLine ToRecord(string quoteId) => new()
    {
        Id = Id,
        QuoteId = quoteId,
        Name = Name.Trim(),
        Spec = Spec.Trim(),
        Unit = Unit.Trim(),
        Quantity = Quantity,
        UnitPrice = UnitPrice,
        DiscountPercent = DiscountPercent
    };
}

public sealed partial class QuoteEditor : ObservableObject
{
    public QuoteEditor()
    {
        Lines.CollectionChanged += OnLinesChanged;
        Recalculate();
    }

    public string Id { get; set; } = string.Empty;

    [ObservableProperty] private string _number = string.Empty;
    [ObservableProperty] private string _customerId = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private QuoteStatus _status = QuoteStatus.Draft;
    [ObservableProperty] private decimal _discountPercent;
    [ObservableProperty] private decimal _taxPercent;
    [ObservableProperty] private string _validUntilText = string.Empty;
    [ObservableProperty] private string _note = string.Empty;

    [ObservableProperty] private decimal _subtotal;
    [ObservableProperty] private decimal _orderDiscount;
    [ObservableProperty] private decimal _taxable;
    [ObservableProperty] private decimal _tax;
    [ObservableProperty] private decimal _total;

    public bool IsNew => string.IsNullOrEmpty(Id);

    public string Heading => IsNew ? "新建报价" : "编辑报价";

    public IReadOnlyList<QuoteStatus> StatusOptions => CrmLabels.QuoteStatuses;

    public ObservableCollection<QuoteLineEditor> Lines { get; } = [];

    public static QuoteEditor From(QuoteView view)
    {
        var editor = new QuoteEditor
        {
            Id = view.Quote.Id,
            Number = view.Quote.Number,
            CustomerId = view.Quote.CustomerId,
            Title = view.Quote.Title,
            Status = view.Quote.Status,
            DiscountPercent = view.Quote.DiscountPercent,
            TaxPercent = view.Quote.TaxPercent,
            ValidUntilText = view.Quote.ValidUntil is null ? string.Empty : DateInput.Day(view.Quote.ValidUntil.Value),
            Note = view.Quote.Note
        };

        foreach (var line in view.Lines)
        {
            editor.Lines.Add(QuoteLineEditor.From(line));
        }

        editor.Recalculate();
        return editor;
    }

    public void AddLine() => Lines.Add(new QuoteLineEditor());

    public void RemoveLine(QuoteLineEditor line) => Lines.Remove(line);

    public QuoteView ToView(string customerName)
    {
        var id = IsNew ? Ids.New("qt") : Id;
        var lines = Lines.Select(line => line.ToRecord(id)).ToList();

        var quote = new Quote
        {
            Id = id,
            Number = Number.Trim(),
            CustomerId = CustomerId,
            CustomerName = customerName,
            Title = Title.Trim(),
            Status = Status,
            DiscountPercent = DiscountPercent,
            TaxPercent = TaxPercent,
            ValidUntil = DateInput.ParseDayOrNull(ValidUntilText),
            Note = Note.Trim()
        };

        return new QuoteView(quote, lines, QuoteCalculator.Calculate(lines, DiscountPercent, TaxPercent));
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (QuoteLineEditor item in e.OldItems)
            {
                item.PropertyChanged -= OnLinePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (QuoteLineEditor item in e.NewItems)
            {
                item.PropertyChanged += OnLinePropertyChanged;
            }
        }

        Recalculate();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    private void Recalculate()
    {
        var lines = Lines.Select(line => line.ToRecord(Id)).ToList();
        var totals = QuoteCalculator.Calculate(lines, DiscountPercent, TaxPercent);

        Subtotal = totals.Subtotal;
        OrderDiscount = totals.OrderDiscount;
        Taxable = totals.Taxable;
        Tax = totals.Tax;
        Total = totals.Total;
    }

    partial void OnDiscountPercentChanged(decimal value) => Recalculate();

    partial void OnTaxPercentChanged(decimal value) => Recalculate();
}