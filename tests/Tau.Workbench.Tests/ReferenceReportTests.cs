using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Cascade;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Report;
using Tau.Workbench.Spec;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Tests;

/// <summary>The report under a frontier reference: its wording, the cascade figures and the secondary gold view.</summary>
public sealed class ReferenceReportTests
{
    /// <summary>
    /// A synthetic example scored against the frontier: the frontier always answers one level above the
    /// dataset's label (so it never agrees with it), and the local model and the baseline track the frontier.
    /// </summary>
    private static TestRepo FrontierRun()
    {
        var repo = ReferenceTests.FrontierRepo(calibration: 400, heldOut: 300, classes: 4, altSubset: 20, synthetic: true,
            models: "[model-a]", baselines: "[classic]", targetError: 0.2);
        var spec = repo.Spec;
        var q = spec.Question;
        var held = ReferenceTests.Split(repo, "heldout");
        var cal = ReferenceTests.Split(repo, "calibration");
        var byId = held.Concat(cal).ToDictionary(i => i.Id);
        int FrontierIndex(string label) => (q.ClassIndex(label)!.Value + 1) % q.Classes.Count;

        FrontierStage.Run(spec, repo.Manifest, held, cal);
        ReferenceTests.Answer(repo, ReferenceTests.Batches(repo, "v1").Concat(ReferenceTests.Batches(repo, "v1-alt")), l => q.Classes[FrontierIndex(byId[l.ItemId].Label)]);
        FrontierStage.Run(spec, repo.Manifest, held, cal);

        var baseline = new StringBuilder();
        foreach (var (split, items) in new[] { ("calibration", cal), ("heldout", held) })
        {
            var rng = new Random(split.Length);
            var records = items.Select(i => new MeasuredItem { ItemId = i.Id, Gold = i.Label, LatencyMs = 10 }
                .WithVector(q, Synthetic.Draw(rng, q.Classes.Count, FrontierIndex(i.Label), 2.5))).ToArray();
            Synthetic.WriteRun(spec, "model-a", split, Phases.Raw, records, StubSystemOne.Hash); // its summary scores against gold
            foreach (var i in items)
            {
                var p = Synthetic.Draw(rng, q.Classes.Count, FrontierIndex(i.Label), 3);
                var probs = new JsonObject();
                for (int k = 0; k < p.Length; k++)
                {
                    probs[q.Classes[k]] = p[k];
                }

                baseline.Append(new JsonObject { ["id"] = i.Id, ["split"] = split, ["label"] = i.Label, ["probabilities"] = probs }.ToJsonString()).Append('\n');
            }
        }

        Directory.CreateDirectory(Path.Combine(repo.SpecDir, "baselines"));
        File.WriteAllText(spec.BaselinePath("classic"), baseline.ToString());
        CalibrateStage.Run(spec, "model-a", repo.Manifest, ReportTests.FixedContext.Clock);
        ThresholdStage.Run(spec);
        CascadeStage.Run(spec, repo.Manifest, held);
        return repo;
    }

    private static string H(string text) => WebUtility.HtmlEncode(text);

