using Tau.Workbench.Data;

namespace Tau.Workbench.Tests;

public sealed class DatasetTests
{
    [Fact]
    public void SplitsLoadAfterTheirHashesCheckOut()
    {
        using var repo = TestRepo.Create(calibration: 12, heldOut: 8);
        var held = PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout");
        Assert.Equal(8, held.Count);
        Assert.Equal(new DatasetItem("h0", "held-out item 0", "a"), held[0]);
        Assert.Equal(12, PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "calibration").Count);
    }

    [Fact]
    public void AChangedSplitFailsTheShaCheck()
    {
        using var repo = TestRepo.Create();
        File.AppendAllText(Path.Combine(repo.DataDir, "heldout.jsonl"), "{\"id\":\"extra\",\"text\":\"x\",\"label\":\"a\"}\n");
        var e = Assert.Throws<WorkbenchException>(() => PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout"));
        Assert.Contains("sha256", e.Message, StringComparison.Ordinal);
        Assert.Contains("re-run the sidecar data script", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingSplitSaysTheDataIsNotPrepared()
    {
        using var repo = TestRepo.Create();
        File.Delete(Path.Combine(repo.DataDir, "calibration.jsonl"));
        var e = Assert.Throws<WorkbenchException>(() => PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "calibration"));
        Assert.Contains("run the sidecar data script", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingManifestSaysTheDataIsNotPrepared()
    {
        using var repo = TestRepo.Create();
        File.Delete(Path.Combine(repo.SpecDir, "dataset.manifest.json"));
        var e = Assert.Throws<WorkbenchException>(() => DatasetManifest.Load(repo.Spec.ManifestPath));
        Assert.Contains("run the sidecar data script", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AManifestWithoutTheSplitHashIsRejected()
    {
        using var repo = TestRepo.Create();
        File.WriteAllText(repo.Spec.ManifestPath, "{\"splits\":{}}");
        var e = Assert.Throws<WorkbenchException>(() => PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout"));
        Assert.Contains("records no sha256", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlatHashMapIsAlsoAccepted()
    {
        using var repo = TestRepo.Create();
        var sha = WorkbenchJson.Sha256File(repo.Spec.SplitPath("heldout"));
        File.WriteAllText(repo.Spec.ManifestPath, $"{{\"sha256\":{{\"heldout\":\"{sha.ToUpperInvariant()}\"}},\"license\":\"MIT\",\"synthetic\":true}}");
        var manifest = repo.Manifest;
        Assert.True(manifest.Synthetic);
        Assert.Equal("MIT", manifest.Licence);
        Assert.Equal(300, PreparedDataset.LoadSplit(repo.Spec, manifest, "heldout").Count);
        Assert.Equal(sha[..12], manifest.CacheRevision);
    }

    [Fact]
    public void CacheRevisionPrefersThePinnedSourceRevision()
    {
        using var repo = TestRepo.Create();
        Assert.Equal("0123456789ab", repo.Manifest.CacheRevision);
        Assert.Equal(64, repo.Manifest.Sha256.Length);
    }

    [Theory]
    [InlineData("{\"id\":\"x1\",\"text\":\"t\",\"label\":\"zz\"}", "gold label 'zz'")]
    [InlineData("{\"id\":\"x1\",\"label\":\"a\"}", "no 'text'")]
    [InlineData("{\"text\":\"t\",\"label\":\"a\"}", "no 'id'")]
    [InlineData("not json", "does not parse")]
    [InlineData("[1,2]", "not a JSON object")]
    public void MalformedItemsAreRejectedWithTheLine(string line, string expected)
    {
        using var repo = TestRepo.Create();
        repo.WriteSplit("heldout", [new DatasetItem("ok", "fine", "a")]);
        File.AppendAllText(Path.Combine(repo.DataDir, "heldout.jsonl"), line + "\n");
        repo.WriteManifest();
        var e = Assert.Throws<WorkbenchException>(() => PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout"));
        Assert.Contains(expected, e.Message, StringComparison.Ordinal);
        Assert.Contains(":2", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedIdsAreRejected()
    {
        using var repo = TestRepo.Create();
        repo.WriteSplit("heldout", [new DatasetItem("x", "one", "a"), new DatasetItem("x", "two", "b")]);
        repo.WriteManifest();
        Assert.Contains("repeats item id", Assert.Throws<WorkbenchException>(() => PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericAndBooleanLabelsAreReadAsText()
    {
        using var repo = TestRepo.Create(type: "score", classes: 3);
        File.WriteAllText(Path.Combine(repo.DataDir, "heldout.jsonl"), "{\"id\":\"n\",\"text\":\"t\",\"label\":2}\n");
        repo.WriteManifest();
        Assert.Equal("2", PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout")[0].Label);

        using var noul = TestRepo.Create(type: "noul");
        File.WriteAllText(Path.Combine(noul.DataDir, "heldout.jsonl"), "{\"id\":\"n\",\"text\":\"t\",\"label\":true}\n");
        noul.WriteManifest();
        Assert.Equal("true", PreparedDataset.LoadSplit(noul.Spec, noul.Manifest, "heldout")[0].Label);
    }
}
