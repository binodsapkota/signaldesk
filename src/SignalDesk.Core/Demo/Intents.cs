using System.Text.RegularExpressions;
using SignalDesk.Core.Agents;

namespace SignalDesk.Core.Demo;

/// <summary>Keyword intent model used by the offline demo brains in place of an LLM's judgement.</summary>
internal static partial class Intents
{
    private static readonly (string Agent, Regex Pattern)[] Rules =
    [
        (AgentRoster.Retention, new(@"\bcancel|\bleav(e|ing)\b|switch(ing)? (carriers?|to another)|close my account|terminate|another carrier|cheaper (deal|offer)|better deal", RegexOptions.IgnoreCase)),
        (AgentRoster.Account, new(@"\bstolen\b|\blost\b.{0,20}\b(phone|device|iphone|pixel)\b|\b(phone|device)\b.{0,20}\blost\b|\bchange (my )?(contact )?email\b|\bupdate (my )?(contact )?email\b|\bnew email\b|contact (info|details)|account (profile|details)", RegexOptions.IgnoreCase)),
        (AgentRoster.TechSupport, new(@"\binternet\b|\bwi-?fi\b|\bdropp?(ing|s|ed)?\b|disconnect|\bslow\b|\boutage\b|no (service|signal|bars)|\bsignal\b|can'?t (make|place|receive) calls|calls? (keep )?(fail|drop)|data (is )?not working|\brouter\b|not connecting|keeps? (cutting|going) out|\bbuffering\b", RegexOptions.IgnoreCase)),
        (AgentRoster.Billing, new(@"\bbill(ed|ing)?\b|\bcharge[sd]?\b|\bfees?\b|\bpay(ment|ments)?\b|\bcredit\b|\brefund\b|\binvoice\b|past due|overdue|\bbalance\b|\bautopay\b|higher than usual", RegexOptions.IgnoreCase)),
        (AgentRoster.Plans, new(@"\bplans?\b|\bupgrade\b|\bdowngrade\b|\btravel(l?ing)?\b|\btrip\b|international|\broaming\b|\babroad\b|\bpass\b|more data|\bhotspot\b|japan|mexico|canada|europe|france|italy|germany|spain|india|united kingdom|\buk\b|korea|brazil|australia", RegexOptions.IgnoreCase)),
    ];

    /// <summary>Returns the specialist that owns the text, preferring the rule that matches most often, then rule order.</summary>
    public static string? Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string? best = null;
        int bestScore = 0;
        foreach ((string agent, Regex pattern) in Rules)
        {
            int score = pattern.Matches(text).Count;
            if (score > bestScore)
            {
                best = agent;
                bestScore = score;
            }
        }

        return best;
    }

    public static bool IsClosing(string text) => Closing().IsMatch(text);

    public static bool IsAffirmative(string text) => !IsNegative(text) && Affirmative().IsMatch(text);

    public static bool IsNegative(string text) => Negative().IsMatch(text);

    public static bool IsHomeInternet(string text) => HomeInternet().IsMatch(text);

    public static string? Phone(string text) => PhonePattern().Match(text) is { Success: true } m ? m.Value : null;

    public static string? Pin(string text)
    {
        if (PinPattern().Match(text) is { Success: true } m) return m.Groups[1].Value;
        string withoutPhone = PhonePattern().Replace(text, " ");
        return StandaloneFour().Match(withoutPhone) is { Success: true } s ? s.Value : null;
    }

    public static string? Email(string text) => EmailPattern().Match(text) is { Success: true } m ? m.Value : null;

    public static decimal? Amount(string text) =>
        AmountPattern().Match(text) is { Success: true } m && decimal.TryParse(m.Groups[1].Value, out decimal d) ? d : null;

    public static string? Country(string text)
    {
        string[] countries = ["Japan", "Mexico", "Canada", "France", "Italy", "Germany", "Spain", "India", "United Kingdom", "Korea", "Brazil", "Australia"];
        return countries.FirstOrDefault(c => text.Contains(c, StringComparison.OrdinalIgnoreCase))
            ?? (Regex.IsMatch(text, @"\buk\b|\blondon\b", RegexOptions.IgnoreCase) ? "United Kingdom" : null)
            ?? (text.Contains("europe", StringComparison.OrdinalIgnoreCase) ? "France" : null);
    }

    [GeneratedRegex(@"\b(thanks|thank you|that'?s all|that is all|bye|goodbye|nothing else|that'?s it|all set)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Closing();

    [GeneratedRegex(@"\b(yes|yeah|yep|sure|ok(ay)?|please do|go ahead|do it|sounds good|that works|works for me|let'?s do|set (it|that) up|add it|book it|take (it|the)|i'?ll take|perfect|great|waive)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Affirmative();

    [GeneratedRegex(@"^\s*(no|nope|nah)\b|\bno,? thanks\b|\bnot now\b|\bdon'?t\b|\brather not\b|\bjust cancel\b|\bstill want to cancel\b|\bnot interested\b|\bdecline\b", RegexOptions.IgnoreCase)]
    private static partial Regex Negative();

    [GeneratedRegex(@"\b(internet|wi-?fi|router|fiber|home|modem|streaming|buffering)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HomeInternet();

    [GeneratedRegex(@"\(?\b\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}\b")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\bpin\b\D{0,12}(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PinPattern();

    [GeneratedRegex(@"(?<![\d-])\b\d{4}\b(?![\d-])")]
    private static partial Regex StandaloneFour();

    [GeneratedRegex(@"[^@\s]+@[^@\s]+\.[a-z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\$\s?(\d+(?:\.\d{1,2})?)")]
    private static partial Regex AmountPattern();
}
