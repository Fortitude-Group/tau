using Tau.Conformance;

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        "usage: dotnet run --project tools/Tau.Conformance -- --tau <url> [--peer <url> --peer-name <name> "
        + "--peer-revision <sha> [--peer-api-key-env <NAME>]] --out <dir> [--requests <dir>] [--lenient-target] [--self-test]");
    return 2;
}

if (options.SelfTest)
{
    return SelfTest.Run();
}

var fixtures = RequestFixture.LoadAll(options.RequestsDir);
Console.WriteLine($"loaded {fixtures.Count} request fixtures from '{options.RequestsDir}'");

string? peerApiKey = null;
if (options.PeerApiKeyEnv is { } keyEnv)
{
    // Process environment first, then the Windows User scope (where setx puts it). Never printed.
    peerApiKey = Environment.GetEnvironmentVariable(keyEnv)
        ?? (OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable(keyEnv, EnvironmentVariableTarget.User) : null);
    if (string.IsNullOrWhiteSpace(peerApiKey))
    {
        Console.Error.WriteLine($"error: environment variable {keyEnv} is not set, so the peer can't be authenticated");
        return 2;
    }
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

var outcomes = new List<FixtureOutcome>(fixtures.Count);

foreach (var fixture in fixtures)
{
    var tauProbe = await HttpProbe.Send(http, "tau", options.TauUrl, fixture.WireBody);
    var tauVerdict = Evaluate(fixture, tauProbe, strict: !options.LenientTarget);

    TargetProbe? peerProbe = null;
    TargetVerdict? peerVerdict = null;
    var disagreements = new List<Disagreement>();

    if (options.PeerUrl is not null)
    {
        peerProbe = await HttpProbe.Send(http, options.PeerName ?? "peer", options.PeerUrl, fixture.WireBody, peerApiKey);
        peerVerdict = Evaluate(fixture, peerProbe, strict: false);

        if (fixture.ExpectedStatus == 200
            && tauVerdict.ContractOk && peerVerdict.ContractOk
            && tauProbe.RawBody is not null && peerProbe.RawBody is not null
            && fixture.ParsedRequest is not null)
        {
            disagreements = Disagree.Compare(fixture.ParsedRequest, tauProbe.RawBody, peerProbe.RawBody).ToList();
        }
    }

    outcomes.Add(new FixtureOutcome(fixture, tauProbe, tauVerdict, peerProbe, peerVerdict, disagreements));

    var tauMark = tauVerdict.ContractOk ? "PASS" : "FAIL";
    Console.WriteLine($"  [{tauMark}] {fixture.FileName} — tau {tauProbe.Status?.ToString() ?? "no response"}");
}

var header = new ReportHeader(
    ReportName: "Tau /v1/systemone conformance report",
    Command: $"dotnet run --project tools/Tau.Conformance -- {string.Join(' ', options.RawArgs)}",
    DateUtc: DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
    GitCommit: GitInfo.CurrentCommit(),
    Hardware: HardwareInfo.Capture(),
    ContractVersion: "systemone/2026-09-27",
    TauModelsEndpoint: await ModelsProbe.TryFetch(http, options.TauUrl),
    PeerName: options.PeerName,
    PeerRevision: options.PeerRevision);

var summary = ReportSummary.From(outcomes);
var (jsonPath, markdownPath) = ReportWriter.Write(options.OutDir, header, summary, outcomes);

Console.WriteLine();
Console.WriteLine($"wrote {jsonPath}");
Console.WriteLine($"wrote {markdownPath}");
Console.WriteLine(
    $"requests={summary.TotalRequests} tau-pass={summary.TauPass} tau-fail={summary.TauFail} "
    + $"peer-pass={summary.PeerPass?.ToString() ?? "n/a"} peer-fail={summary.PeerFail?.ToString() ?? "n/a"} "
    + $"structural-failures={summary.StructuralFailures} model-disagreements={summary.ModelDisagreements} "
    + $"peer-extensions={summary.PeerExtensions}");

return summary.TauFail > 0 ? 1 : 0;

static TargetVerdict Evaluate(RequestFixture fixture, TargetProbe probe, bool strict)
{
    if (probe.TransportError is not null)
    {
        return new TargetVerdict(false, [$"request failed: {probe.TransportError}"], []);
    }

    if (probe.Status != fixture.ExpectedStatus)
    {
        return new TargetVerdict(false, [$"status mismatch: expected {fixture.ExpectedStatus}, got {probe.Status}"], []);
    }

    if (fixture.ExpectedStatus != 200)
    {
        // The contract gives no response-body shape for an error; matching the expected status is
        // the whole check for a fixture that expects one (FR-024).
        return new TargetVerdict(true, [], []);
    }

    var outcome = ResponseValidator.ValidateSuccessBody(probe.RawBody!, fixture.ParsedRequest!, strict);
    return new TargetVerdict(outcome.ContractOk, outcome.Errors, outcome.Extensions);
}
