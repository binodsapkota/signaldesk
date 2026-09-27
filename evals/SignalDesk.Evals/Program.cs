using System.Globalization;
using System.Text;
using SignalDesk.Core.Runtime;
using SignalDesk.Evals;

double minRouting = Arg("--min-routing", 0.9);
double minSuccess = Arg("--min-success", 0.9);
bool live = args.Contains("--live");

var options = new DeskOptions
{
    DemoTokenDelayMs = 0,
    Mode = live ? "openai" : "demo",
    OpenAIApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
    OpenAIModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4.1-mini",
    OpenAIEndpoint = Environment.GetEnvironmentVariable("OPENAI_BASE_URL"),
};
if (live && !options.IsLive)
{
    Console.Error.WriteLine("--live requires OPENAI_API_KEY.");
    return 2;
}

GoldenSet golden = GoldenRunner.Load(Path.Combine(AppContext.BaseDirectory, "golden.json"));
if (Array.IndexOf(args, "--trace") is var t and >= 0 && t + 1 < args.Length)
{
    GoldenCase traced = golden.Cases.First(c => c.Id == args[t + 1]);
    CaseResult result = await GoldenRunner.RunCaseAsync(traced, options, log: Console.WriteLine);
    Console.WriteLine(result.Passed ? "PASS" : "FAIL: " + string.Join("; ", result.Problems));
    return result.Passed ? 0 : 1;
}

EvalSummary summary = await GoldenRunner.RunAsync(golden.Cases, options);

var md = new StringBuilder();
md.AppendLine($"## SignalDesk evals ({options.ModelLabel})");
md.AppendLine();
md.AppendLine("| Case | Category | Agent path | Routing | Tools | Guardrails | Task | Notes |");
md.AppendLine("|---|---|---|:-:|:-:|:-:|:-:|---|");
foreach (CaseResult r in summary.Results)
{
    md.AppendLine($"| {r.Case.Id} | {r.Case.Category} | {string.Join(" > ", r.Path)} | {Mark(r.RoutingOk)} | {r.ToolRecall:P0} | {Mark(r.GuardrailsOk)} | {Mark(r.TaskOk)} | {string.Join("; ", r.Problems)} |");
}

md.AppendLine();
md.AppendLine("| Metric | Score |");
md.AppendLine("|---|---|");
md.AppendLine($"| Routing accuracy (exact agent path) | {summary.RoutingAccuracy:P1} |");
md.AppendLine($"| Tool-call recall | {summary.ToolRecall:P1} |");
md.AppendLine($"| Guardrail adherence | {summary.GuardrailAdherence:P1} |");
md.AppendLine($"| Task success | {summary.TaskSuccess:P1} |");
md.AppendLine($"| Cases passed | {summary.Results.Count(r => r.Passed)}/{summary.Results.Count} |");

Console.WriteLine(md);
if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } stepSummary)
{
    File.AppendAllText(stepSummary, md.ToString());
}

bool ok = summary.RoutingAccuracy >= minRouting && summary.TaskSuccess >= minSuccess && summary.GuardrailAdherence >= 1.0;
Console.WriteLine(ok ? "PASS" : $"FAIL (thresholds: routing >= {minRouting:P0}, task >= {minSuccess:P0}, guardrails = 100%)");
return ok ? 0 : 1;

double Arg(string name, double fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
}

static string Mark(bool ok) => ok ? "pass" : "FAIL";
