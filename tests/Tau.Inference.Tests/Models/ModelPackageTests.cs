using System.Text.Json.Nodes;
using Tau.Inference.Models;

namespace Tau.Inference.Tests.Models;

public sealed class ModelPackageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tau-pkg-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    private void WriteLayaPackage(Func<JsonObject, JsonObject>? tweak = null)
    {
        var onnx = Write("model.onnx", "graph");
        var data = Write("model.onnx.data", "weights");
        var tok = Write("tokenizer.json", "{}");
        var cfg = Write("tokenizer_config.json", "{}");
        var m = new JsonObject
        {
            ["format"] = "tau.model", ["version"] = 1, ["id"] = "laya-en", ["family"] = "laya",
            ["source"] = new JsonObject { ["repo"] = "convaiinnovations/laya", ["revision"] = new string('a', 40) },
            ["onnx"] = new JsonObject
            {
                ["file"] = "model.onnx", ["sha256"] = ModelPackage.Sha256(onnx),
                ["data_file"] = "model.onnx.data", ["data_sha256"] = ModelPackage.Sha256(data),
            },
            ["tokenizer"] = new JsonObject
            {
                ["file"] = "tokenizer.json", ["sha256"] = ModelPackage.Sha256(tok),
                ["config"] = "tokenizer_config.json", ["config_sha256"] = ModelPackage.Sha256(cfg),
            },
            ["limits"] = new JsonObject { ["max_len"] = 512, ["head_max_len"] = 192, ["min_k"] = 2 },
            ["postProcessing"] = new JsonObject
            {
                ["temperature"] = new JsonArray(1.5, 1.25, 2.0),
                ["temperature_by_options"] = new JsonObject { ["choice:11+"] = 0.5 },
                ["rounding"] = 4,
            },
            ["reference"] = new JsonObject { ["package"] = "laya==0.3.20" },
        };
        Write("tau-model.json", (tweak?.Invoke(m) ?? m).ToJsonString());
    }

    [Fact]
    public void Loads_a_valid_package()
    {
        WriteLayaPackage();
        var p = ModelPackage.Load(_dir);
        Assert.Equal("laya-en", p.Id);
        Assert.Equal(ModelFamily.Laya, p.Family);
        Assert.Equal(new LayaLimits(512, 192, 2), p.LayaLimits);
        Assert.Equal([1.5, 1.25, 2.0], p.LayaPost!.Temperature);
        Assert.Equal(0.5, p.LayaPost.TemperatureByOptions["choice:11+"]);
        Assert.Equal("laya==0.3.20", p.ReferencePackage);
    }

    [Theory]
    [InlineData("model.onnx")]
    [InlineData("model.onnx.data")]
    [InlineData("tokenizer.json")]
    [InlineData("tokenizer_config.json")]
    public void Refuses_a_tampered_file_and_names_it(string file)
    {
        WriteLayaPackage();
        File.AppendAllText(Path.Combine(_dir, file), "tampered");
        var e = Assert.Throws<ModelPackageException>(() => ModelPackage.Load(_dir));
        Assert.EndsWith(file, e.File);
        Assert.Contains("sha256", e.Message);
    }

    [Fact]
    public void Refuses_a_tampered_manifest_hash()
    {
        WriteLayaPackage(m => { m["onnx"]!["sha256"] = new string('0', 64); return m; });
        var e = Assert.Throws<ModelPackageException>(() => ModelPackage.Load(_dir));
        Assert.EndsWith("model.onnx", e.File);
    }

    [Fact]
    public void Refuses_a_missing_manifest()
    {
        var e = Assert.Throws<ModelPackageException>(() => ModelPackage.Load(_dir));
        Assert.Contains("export.ps1", e.Message);
    }

    [Fact]
    public void Refuses_a_missing_weights_file()
    {
        WriteLayaPackage();
        File.Delete(Path.Combine(_dir, "model.onnx.data"));
        var e = Assert.Throws<ModelPackageException>(() => ModelPackage.Load(_dir));
        Assert.Contains("missing", e.Message);
    }

    [Fact]
    public void Refuses_wrong_format_or_family()
    {
        WriteLayaPackage(m => { m["family"] = "gpt"; return m; });
        Assert.Throws<ModelPackageException>(() => ModelPackage.Load(_dir));
    }

    [Fact]
    [Trait("Category", "Models")]
    public void Loads_every_exported_package()
    {
        var root = RepoRoot.Models;
        foreach (var id in new[] { "laya-en", "laya-multilingual", "laya-typed-decisions", "von-1.2.0" })
        {
            var dir = Path.Combine(root, id);
            Assert.True(File.Exists(Path.Combine(dir, "tau-model.json")),
                $"{dir} missing: run scripts/fetch-models.ps1 then scripts/export.ps1");
            var p = ModelPackage.Load(dir);
            Assert.Equal(id, p.Id);
            if (p.Family == ModelFamily.Von)
            {
                Assert.True(p.VonPost!.IndependentOptions);
                Assert.Equal(64, p.VonLimits!.SlidingWindow);
            }
        }
    }
}
