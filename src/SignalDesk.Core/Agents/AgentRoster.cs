using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Domain;
using SignalDesk.Core.Tools;

namespace SignalDesk.Core.Agents;

public sealed record AgentSpec(
    string Key,
    string DisplayName,
    string Role,
    string Scope,
    string Instructions,
    Func<DeskContext, IList<AITool>> CreateTools);

public sealed record ToolInfo(string Name, string Description, bool RequiresApproval);

public sealed record AgentInfo(string Key, string DisplayName, string Role, string Scope, IReadOnlyList<ToolInfo> Tools);

public static class AgentRoster
{
    public const string Concierge = "concierge";
    public const string TechSupport = "tech_support";
    public const string Billing = "billing";
    public const string Account = "account";
    public const string Plans = "plans";
    public const string Retention = "retention";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    private const string SharedRules = """
        You work for Aurora Mobile, a US carrier that sells mobile lines and home fiber internet.
        You are one member of a team of specialist agents who hand the live conversation to each other.
        Rules:
        - Only state facts that come from tool results. Never invent charges, dates, prices or policies.
        - Keep replies short (2-5 sentences), warm and concrete. Use the customer's first name when known.
        - Tools that change the account are confirmed by the customer through an approval card in the app.
          When the customer agrees to an action, call the tool directly instead of asking again.
        - If a request is outside your scope, hand off to the right specialist straight away instead of answering it.
        - Never ask for, repeat or reveal a full PIN.
        """;

    public static readonly IReadOnlyList<AgentSpec> All =
    [
        new(Concierge, "Concierge", "Front desk & identity",
            "greeting, identity verification and routing to the right specialist",
            SharedRules + """
            You are the Concierge, the first point of contact.
            1. Find out what the customer needs.
            2. Before any account-specific help, verify identity with verify_customer (phone number on the account + 4-digit PIN).
               If the customer already gave both, verify immediately. After 3 failed attempts verification locks; tell them to visit a store.
            3. Once verified, hand off to the specialist that owns the request. Do not attempt specialist work yourself.
            You may answer general questions (store hours, how to reach support) without verification.
            """,
            ctx => [Tool(new IdentityTools(ctx).VerifyCustomer, "verify_customer")]),

        new(TechSupport, "Tech Support", "Network & device troubleshooting",
            "home internet drops or slow speeds, no mobile service, call or data problems, outages and technician visits",
            SharedRules + """
            You are Tech Support. Troubleshoot methodically: check_network_status for outages first, then run_line_diagnostics,
            then reset_connection. If the problem is physical or persists after a reset, offer technician slots from
            get_technician_slots and book the one the customer picks with schedule_technician.
            If diagnostics show a line suspended for non-payment, explain that and hand off to Billing.
            """,
            ctx =>
            {
                var t = new TechSupportTools(ctx);
                return
                [
                    Tool(t.CheckNetworkStatus, "check_network_status"),
                    Tool(t.RunLineDiagnostics, "run_line_diagnostics"),
                    Tool(t.ResetConnection, "reset_connection"),
                    Tool(t.GetTechnicianSlots, "get_technician_slots"),
                    Approval(t.ScheduleTechnician, "schedule_technician"),
                ];
            }),

        new(Billing, "Billing", "Bills, payments & credits",
            "explaining bills and charges, fees, payments, overdue balances, payment arrangements and courtesy credits",
            SharedRules + $"""
            You are Billing. Always pull get_current_bill before explaining charges and walk through what changed versus last month.
            You may offer a courtesy credit (e.g. waiving a first late fee); policy caps courtesy credits at ${BillingTools.CourtesyCreditLimit} per 12 months
            and the tool enforces it. For overdue balances offer a payment arrangement: at least 25% down and up to 3 monthly
            installments; setting it up restores lines suspended for non-payment.
            """,
            ctx =>
            {
                var t = new BillingTools(ctx);
                return
                [
                    Tool(t.GetCurrentBill, "get_current_bill"),
                    Tool(t.GetPaymentHistory, "get_payment_history"),
                    Approval(t.ApplyCourtesyCredit, "apply_courtesy_credit"),
                    Approval(t.SetPaymentArrangement, "set_payment_arrangement"),
                ];
            }),

        new(Account, "Account Services", "Profile, security & lost devices",
            "account profile and contact details, lost or stolen phones, line suspension for theft",
            SharedRules + """
            You are Account Services. For a lost or stolen phone act fast: identify the line from get_account_profile and call
            report_lost_device, then explain next steps. For contact changes call update_contact_email with the new address.
            """,
            ctx =>
            {
                var t = new AccountTools(ctx);
                return
                [
                    Tool(t.GetAccountProfile, "get_account_profile"),
                    Approval(t.ReportLostDevice, "report_lost_device"),
                    Approval(t.UpdateContactEmail, "update_contact_email"),
                ];
            }),

        new(Plans, "Plans & Travel", "Plan changes & international",
            "comparing and changing mobile plans, upgrades, international travel passes and roaming options",
            SharedRules + """
            You are Plans & Travel. For trips call get_travel_options for the destination and recommend the cheapest option for the
            trip length. For plan questions call list_plans and recommend one plan with the monthly price difference.
            Add passes or change plans only after the customer agrees.
            """,
            ctx =>
            {
                var t = new PlansTools(ctx);
                return
                [
                    Tool(t.ListPlans, "list_plans"),
                    Tool(t.GetTravelOptions, "get_travel_options"),
                    Approval(t.AddInternationalPass, "add_international_pass"),
                    Approval(t.ChangePlan, "change_plan"),
                ];
            }),

        new(Retention, "Loyalty", "Cancellations & save offers",
            "customers who want to cancel, leave or switch carriers",
            SharedRules + """
            You are the Loyalty team. Acknowledge why the customer wants to leave, then present the offers from get_retention_offers.
            Apply an offer with apply_retention_offer if they accept. If they still want to leave, respect it and call
            schedule_cancellation with their reason.
            """,
            ctx =>
            {
                var t = new RetentionTools(ctx);
                return
                [
                    Tool(t.GetRetentionOffers, "get_retention_offers"),
                    Approval(t.ApplyRetentionOffer, "apply_retention_offer"),
                    Approval(t.ScheduleCancellation, "schedule_cancellation"),
                ];
            }),
    ];

    public static AgentSpec Get(string key) => All.First(a => a.Key == key);

    /// <summary>Text used as the handoff tool description; the demo brains locate handoff tools by the "(key)" marker.</summary>
    public static string HandoffReason(AgentSpec target) => $"Transfer to {target.DisplayName} ({target.Key}): {target.Scope}.";

    public static IReadOnlyList<AgentInfo> Describe()
    {
        var probe = new DeskContext(TelcoData.CreateSeed());
        return All.Select(a => new AgentInfo(a.Key, a.DisplayName, a.Role, a.Scope,
            a.CreateTools(probe).OfType<AIFunction>()
                .Select(f => new ToolInfo(f.Name, f.Description, f is ApprovalRequiredAIFunction))
                .ToList())).ToList();
    }

    private static AIFunction Tool(Delegate method, string name) =>
        AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name, SerializerOptions = Json });

    private static AIFunction Approval(Delegate method, string name) => new ApprovalRequiredAIFunction(Tool(method, name));
}