    [Fact]
    public void UnderAFrontierReferenceTheReportSaysAgreementAndKeepsAGoldView()
    {
        using var repo = FrontierRun();
        var doc = ReportBuilder.Run(repo.Spec, repo.Manifest, ReportTests.FixedContext);
        var html = File.ReadAllText(repo.Spec.ReportHtmlPath);
        var json = JsonNode.Parse(File.ReadAllText(repo.Spec.ReportJsonPath))!;

        // The reference is stated at the top, in the metadata and in the JSON twin.
        Assert.Equal(ReferenceKind.Frontier, doc.Reference);
        Assert.Contains("Reference: frontier-x's answers, not the dataset's labels.", doc.Summary, StringComparison.Ordinal);
        Assert.Contains(H(doc.Summary), html, StringComparison.Ordinal);
        Assert.Equal("frontier", (string)json["reference"]!);
        Assert.Equal("frontier", (string)json["metadata"]!["reference"]!);
        Assert.Contains("<dt>Reference labels</dt><dd>frontier: Scored against the frontier model", html, StringComparison.Ordinal);

        // Every rate is agreement with the frontier model, re-scored from the records (the summary files scored against gold).
        var raw = doc.Models[0].RawHeldOut!;
        Assert.Equal(ReferenceKind.Frontier, raw.Reference);
        Assert.InRange(raw.Metrics!.Accuracy, 0.5, 1);
        Assert.Contains("agrees with frontier-x on", doc.Summary, StringComparison.Ordinal);
        Assert.Contains("agreement with the frontier model", html, StringComparison.Ordinal);
        Assert.Contains("<th>Agreement raw</th>", html, StringComparison.Ordinal);
        Assert.Contains("Agreement with the frontier model on kept items", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Accuracy", html, StringComparison.Ordinal);
        Assert.DoesNotContain("% accurate", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rows: gold label", html, StringComparison.Ordinal);
        Assert.Contains("Rows are the frontier model's answer and columns the model's answer", html, StringComparison.Ordinal);
        var confusion = doc.Models[0].Confusion!;
        Assert.Equal(doc.Models[0].BestCalibrated!.Accuracy, confusion.Matrix!.Select((row, i) => row[i]).Sum() / (double)confusion.N, 10); // rows are frontier answers

        // The cascade: frontier-only is 100% by construction and said to be; blended is agreement of the served answer.
        var c = doc.Cascades.Single();
        Assert.Equal(ReferenceKind.Frontier, c.Reference);
        Assert.Equal(1.0, c.FrontierOnlyAccuracy.Rate);
        Assert.Contains("100% by construction", html, StringComparison.Ordinal);
        Assert.Contains(H(CascadeStage.FrontierOnlyByConstruction), html, StringComparison.Ordinal);
        Assert.NotNull(c.Cost); // the cost table is unchanged
        Assert.Contains("Estimates, not measured bills", html, StringComparison.Ordinal);

        // The secondary gold view, with the label failure reported as a finding.
        var view = doc.DatasetLabels!;
        Assert.StartsWith("The dataset's labels are synthetic and close to arbitrary; the frontier model agrees with them on 0.0%.", view.Caption, StringComparison.Ordinal);
        Assert.Equal(0.25, view.MajorityClassShare!.Value, 10);
        Assert.Equal(CountedRate.Of(300, 0), view.FrontierAgreement);
        Assert.Contains("<section id=\"dataset-labels\">", html, StringComparison.Ordinal);
        Assert.Contains(H(view.Caption), html, StringComparison.Ordinal);
        Assert.Equal(["model-a", "classic", "frontier-x"], view.Rows.Select(r => r.Model));
        var modelRow = view.Rows[0];
        Assert.Equal(300, modelRow.Raw!.N);
        Assert.True(modelRow.Raw.Rate < raw.Metrics.Accuracy); // it tracks the frontier, not the dataset's labels
        Assert.NotNull(modelRow.Calibrated);
        Assert.NotNull(view.Rows[1].Raw);
        Assert.Contains(doc.Misses, m => m.Contains("scored against the frontier model's answers instead", StringComparison.Ordinal));
        Assert.DoesNotContain(doc.Misses, m => m.Contains("' again", StringComparison.Ordinal)); // nothing is stale
        Assert.Equal(400, doc.Frontier!.Splits["calibration"].Cached["v1"]);
        Assert.Contains("v1 answers cached of 400 eligible calibration items; 0 pending", html, StringComparison.Ordinal);
    }

    [Fact]
    public void UnderGoldTheReportStatesItsReferenceAndHasNoGoldView()
    {
        using var repo = ReportTests.FullRun();
        var doc = ReportBuilder.Run(repo.Spec, repo.Manifest, ReportTests.FixedContext);
        var html = File.ReadAllText(repo.Spec.ReportHtmlPath);
        Assert.Equal(ReferenceKind.Gold, doc.Reference);
        Assert.Null(doc.DatasetLabels);
        Assert.Contains("Reference: the dataset's gold labels", doc.Summary, StringComparison.Ordinal);
        Assert.Contains("<th>Accuracy raw</th>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dataset-labels", html, StringComparison.Ordinal);
        Assert.DoesNotContain("by construction", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFrontierLabelFailsTheReportInsteadOfDroppingTheItem()
    {
        using var repo = FrontierRun();
        File.WriteAllLines(repo.Spec.CachePath, File.ReadAllLines(repo.Spec.CachePath).Where(l => !l.Contains("\"item_id\":\"c7\"", StringComparison.Ordinal)));
        Assert.Contains("c7", Assert.Throws<StageBlockedException>(() => ReportBuilder.Build(repo.Spec, repo.Manifest, ReportTests.FixedContext)).Message, StringComparison.Ordinal);
        Assert.Throws<StageBlockedException>(() => ThresholdStage.Run(repo.Spec));
        Assert.Throws<StageBlockedException>(() => CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest));
    }

    [Fact]
    public void ArtefactsFromTheOtherReferenceAreFlaggedForARerun()
    {
        using var repo = FrontierRun();
        repo.WriteSpec(File.ReadAllText(repo.SpecPath).Replace("reference: frontier", "reference: gold", StringComparison.Ordinal));
        var doc = ReportBuilder.Build(repo.Spec, repo.Manifest, ReportTests.FixedContext);
        Assert.Contains(doc.Misses, m => m.Contains("model-a's calibrators were fitted against the frontier model's answers", StringComparison.Ordinal));
        Assert.Contains(doc.Misses, m => m.Contains("run 'tau threshold' and 'tau cascade' again", StringComparison.Ordinal));
    }
}
