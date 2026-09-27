using Tau.Bench;
using Tau.Inference.Models;

// Tau.Bench: per-model latency on one provider, in-process (default) or over HTTP (--http-url), or --combine.
// Exit codes: 0 report written, 1 bad arguments, 2 refused (a number couldn't be labelled honestly) or failed.
BenchOptions options;
try
{
    if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
    {
        Console.WriteLine(BenchOptions.Usage);
        return args.Length == 0 ? 1 : 0;
    }
    options = BenchOptions.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    Console.Error.WriteLine(BenchOptions.Usage);
    return 1;
}

try
{
    if (options.CombineDir is { } dir)
    {
        var (md, json) = Combiner.Combine(dir);
        Console.WriteLine($"wrote {md}");
        Console.WriteLine($"wrote {json}");
        return 0;
    }

    var workload = Workload.Load();
    Console.WriteLine($"Tau.Bench: {(options.IsHttp ? "HTTP " + options.HttpUrl : "in-process")}, provider {options.Provider}, "
                      + $"questions {string.Join(",", options.Questions)}, warm-up {options.Warmup}, iterations {options.Iterations}, repeats {options.Repeat}");

    var report = options.IsHttp
        ? HttpRunner.Run(options, workload, HostInfo.SampleLoad)
        : InProcessRunner.Run(options, workload, HostInfo.SampleLoad);

    Directory.CreateDirectory(options.OutDir);
    var jsonPath = Path.Combine(options.OutDir, options.FileStem + ".json");
    var mdPath = Path.Combine(options.OutDir, options.FileStem + ".md");
    ReportJson.Write(jsonPath, report);
    File.WriteAllText(mdPath, MarkdownReport.Build(report));
    Console.WriteLine($"wrote {mdPath}");
    Console.WriteLine($"wrote {jsonPath}");
    return 0;
}
catch (Exception e) when (e is BenchRefusedException or ModelPackageException or HttpRequestException or IOException or InvalidDataException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    Console.Error.WriteLine("no report was written.");
    return 2;
}
