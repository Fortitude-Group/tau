using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tau.Contract;
using Tau.Inference.Engine;

namespace Tau.Runtime.Tests.Http;

/// <summary>A deterministic stand-in engine: answers every question with a fixed, contract-valid answer.</summary>
internal sealed class FakeEngine : IDecisionEngine
{
    public List<(DecisionRequest Request, DecisionOptions Options)> Calls { get; } = [];
    public Func<DecisionRequest, DecisionRejectedException?> Reject { get; set; } = _ => null;

    public Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct)
    {
        Calls.Add((request, options));
        if (Reject(request) is { } e) throw e;
        var answers = new OrderedDictionary<string, Answer>();
        foreach (var (key, q) in request.Questions)
        {
            answers[key] = q switch
            {
                ChoiceQuestion c => new ChoiceAnswer
                {
                    Choice = c.Criteria[0].Key,
                    Probabilities = new OrderedDictionary<string, double>(c.Criteria.Select((kv, i) =>
                        new KeyValuePair<string, double>(kv.Key, i == 0 ? 1.0 : 0.0))),
                    Confidence = 1.0,
                },
                ScoreQuestion s => new ScoreAnswer
                {
                    Score = 0,
                    Legend = new OrderedDictionary<string, string>(s.Criteria.Select((l, i) =>
                        new KeyValuePair<string, string>(i.ToString(), l.ToString()))),
                    Probabilities = new OrderedDictionary<string, double>(s.Criteria.Select((_, i) =>
                        new KeyValuePair<string, double>(i.ToString(), i == 0 ? 1.0 : 0.0))),
                    Confidence = 1.0,
                },
                _ => new NoulAnswer { Noul = 0.25 },
            };
        }
        var response = new DecisionResponse
        {
            Model = "laya-en", Answers = answers, Usage = new Usage { InputTokens = 42, OutputTokens = 0 },
        };
        return Task.FromResult(new DecisionResult(response, new DecisionDiagnostics(
            "laya-en", new string('a', 64), "English Latin text (Ω)", [], false, request.Questions.Count, 42, 1.5)));
    }

    public IReadOnlyList<InstalledModel> Models { get; } =
        [new("laya-en", "laya", new string('b', 40), new string('a', 64), true)];

    public IReadOnlyList<string> Aliases { get; } = ["auto", "tau-auto", "jev-latest", "jev-*"];

    public bool Ready => true;
}

/// <summary>Hosts the real Tau.Runtime pipeline with the fake engine swapped in.</summary>
public sealed class FakeEngineFactory : WebApplicationFactory<Program>
{
    internal FakeEngine Engine { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IDecisionEngine>(Engine)));
}
