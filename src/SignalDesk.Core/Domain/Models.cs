namespace SignalDesk.Core.Domain;

public enum LineStatus { Active, Suspended }

public sealed class Line
{
    public required string Number { get; init; }
    public required string UserName { get; init; }
    public required string Relationship { get; init; }
    public required string PlanId { get; set; }
    public required string Device { get; init; }
    public LineStatus Status { get; set; } = LineStatus.Active;
    public string? SuspendReason { get; set; }
    public bool DeviceBlocked { get; set; }
    public List<string> TravelPasses { get; } = [];
    public string Last4 => Number[^4..];
}

public sealed class FiberService
{
    public required string PlanId { get; init; }
    public required int DropsLast24h { get; set; }
    public required decimal OpticalSignalDbm { get; set; }
    public string RouterFirmware { get; set; } = "4.2.1";
}

public sealed record BillItem(string Description, decimal Amount, string? Line = null);

public sealed record Bill(string Period, DateOnly DueDate, IReadOnlyList<BillItem> Items)
{
    public decimal Total => Items.Sum(i => i.Amount);
}

public sealed record Payment(DateOnly Date, decimal Amount, string Method, bool Late);

public sealed record Credit(DateOnly Date, decimal Amount, string Reason);

public sealed record PaymentArrangement(decimal DownPayment, int Installments, decimal InstallmentAmount, DateOnly FirstInstallmentDue);

public sealed record Appointment(string Id, string SlotLabel, string Issue);

public sealed class Customer
{
    public required string AccountId { get; init; }
    public required string Name { get; init; }
    public required string Phone { get; init; }
    public required string Pin { get; init; }
    public required string Email { get; set; }
    public required string ServiceZip { get; init; }
    public required int TenureYears { get; init; }
    public required List<Line> Lines { get; init; }
    public FiberService? Fiber { get; init; }
    public required List<Bill> Bills { get; init; }
    public required List<Payment> Payments { get; init; }
    public decimal PastDue { get; set; }
    public List<Credit> Credits { get; } = [];
    public PaymentArrangement? Arrangement { get; set; }
    public List<Appointment> Appointments { get; } = [];
    public string? RetentionOffer { get; set; }
    public DateOnly? CancellationDate { get; set; }

    public string FirstName => Name.Split(' ')[0];
    public Bill CurrentBill => Bills[^1];
    public Bill? PreviousBill => Bills.Count > 1 ? Bills[^2] : null;
    public decimal Balance => CurrentBill.Total + PastDue - Credits.Sum(c => c.Amount);
}

public sealed record Plan(string Id, string Name, decimal MonthlyPrice, string Data, string Highlights, bool IsFiber = false);

public sealed record TravelPass(string Id, string Name, decimal Price, string Coverage, string Details);

public sealed record Outage(string Zip, string Service, string Summary, string Eta);

public sealed record TechnicianSlot(string Id, string Label);

public sealed record RetentionOffer(string Id, string Title, string Details, decimal MonthlySavings, int Months);
