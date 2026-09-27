using System.Reflection;
using Tau.Workbench.Measure;
using Tau.Workbench.Report;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Cli;

/// <summary>
/// The <c>tau</c> command line: a thin shell over <see cref="Pipeline"/> (constitution I). It parses
/// arguments, loads the spec, runs one stage (or all of them) and maps failures to exit codes:
/// 0 success, 1 error, 2 blocked.
/// </summary>
internal static class TauCli
{
    internal const string Usage = """
tau: measure, calibrate and cost any /v1/systemone endpoint against a labelled decision.

Usage:
  tau <stage> <decision.yaml> [--endpoint URL] [--phase raw|calibrated] [--force]

Stages:
  label      Ingest cached frontier answers and export pending batches (exit 2 while any are pending).
  measure    Measure every model on the calibration and held-out splits (--phase raw by default;
             --phase calibrated once the Runtime has loaded the calibrators).
  calibrate  Fit temperature and isotonic calibrators on the calibration split and write them.
  threshold  Choose the confidence threshold for the spec's target error.
  cascade    Simulate local-first with frontier escalation, and estimate the cost.
  report     Write report.json and a self-contained report.html next to the spec.
  run        All of the above in order, skipping measure and calibrate when their outputs are current.

Options:
  --endpoint URL   Measure this endpoint instead of the one in the spec.
  --phase PHASE    For 'measure': raw (default) or calibrated.
  --force          For 'run': re-measure the raw phase and re-fit even when their outputs look current
                   (for example after the endpoint started honouring x-tau-precision: full).
  -h, --help       Show this help.
  --version        Show the version.

Exit codes: 0 success, 1 error, 2 blocked (for example frontier answers pending).
The Workbench never calls a paid API: frontier answers arrive as files in frontier/cache.jsonl.
""";

    private static readonly string[] Stages = ["label", "measure", "calibrate", "threshold", "cascade", "report", "run"];

    /// <summary>Runs the command line.</summary>
    /// <param name="args">Arguments.</param>
    /// <param name="stdout">Standard output.</param>
    /// <param name="stderr">Standard error.</param>
    /// <param name="configure">Lets tests adjust the pipeline options (stub endpoint, clock, probes).</param>
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, Func<PipelineOptions, PipelineOptions>? configure = null)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            stdout.Write(Usage);
            return args.Length == 0 ? ExitCodes.Error : ExitCodes.Ok;
        }

        if (args[0] == "--version")
        {
            stdout.WriteLine(typeof(TauCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "unknown");
            return ExitCodes.Ok;
        }

        if (!Parse(args, out var stage, out var specPath, out var endpoint, out var phase, out var force, out var problem))
        {
            stderr.WriteLine($"tau: {problem}");
            stderr.WriteLine("Run 'tau --help' for usage.");
            return ExitCodes.Error;
        }

        try
        {
            var spec = DecisionSpec.Load(specPath!);
            var options = new PipelineOptions
            {
                Endpoint = endpoint,
                Force = force,
                Out = stdout,
                Report = new ReportContext { Command = CommandLine(spec, stage!, args) },
            };
            options = configure?.Invoke(options) ?? options;
            return stage switch
            {
                "label" => Pipeline.Label(spec, options),
                "measure" => await Pipeline.MeasureAsync(spec, options, phase).ConfigureAwait(false),
                "calibrate" => Pipeline.Calibrate(spec, options),
                "threshold" => Pipeline.Threshold(spec, options),
                "cascade" => Pipeline.Cascade(spec, options),
                "report" => Pipeline.Report(spec, options),
                _ => await Pipeline.RunAsync(spec, options).ConfigureAwait(false),
            };
        }
        catch (StageBlockedException e)
        {
            stderr.WriteLine($"tau {stage}: blocked: {e.Message}");
            return ExitCodes.Blocked;
        }
        catch (WorkbenchException e)
        {
            stderr.WriteLine($"tau {stage}: {e.Message}");
            return ExitCodes.Error;
        }
    }

    /// <summary>Parses the arguments; false (with a problem) for anything malformed.</summary>
    internal static bool Parse(string[] args, out string? stage, out string? specPath, out Uri? endpoint, out string phase, out bool force, out string? problem)
    {
        stage = args.Length > 0 ? args[0] : null;
        specPath = null;
        endpoint = null;
        phase = Phases.Raw;
        force = false;
        problem = null;
        if (stage is null || !Stages.Contains(stage))
        {
            problem = $"unknown stage '{stage}'. Stages: {string.Join(", ", Stages)}.";
            return false;
        }

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--endpoint" when i + 1 < args.Length:
                    if (!Uri.TryCreate(args[++i], UriKind.Absolute, out endpoint) || endpoint.Scheme is not ("http" or "https"))
                    {
                        problem = $"--endpoint '{args[i]}' is not an absolute http(s) URL.";
                        return false;
                    }

                    break;
                case "--phase" when i + 1 < args.Length:
                    phase = args[++i];
                    if (phase is not (Phases.Raw or Phases.Calibrated))
                    {
                        problem = $"--phase must be '{Phases.Raw}' or '{Phases.Calibrated}', not '{phase}'.";
                        return false;
                    }

                    if (stage != "measure")
                    {
                        problem = "--phase only applies to 'measure'.";
                        return false;
                    }

                    break;
                case "--force":
                    if (stage != "run")
                    {
                        problem = "--force only applies to 'run' (the other stages never skip work).";
                        return false;
                    }

                    force = true;
                    break;
                case var a when a.StartsWith('-'):
                    problem = $"unknown or incomplete option '{a}'.";
                    return false;
                default:
                    if (specPath is not null)
                    {
                        problem = $"unexpected argument '{args[i]}': give one decision.yaml.";
                        return false;
                    }

                    specPath = args[i];
                    break;
            }
        }

        if (specPath is null)
        {
            problem = "missing the decision.yaml path.";
            return false;
        }

        return true;
    }

    /// <summary>The command as recorded in the report: the spec path relative to the repository root.</summary>
    private static string CommandLine(DecisionSpec spec, string stage, string[] args)
    {
        var rel = Path.GetRelativePath(spec.RepoRoot, spec.SpecPath).Replace('\\', '/');
        var rest = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--endpoint" or "--phase")
            {
                rest.Add(args[i]);
                rest.Add(args[++i]);
            }
            else if (args[i] == "--force")
            {
                rest.Add(args[i]);
            }
        }

        return string.Join(' ', new[] { "tau", stage, rel }.Concat(rest));
    }
}
