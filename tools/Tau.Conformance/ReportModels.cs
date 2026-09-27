namespace Tau.Conformance;

/// <summary>Whether one side's response for one fixture honoured the pinned contract.</summary>
internal sealed record TargetVerdict(bool ContractOk, IReadOnlyList<string> Errors, IReadOnlyList<string> Extensions);

/// <summary>Everything recorded for one fixture against one or both targets.</summary>
internal sealed record FixtureOutcome(
    RequestFixture Fixture,
    TargetProbe Tau,
    TargetVerdict TauVerdict,
    TargetProbe? Peer,
    TargetVerdict? PeerVerdict,
    IReadOnlyList<Disagreement> Disagreements);

/// <summary>The standard report header every parity/conformance/benchmark report carries (constitution Principle XIII).</summary>
internal sealed record ReportHeader(
    string ReportName,
    string Command,
    string DateUtc,
    string GitCommit,
    HardwareFingerprint Hardware,
    string ContractVersion,
    string TauModelsEndpoint,
    string? PeerName,
    string? PeerRevision);

/// <summary>The headline counts explained in prose alongside the markdown report (constitution Principle XII).</summary>
internal sealed record ReportSummary(
    int TotalRequests,
    int TauPass,
    int TauFail,
    int? PeerPass,
    int? PeerFail,
    int StructuralFailures,
    int ModelDisagreements,
    int PeerExtensions)
{
    public static ReportSummary From(IReadOnlyList<FixtureOutcome> outcomes)
    {
        var tauPass = outcomes.Count(o => o.TauVerdict.ContractOk);
        var hasPeer = outcomes.Count > 0 && outcomes[0].Peer is not null;

        int? peerPass = hasPeer ? outcomes.Count(o => o.PeerVerdict is { ContractOk: true }) : null;
        int? peerFail = hasPeer ? outcomes.Count - peerPass!.Value : null;

        var structuralFailures = outcomes.Count(o => !o.TauVerdict.ContractOk)
            + (hasPeer ? outcomes.Count(o => o.PeerVerdict is { ContractOk: false }) : 0);

        return new ReportSummary(
            TotalRequests: outcomes.Count,
            TauPass: tauPass,
            TauFail: outcomes.Count - tauPass,
            PeerPass: peerPass,
            PeerFail: peerFail,
            StructuralFailures: structuralFailures,
            ModelDisagreements: outcomes.Sum(o => o.Disagreements.Count),
            PeerExtensions: outcomes.Sum(o => o.PeerVerdict?.Extensions.Count ?? 0));
    }
}
