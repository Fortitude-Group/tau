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
        // Raw is the reference's answer (its own clamped temperature, ≈1.98). Tau's T=3 on log p_ref must pull
        // P(true) further towards 0.5.
        Assert.True(Math.Abs(cal - 0.5) < Math.Abs(raw - 0.5), $"calibrated {cal} should be closer to 0.5 than raw {raw}");
    }

    [Fact]
    public async Task Calibrated_noul_equals_the_calibrator_applied_to_the_raw_response()
    {
        // Format v1 (research R-01): the Runtime applies a calibrator to the reference probabilities a raw answer
        // reports, so anyone holding the raw response (the Workbench) computes the same calibrated answer. Laya's
        // noul vector is [1 − p, p]; the only gap is the raw answer's 4-dp rounding.
        var file = CalibratorFile.CreateTemperature("laya-en", LayaEnHash(), QuestionType.Noul, OptionBucket.Two, 3.0, Fitted);
        file.Write(Path.Combine(_dir, "laya-en-noul.calibrator.json"));
        await using var factory = new RealEngineFactory { Settings = new() { ["Tau:CalibratorsDirectory"] = _dir, ["Tau:Models:0"] = "laya-en" } };
        var client = factory.CreateClient();
        var q = new JsonObject { ["refund"] = new JsonObject { ["type"] = "noul", ["instructions"] = "Is the customer asking for money back?" } };
        var body = Http.Request("laya-en", Ticket, q);

        var (_, calBody, _) = await Http.PostAsync(client, body);
        var (_, rawBody, _) = await Http.PostAsync(client, body, r => r.Headers.Add("x-tau-raw", "true"));
        var cal = JsonNode.Parse(calBody)!["answers"]!["refund"]!["noul"]!.GetValue<double>();
        var raw = JsonNode.Parse(rawBody)!["answers"]!["refund"]!["noul"]!.GetValue<double>();

        var expected = new Calibrator(file).ApplyToProbabilities([1 - raw, raw])[1];
        Assert.Equal(expected, cal, 1e-4);
    }

    [Fact]
    public async Task Calibrated_choice_equals_the_calibrator_applied_to_the_raw_response()
    {
        // The same equality for a whole choice distribution, option by option.
        var file = CalibratorFile.CreateTemperature("laya-en", LayaEnHash(), QuestionType.Choice, null, 2.0, Fitted);
        file.Write(Path.Combine(_dir, "laya-en-choice.calibrator.json"));
        await using var factory = new RealEngineFactory { Settings = new() { ["Tau:CalibratorsDirectory"] = _dir, ["Tau:Models:0"] = "laya-en" } };
        var client = factory.CreateClient();
        var body = Http.Request("laya-en", Ticket, Http.Three);

        var (_, calBody, _) = await Http.PostAsync(client, body);
        var (_, rawBody, _) = await Http.PostAsync(client, body, r => r.Headers.Add("x-tau-raw", "true"));
        double[] Probs(string json) => JsonNode.Parse(json)!["answers"]!["queue"]!["probabilities"]!.AsObject()
            .Select(kv => kv.Value!.GetValue<double>()).ToArray();

        var expected = new Calibrator(file).ApplyToProbabilities(Probs(rawBody));
        var actual = Probs(calBody);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 1e-4);
    }

    [Fact]
    public async Task Full_precision_calibrated_choice_equals_the_calibrator_applied_to_the_full_precision_raw_response()
    {
        // With x-tau-precision: full on both calls, the rounding gap closes: fitting and applying see one vector.
        var file = CalibratorFile.CreateTemperature("laya-en", LayaEnHash(), QuestionType.Choice, null, 2.0, Fitted);
        file.Write(Path.Combine(_dir, "laya-en-choice.calibrator.json"));
        await using var factory = new RealEngineFactory { Settings = new() { ["Tau:CalibratorsDirectory"] = _dir, ["Tau:Models:0"] = "laya-en" } };
        var client = factory.CreateClient();
        var body = Http.Request("laya-en", Ticket, Http.Three);

        var (_, calBody, calRes) = await Http.PostAsync(client, body, r => r.Headers.Add("x-tau-precision", "full"));
        var (_, rawBody, rawRes) = await Http.PostAsync(client, body, r =>
        {
            r.Headers.Add("x-tau-raw", "true");
            r.Headers.Add("x-tau-precision", "full");
        });
        var (_, roundedBody, roundedRes) = await Http.PostAsync(client, body);
        Http.AssertContractValid(calBody);
        Http.AssertContractValid(rawBody);
        Assert.Equal("full", calRes.Headers.GetValues("x-tau-precision").Single());
        Assert.Equal("full", rawRes.Headers.GetValues("x-tau-precision").Single());
        Assert.False(roundedRes.Headers.Contains("x-tau-precision"));
        double[] Probs(string json) => JsonNode.Parse(json)!["answers"]!["queue"]!["probabilities"]!.AsObject()
            .Select(kv => kv.Value!.GetValue<double>()).ToArray();

        var expected = new Calibrator(file).ApplyToProbabilities(Probs(rawBody));
        var actual = Probs(calBody);
        var rounded = Probs(roundedBody);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 1e-9);
            Assert.Equal(Math.Round(actual[i], 4, MidpointRounding.ToEven), rounded[i], 1e-12);
        }
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
