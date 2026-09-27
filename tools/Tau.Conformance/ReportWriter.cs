using System.Text;
using System.Text.Json;

namespace Tau.Conformance;

/// <summary>Writes <c>conformance.json</c> and <c>conformance.md</c> from a finished run.</summary>
internal static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static (string JsonPath, string MarkdownPath) Write(
        string outDir, ReportHeader header, ReportSummary summary, IReadOnlyList<FixtureOutcome> outcomes)
    {
        Directory.CreateDirectory(outDir);

        var jsonPath = Path.Combine(outDir, "conformance.json");
        var markdownPath = Path.Combine(outDir, "conformance.md");

        File.WriteAllText(jsonPath, BuildJson(header, summary, outcomes));
        File.WriteAllText(markdownPath, BuildMarkdown(header, summary, outcomes));

        return (jsonPath, markdownPath);
    }

    private static string BuildJson(ReportHeader header, ReportSummary summary, IReadOnlyList<FixtureOutcome> outcomes)
    {
        var document = new
        {
            header,
            summary,
            requests = outcomes.Select(o => new
            {
                file = o.Fixture.FileName,
                name = o.Fixture.Name,
                description = o.Fixture.Description,
                expectedStatus = o.Fixture.ExpectedStatus,
                tau = ToDto(o.Tau, o.TauVerdict),
                peer = o.Peer is not null ? ToDto(o.Peer, o.PeerVerdict!) : null,
                disagreements = o.Disagreements.Select(d => new { d.Question, d.Kind, d.TauValue, d.PeerValue }),
            }),
        };

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private static object ToDto(TargetProbe probe, TargetVerdict verdict) => new
    {
        status = probe.Status,
        latencyMs = Math.Round(probe.LatencyMs, 1),
        transportError = probe.TransportError,
        contractOk = verdict.ContractOk,
        errors = verdict.Errors,
        extensions = verdict.Extensions,
    };

    private static string BuildMarkdown(ReportHeader header, ReportSummary summary, IReadOnlyList<FixtureOutcome> outcomes)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {header.ReportName}");
        sb.AppendLine();
        sb.AppendLine($"- **Command**: `{header.Command}`");
        sb.AppendLine($"- **Date (UTC)**: {header.DateUtc}");
        sb.AppendLine($"- **Git commit**: {header.GitCommit}");
        sb.AppendLine($"- **Contract version**: `{header.ContractVersion}`");
        sb.AppendLine($"- **GPU**: {header.Hardware.Gpu}");
        sb.AppendLine($"- **CPU**: {header.Hardware.Cpu}");
        sb.AppendLine($"- **RAM**: {header.Hardware.Ram}");
        sb.AppendLine($"- **OS**: {header.Hardware.Os}");
        sb.AppendLine($"- **Tau `/v1/models`**: {OneLine(header.TauModelsEndpoint)}");
        sb.AppendLine($"- **Peer**: {header.PeerName ?? "(none — Tau run alone)"}"
            + (header.PeerRevision is null ? "" : $" at `{header.PeerRevision}`"));
        sb.AppendLine();
        sb.AppendLine(
            "This report is reproducible by re-running the command above from a checkout of the "
            + "commit named above; every number below comes from that one run.");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Requests | Tau pass | Tau fail | Peer pass | Peer fail | Structural failures | Model disagreements | Peer extensions |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        sb.AppendLine(
            $"| {summary.TotalRequests} | {summary.TauPass} | {summary.TauFail} | "
            + $"{summary.PeerPass?.ToString() ?? "n/a"} | {summary.PeerFail?.ToString() ?? "n/a"} | "
            + $"{summary.StructuralFailures} | {summary.ModelDisagreements} | {summary.PeerExtensions} |");
        sb.AppendLine();
        sb.AppendLine(
            $"- **Requests** is the number of committed fixtures under `tests/conformance/requests/` "
            + $"this run sent to every target: {summary.TotalRequests}.");
        sb.AppendLine(
            $"- **Tau pass/fail** is how many of those {summary.TotalRequests} requests Tau answered without any "
            + $"contract violation ({summary.TauPass} did, {summary.TauFail} did not); any Tau fail makes this "
            + "run's exit code non-zero, since Tau's own conformance is the merge gate (FR-024).");
        sb.AppendLine(
            "- **Peer pass/fail** is the same count for the peer server, validated leniently (extra fields "
            + "never count against it); it is recorded for comparison and never affects the exit code.");
        sb.AppendLine(
            $"- **Structural failures** is the total number of contract violations across both sides "
            + $"({summary.StructuralFailures}): a status the contract didn't expect, a required field missing, "
            + "or a value outside what the question allows — see FR-024's structural/model-disagreement split.");
        sb.AppendLine(
            $"- **Model disagreements** ({summary.ModelDisagreements}) is how many times Tau and the peer both gave a "
            + "contractually valid answer to the same question that simply differs in value (a different choice, "
            + "score or noul) — expected because they are different models, and never a failure.");
        sb.AppendLine(
            $"- **Peer extensions** ({summary.PeerExtensions}) is how many response fields the peer returned beyond "
            + "the pinned contract, tolerated under lenient validation and listed per request below.");
        sb.AppendLine();

        sb.AppendLine("## Per-request detail");
        sb.AppendLine();

        foreach (var outcome in outcomes)
        {
            sb.AppendLine($"### {outcome.Fixture.Name} (`{outcome.Fixture.FileName}`)");
            sb.AppendLine();
            sb.AppendLine(outcome.Fixture.Description);
            sb.AppendLine();
            sb.AppendLine($"Expected status: **{outcome.Fixture.ExpectedStatus}**.");
            sb.AppendLine();

            AppendTarget(sb, "Tau", outcome.Tau, outcome.TauVerdict);
            if (outcome.Peer is not null && outcome.PeerVerdict is not null)
            {
                AppendTarget(sb, "Peer", outcome.Peer, outcome.PeerVerdict);
            }

            if (outcome.Disagreements.Count > 0)
            {
                sb.AppendLine("Model disagreements:");
                foreach (var d in outcome.Disagreements)
                {
                    sb.AppendLine($"- `{d.Question}` ({d.Kind}): Tau said `{d.TauValue}`, peer said `{d.PeerValue}`.");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static void AppendTarget(StringBuilder sb, string label, TargetProbe probe, TargetVerdict verdict)
    {
        var statusText = probe.Status?.ToString() ?? "no response";
        var okText = verdict.ContractOk ? "PASS" : "FAIL";
        sb.AppendLine($"- **{label}**: status {statusText}, {probe.LatencyMs:0.#} ms, contract **{okText}**.");

        if (probe.TransportError is not null)
        {
            sb.AppendLine($"  - transport error: {probe.TransportError}");
        }

        foreach (var error in verdict.Errors)
        {
            sb.AppendLine($"  - error: {error}");
        }

        foreach (var extension in verdict.Extensions)
        {
            sb.AppendLine($"  - peer extension: {extension}");
        }
    }

    private static string OneLine(string text) => text.Replace('\n', ' ').Replace('\r', ' ').Trim();
}
