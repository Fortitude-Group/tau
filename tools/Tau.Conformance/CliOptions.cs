namespace Tau.Conformance;

/// <summary>Parsed command-line options for the conformance runner.</summary>
internal sealed record CliOptions
{
    /// <summary>Base URL of the Tau (or, in <see cref="LenientTarget"/> mode, the stand-in) target under test.</summary>
    public required string TauUrl { get; init; }

    /// <summary>Base URL of the peer server to compare against, or <c>null</c> to run against Tau alone.</summary>
    public string? PeerUrl { get; init; }

    /// <summary>Human-readable peer name for the report header (for example <c>kev-0.8b</c>).</summary>
    public string? PeerName { get; init; }

    /// <summary>The peer's pinned commit/revision for the report header.</summary>
    public string? PeerRevision { get; init; }

    /// <summary>Directory the report files are written to.</summary>
    public required string OutDir { get; init; }

    /// <summary>Directory holding the <c>*.json</c> request fixtures.</summary>
    public string RequestsDir { get; init; } = Path.Combine("tests", "conformance", "requests");

    /// <summary>When set, run the canned self-test instead of hitting any server.</summary>
    public bool SelfTest { get; init; }

    /// <summary>
    /// When set, validate the primary (<c>--tau</c>) target leniently, the same way a peer is
    /// validated. Used to run the tool end to end against a peer standing in as the target, before
    /// Tau itself is runnable, without strict-mode peer-extension failures aborting the run.
    /// </summary>
    public bool LenientTarget { get; init; }

    /// <summary>The raw argv, kept only so the report can record the exact command that produced it.</summary>
    public required IReadOnlyList<string> RawArgs { get; init; }

    public static CliOptions Parse(string[] args)
    {
        string? tau = null;
        string? peer = null;
        string? peerName = null;
        string? peerRevision = null;
        string? outDir = null;
        string? requestsDir = null;
        var selfTest = false;
        var lenientTarget = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tau": tau = Next(args, ref i); break;
                case "--peer": peer = Next(args, ref i); break;
                case "--peer-name": peerName = Next(args, ref i); break;
                case "--peer-revision": peerRevision = Next(args, ref i); break;
                case "--out": outDir = Next(args, ref i); break;
                case "--requests": requestsDir = Next(args, ref i); break;
                case "--self-test": selfTest = true; break;
                case "--lenient-target": lenientTarget = true; break;
                default:
                    throw new ArgumentException($"unrecognised argument '{args[i]}'");
            }
        }

        if (selfTest)
        {
            return new CliOptions
            {
                TauUrl = tau ?? "unused",
                OutDir = outDir ?? "reports/r1",
                SelfTest = true,
                RawArgs = args,
            };
        }

        if (tau is null)
        {
            throw new ArgumentException("--tau <url> is required");
        }

        if (outDir is null)
        {
            throw new ArgumentException("--out <dir> is required");
        }

        return new CliOptions
        {
            TauUrl = tau,
            PeerUrl = peer,
            PeerName = peerName,
            PeerRevision = peerRevision,
            OutDir = outDir,
            RequestsDir = requestsDir ?? Path.Combine("tests", "conformance", "requests"),
            LenientTarget = lenientTarget,
            RawArgs = args,
        };
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"missing value for '{args[i]}'");
        }

        return args[++i];
    }
}
