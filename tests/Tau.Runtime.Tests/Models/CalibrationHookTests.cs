using System.Text.Json.Nodes;
using Tau.Calibration;
using Tau.Inference.Models;

namespace Tau.Runtime.Tests.Models;

/// <summary>
/// US6 / FR-019 / FR-020: calibrators from the configured directory change the answers exactly as Tau.Calibration
/// computes; <c>x-tau-raw</c> bypasses them; a bad calibrator stops the Runtime at start-up, naming the file.
/// </summary>
[Trait("Category", "Models")]
public sealed class CalibrationHookTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tau-cal-").FullName;
    private const string Ticket = "I was charged twice for my order and I want a refund today.";

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string LayaEnHash()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Tau.slnx")))
                return ModelPackage.Load(Path.Combine(d.FullName, "models", "laya-en")).OnnxSha256;
        throw new InvalidOperationException("repo root not found");
    }

    private static readonly CalibratorFitted Fitted = new(100, "hand-made test calibrator", null, "2026-09-27", null, null, "tests", "0");

    [Fact]
    public async Task Temperature_calibrator_changes_probabilities_and_raw_bypasses_it()
    {
        // T = 3 on laya-en noul (bucket 2): strongly softens the distribution.
        CalibratorFile.CreateTemperature("laya-en", LayaEnHash(), QuestionType.Noul, OptionBucket.Two, 3.0, Fitted)
            .Write(Path.Combine(_dir, "laya-en-noul.calibrator.json"));
        await using var factory = new RealEngineFactory { Settings = new() { ["Tau:CalibratorsDirectory"] = _dir, ["Tau:Models:0"] = "laya-en" } };
        var client = factory.CreateClient();
        var q = new JsonObject { ["refund"] = new JsonObject { ["type"] = "noul", ["instructions"] = "Is the customer asking for money back?" } };
        var body = Http.Request("laya-en", Ticket, q);

        var (_, calBody, calRes) = await Http.PostAsync(client, body);
        var (_, rawBody, rawRes) = await Http.PostAsync(client, body, r => r.Headers.Add("x-tau-raw", "true"));
        Http.AssertContractValid(calBody);
        Http.AssertContractValid(rawBody);
        Assert.Equal("laya-en:noul:2", calRes.Headers.GetValues("x-tau-calibrators").Single());
        Assert.Equal("none", rawRes.Headers.GetValues("x-tau-calibrators").Single());

        var cal = JsonNode.Parse(calBody)!["answers"]!["refund"]!["noul"]!.GetValue<double>();
        var raw = JsonNode.Parse(rawBody)!["answers"]!["refund"]!["noul"]!.GetValue<double>();
        // Raw is the reference's clamped temperature (≈1.98). Tau's T=3 must pull P(true) towards 0.5.
        Assert.True(Math.Abs(cal - 0.5) < Math.Abs(raw - 0.5), $"calibrated {cal} should be closer to 0.5 than raw {raw}");
    }

    [Fact]
    public async Task Isotonic_calibrator_is_applied_and_renormalised()
    {
        CalibratorFile.CreateIsotonic("laya-en", LayaEnHash(), QuestionType.Choice, null,
            new IsotonicKnots([0.0, 0.5, 1.0], [0.1, 0.5, 0.9]), Fitted)
            .Write(Path.Combine(_dir, "laya-en-choice.calibrator.json"));
        await using var factory = new RealEngineFactory { Settings = new() { ["Tau:CalibratorsDirectory"] = _dir, ["Tau:Models:0"] = "laya-en" } };
        var (status, body, res) = await Http.PostAsync(factory.CreateClient(), Http.Request("laya-en", Ticket, Http.Three));
        Assert.Equal(200, status);
        Http.AssertContractValid(body);
        Assert.Equal("laya-en:choice", res.Headers.GetValues("x-tau-calibrators").Single());
        var probs = JsonNode.Parse(body)!["answers"]!["queue"]!["probabilities"]!.AsObject().Select(kv => kv.Value!.GetValue<double>()).ToArray();
        Assert.InRange(probs.Sum(), 1 - 1e-3, 1 + 1e-3);
        Assert.True(probs.Max() < 0.9 + 1e-3, "the map caps any probability at 0.9 before renormalising");
    }

    [Fact]
    public void Bad_calibrator_file_stops_start_up_and_names_the_file()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.calibrator.json"), """{"format":"tau.calibrator","version":2}""");
        using var factory = new RealEngineFactory { Settings = new() { ["Tau:CalibratorsDirectory"] = _dir, ["Tau:Models:0"] = "laya-en" } };
        var e = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("broken.calibrator.json", e.ToString());
    }

    [Fact]
    public void Calibrator_for_a_different_model_hash_stops_start_up()
    {
        CalibratorFile.CreateTemperature("laya-en", new string('0', 64), QuestionType.Noul, null, 2.0, Fitted)
            .Write(Path.Combine(_dir, "stale.calibrator.json"));
        using var factory = new RealEngineFactory { Settings = new() { ["Tau:CalibratorsDirectory"] = _dir, ["Tau:Models:0"] = "laya-en" } };
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
