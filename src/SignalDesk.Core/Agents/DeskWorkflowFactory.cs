using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Tools;

namespace SignalDesk.Core.Agents;

public static class DeskWorkflowFactory
{
    /// <summary>
    /// Builds the handoff (swarm) workflow for one conversation. The Concierge starts every conversation and can hand
    /// off to any specialist; specialists can hand off to each other or back to the Concierge. With return-to-previous
    /// enabled, the customer's next message goes straight to whichever agent spoke last.
    /// </summary>
    public static Workflow Build(DeskContext context, Func<AgentSpec, IChatClient> clientFor)
    {
        Dictionary<string, AIAgent> agents = AgentRoster.All.ToDictionary(
            spec => spec.Key,
            spec => (AIAgent)new ChatClientAgent(clientFor(spec), new ChatClientAgentOptions
            {
                Id = spec.Key,
                Name = spec.Key,
                Description = spec.Scope,
                ChatOptions = new ChatOptions
                {
                    Instructions = spec.Instructions,
                    Tools = spec.CreateTools(context),
                },
            }));

        HandoffWorkflowBuilder builder = AgentWorkflowBuilder.CreateHandoffBuilderWith(agents[AgentRoster.Concierge]);
        foreach (AgentSpec from in AgentRoster.All)
        {
            foreach (AgentSpec to in AgentRoster.All.Where(to => to.Key != from.Key))
            {
                builder = builder.WithHandoff(agents[from.Key], agents[to.Key], AgentRoster.HandoffReason(to));
            }
        }

        return builder.EnableReturnToPrevious().Build();
    }
}
