namespace SignalDesk.Core.Domain;

public static class Catalog
{
    public static readonly IReadOnlyList<Plan> Plans =
    [
        new("essentials", "Essentials", 35m, "15 GB", "Talk & text, 15 GB high-speed data"),
        new("unlimited", "Unlimited", 50m, "Unlimited", "Unlimited data, 5 GB hotspot, SD streaming"),
        new("unlimited_plus", "Unlimited Plus", 65m, "Unlimited", "50 GB hotspot, HD streaming, 5G+ priority"),
        new("unlimited_max", "Unlimited Max", 80m, "Unlimited", "Everything in Plus, 100 GB hotspot, travel data in 210 countries"),
        new("fiber_300", "Fiber 300", 45m, "300 Mbps", "Symmetrical 300 Mbps home fiber", IsFiber: true),
        new("fiber_1g", "Fiber 1 Gig", 65m, "1 Gbps", "Symmetrical 1 Gbps home fiber, Wi-Fi 7 router", IsFiber: true),
    ];

    public static readonly IReadOnlyList<TravelPass> TravelPasses =
    [
        new("day_pass", "TravelPass Day", 10m, "Worldwide", "$10 per day you use your phone abroad; uses your plan's data, talk and text"),
        new("global_30", "Global 30-Day Pass", 50m, "Worldwide except North America", "30 days, 15 GB high-speed data, unlimited texts, 100 minutes of calls"),
    ];

    public static readonly IReadOnlyList<string> NorthAmerica = ["canada", "mexico"];

    public static readonly IReadOnlyList<Outage> Outages =
    [
        new("10001", "mobile", "Tower maintenance in Midtown Manhattan is degrading 5G coverage", "Today 6:00 PM"),
        new("73301", "fiber", "Fiber cut on Congress Ave affecting home internet", "Tomorrow 10:00 AM"),
    ];

    public static readonly IReadOnlyList<TechnicianSlot> TechnicianSlots =
    [
        new("S1", "Tue Sep 29, 8:00 AM - 12:00 PM"),
        new("S2", "Tue Sep 29, 1:00 PM - 5:00 PM"),
        new("S3", "Wed Sep 30, 8:00 AM - 12:00 PM"),
    ];

    public static Plan? FindPlan(string id) => Plans.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static TravelPass? FindPass(string id) => TravelPasses.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
