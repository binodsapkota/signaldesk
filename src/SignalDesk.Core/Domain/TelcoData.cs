namespace SignalDesk.Core.Domain;

/// <summary>
/// Sandbox copy of the carrier's systems of record. Every conversation gets a fresh copy so demo
/// scenarios are repeatable and one customer's changes never leak into another session.
/// </summary>
public sealed class TelcoData
{
    public static readonly DateOnly Today = new(2026, 9, 26);

    public required List<Customer> Customers { get; init; }

    public Customer? FindByPhone(string digits) =>
        Customers.FirstOrDefault(c => c.Lines.Any(l => Digits(l.Number) == digits) || Digits(c.Phone) == digits);

    public static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());

    public static TelcoData CreateSeed() => new()
    {
        Customers =
        [
            new Customer
            {
                AccountId = "A-1001",
                Name = "Maya Chen",
                Phone = "415-555-0142",
                Pin = "4821",
                Email = "maya.chen@example.com",
                ServiceZip = "94107",
                TenureYears = 6,
                Lines =
                [
                    new Line { Number = "415-555-0142", UserName = "Maya Chen", Relationship = "account holder", PlanId = "unlimited_plus", Device = "iPhone 16 Pro" },
                    new Line { Number = "415-555-0199", UserName = "Leo Chen", Relationship = "son", PlanId = "unlimited", Device = "Pixel 9" },
                ],
                Fiber = new FiberService { PlanId = "fiber_1g", DropsLast24h = 14, OpticalSignalDbm = -27.8m, RouterFirmware = "4.2.1" },
                Bills =
                [
                    new Bill("August 2026", new DateOnly(2026, 8, 21),
                    [
                        new("Unlimited Plus - line 0142", 65m, "415-555-0142"),
                        new("Unlimited - line 0199", 50m, "415-555-0199"),
                        new("Fiber 1 Gig", 65m),
                        new("Taxes & surcharges", 12m),
                    ]),
                    new Bill("September 2026", new DateOnly(2026, 10, 21),
                    [
                        new("Unlimited Plus - line 0142", 65m, "415-555-0142"),
                        new("Unlimited - line 0199", 50m, "415-555-0199"),
                        new("Fiber 1 Gig", 65m),
                        new("Taxes & surcharges", 12m),
                        new("TravelPass Day - Canada (4 days x $10)", 40m, "415-555-0199"),
                        new("Late payment fee (August bill)", 7.49m),
                    ]),
                ],
                Payments =
                [
                    new(new DateOnly(2026, 7, 20), 192m, "Visa ****4410", false),
                    new(new DateOnly(2026, 8, 29), 192m, "Visa ****4410", true),
                ],
            },
            new Customer
            {
                AccountId = "A-1002",
                Name = "Daniel Okafor",
                Phone = "312-555-0177",
                Pin = "1937",
                Email = "d.okafor@example.com",
                ServiceZip = "60614",
                TenureYears = 2,
                Lines =
                [
                    new Line
                    {
                        Number = "312-555-0177", UserName = "Daniel Okafor", Relationship = "account holder", PlanId = "essentials",
                        Device = "Galaxy S24", Status = LineStatus.Suspended, SuspendReason = "non-payment",
                    },
                ],
                Bills =
                [
                    new Bill("August 2026", new DateOnly(2026, 8, 18),
                    [
                        new("Essentials - line 0177", 35m, "312-555-0177"),
                        new("Taxes & surcharges", 4.20m),
                    ]),
                    new Bill("September 2026", new DateOnly(2026, 9, 18),
                    [
                        new("Essentials - line 0177", 35m, "312-555-0177"),
                        new("Taxes & surcharges", 4.20m),
                        new("Late payment fee (August bill)", 8m),
                    ]),
                ],
                Payments =
                [
                    new(new DateOnly(2026, 7, 16), 39.20m, "Debit ****2290", false),
                ],
                PastDue = 39.20m,
            },
            new Customer
            {
                AccountId = "A-1003",
                Name = "Priya Raman",
                Phone = "206-555-0123",
                Pin = "5550",
                Email = "priya.raman@example.com",
                ServiceZip = "98101",
                TenureYears = 4,
                Lines =
                [
                    new Line { Number = "206-555-0123", UserName = "Priya Raman", Relationship = "account holder", PlanId = "unlimited", Device = "iPhone 15" },
                ],
                Fiber = new FiberService { PlanId = "fiber_300", DropsLast24h = 0, OpticalSignalDbm = -18.2m, RouterFirmware = "4.3.0" },
                Bills =
                [
                    new Bill("August 2026", new DateOnly(2026, 8, 25),
                    [
                        new("Unlimited - line 0123", 50m, "206-555-0123"),
                        new("Fiber 300", 45m),
                        new("Taxes & surcharges", 8.50m),
                    ]),
                    new Bill("September 2026", new DateOnly(2026, 9, 25),
                    [
                        new("Unlimited - line 0123", 50m, "206-555-0123"),
                        new("Fiber 300", 45m),
                        new("Taxes & surcharges", 8.50m),
                    ]),
                ],
                Payments =
                [
                    new(new DateOnly(2026, 8, 24), 103.50m, "Autopay ****8812", false),
                    new(new DateOnly(2026, 9, 24), 103.50m, "Autopay ****8812", false),
                ],
            },
        ],
    };
}
