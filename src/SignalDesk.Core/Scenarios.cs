namespace SignalDesk.Core;

/// <summary>A scripted customer journey for the UI. Steps are sent in order; approvals appear in between as cards.</summary>
public sealed record Scenario(string Id, string Title, string Customer, string Summary, IReadOnlyList<string> Agents, IReadOnlyList<string> Steps);

public sealed record DemoCredential(string Name, string Phone, string Pin, string Profile);

public static class Scenarios
{
    public static readonly IReadOnlyList<Scenario> All =
    [
        new("internet-drops", "Home internet keeps dropping", "Maya Chen",
            "Diagnostics, a remote reset, then a technician visit. Maya then asks about her bill and Tech Support hands her to Billing.",
            ["concierge", "tech_support", "billing"],
            [
                "My home internet keeps dropping every evening.",
                "Sure, it's 415-555-0142 and my PIN is 4821.",
                "Tuesday morning works.",
                "Thanks! Also, why is my bill so much higher this month?",
                "Yes please, waive it.",
            ]),
        new("no-service", "No service on my phone", "Daniel Okafor",
            "Tech Support finds the line suspended for non-payment and hands off to Billing, who sets up a payment arrangement.",
            ["concierge", "tech_support", "billing"],
            [
                "My phone has had no service since yesterday.",
                "312-555-0177, PIN 1937",
                "Yes, set that up.",
            ]),
        new("bill-shock", "Why did my bill jump?", "Maya Chen",
            "Billing explains a travel charge and a late fee, then offers a courtesy credit (capped by policy in code).",
            ["concierge", "billing"],
            [
                "Why did my bill jump this month? My number is 415-555-0142, PIN 4821.",
                "Yes please, waive the late fee.",
            ]),
        new("japan-trip", "Traveling to Japan", "Priya Raman",
            "Plans & Travel compares passes for a 10-day trip and adds the cheaper one.",
            ["concierge", "plans"],
            [
                "I'm traveling to Japan for 10 days next month. What are my options?",
                "206-555-0123, PIN 5550",
                "Yes, add the 30-day pass.",
            ]),
        new("stolen-phone", "Son's phone was stolen", "Maya Chen",
            "Account Services identifies the right line and suspends it and blocks the device after approval.",
            ["concierge", "account"],
            [
                "My son's phone was just stolen! My number is 415-555-0142, PIN 4821.",
            ]),
        new("cancel", "I want to cancel", "Priya Raman",
            "The Loyalty team presents save offers based on tenure and applies the one Priya picks.",
            ["concierge", "retention"],
            [
                "I want to cancel my service, I found a cheaper deal.",
                "206-555-0123, PIN 5550",
                "The loyalty discount sounds good.",
            ]),
        new("wrong-pin", "Guardrail: failed verification", "Unknown caller",
            "Someone without the PIN tries to get account details. No specialist or account tool is ever reached.",
            ["concierge"],
            [
                "What's the balance on 415-555-0142?",
                "The PIN is 1111.",
                "Try 2222.",
            ]),
    ];

    public static readonly IReadOnlyList<DemoCredential> Credentials =
    [
        new("Maya Chen", "415-555-0142", "4821", "2 lines + Fiber 1 Gig, bill went up, fiber signal is weak"),
        new("Daniel Okafor", "312-555-0177", "1937", "1 line, suspended for non-payment"),
        new("Priya Raman", "206-555-0123", "5550", "1 line + Fiber 300, 4-year customer"),
    ];
}
