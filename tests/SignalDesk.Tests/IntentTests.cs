using SignalDesk.Core.Agents;
using SignalDesk.Core.Demo;

namespace SignalDesk.Tests;

public class IntentTests
{
    [Theory]
    [InlineData("My home internet keeps dropping every evening.", AgentRoster.TechSupport)]
    [InlineData("I have no service on my phone", AgentRoster.TechSupport)]
    [InlineData("Why did my bill jump this month?", AgentRoster.Billing)]
    [InlineData("I was charged roaming fees on my bill", AgentRoster.Billing)]
    [InlineData("I'm traveling to Japan for 10 days", AgentRoster.Plans)]
    [InlineData("Can I upgrade my plan?", AgentRoster.Plans)]
    [InlineData("My son's phone was stolen", AgentRoster.Account)]
    [InlineData("Please update my email", AgentRoster.Account)]
    [InlineData("I want to cancel, I found a cheaper deal", AgentRoster.Retention)]
    [InlineData("Tuesday morning works.", null)]
    [InlineData("Yes, set that up.", null)]
    public void Classifies_customer_requests(string text, string? expected) =>
        Assert.Equal(expected, Intents.Classify(text));

    [Theory]
    [InlineData("Sure, it's 415-555-0142 and my PIN is 4821.", "415-555-0142", "4821")]
    [InlineData("206-555-0123 / 5550", "206-555-0123", "5550")]
    [InlineData("What's the balance on 415-555-0142?", "415-555-0142", null)]
    public void Extracts_credentials(string text, string? phone, string? pin)
    {
        Assert.Equal(phone, Intents.Phone(text));
        Assert.Equal(pin, Intents.Pin(text));
    }

    [Theory]
    [InlineData("Yes please, waive it.", true)]
    [InlineData("No thanks, that's fine.", false)]
    [InlineData("Sounds good", true)]
    public void Detects_consent(string text, bool affirmative) => Assert.Equal(affirmative, Intents.IsAffirmative(text));
}
