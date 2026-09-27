using System.Text.Json.Nodes;
using Tau.Calibration;

namespace Tau.Calibration.Tests;

public class CalibratorFileTests
{
    private const string ValidModelHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static JsonObject ValidTemperatureJson() => new()
    {
        ["format"] = "tau.calibrator",
        ["version"] = 1,
        ["model"] = "laya-base",
        ["modelHash"] = ValidModelHash,
        ["questionType"] = "choice",
        ["bucket"] = "3-5",
        ["method"] = "temperature",
        ["temperature"] = 1.75,
        ["fitted"] = new JsonObject
        {
            ["n"] = 1000,
            ["dataset"] = "held-out",
            ["date"] = "2026-09-27",
            ["tool"] = "tau-fit",
            ["toolVersion"] = "1.0.0",
        },
    };

    private static JsonObject ValidIsotonicJson() => new()
    {
        ["format"] = "tau.calibrator",
        ["version"] = 1,
        ["model"] = "von-base",
        ["modelHash"] = ValidModelHash,
        ["questionType"] = "score",
        ["method"] = "isotonic",
        ["isotonic"] = new JsonObject
        {
            ["x"] = new JsonArray(0.0, 0.5, 1.0),
            ["y"] = new JsonArray(0.0, 0.4, 1.0),
        },
        ["fitted"] = new JsonObject
        {
            ["n"] = 2000,
            ["dataset"] = "held-out",
            ["date"] = "2026-09-27",
            ["eceBefore"] = 0.08,
            ["eceAfter"] = 0.01,
            ["tool"] = "tau-fit",
            ["toolVersion"] = "1.0.0",
        },
    };

    [Fact]
    public void Parse_ValidTemperatureDocument_Succeeds()
    {
        var file = CalibratorFile.Parse(ValidTemperatureJson().ToJsonString());

        Assert.Equal("tau.calibrator", file.Format);
        Assert.Equal(1, file.Version);
        Assert.Equal("laya-base", file.Model);
        Assert.Equal(ValidModelHash, file.ModelHash);
        Assert.Equal(QuestionType.Choice, file.QuestionType);
        Assert.Equal(OptionBucket.ThreeToFive, file.Bucket);
        Assert.Equal(CalibrationMethod.Temperature, file.Method);
        Assert.Equal(1.75, file.Temperature);
        Assert.Null(file.Isotonic);
        Assert.Equal(1000, file.Fitted.N);
    }

    [Fact]
    public void Parse_ValidIsotonicDocument_Succeeds()
    {
        var file = CalibratorFile.Parse(ValidIsotonicJson().ToJsonString());

        Assert.Equal(CalibrationMethod.Isotonic, file.Method);
        Assert.Null(file.Bucket);
        Assert.Null(file.Temperature);
        Assert.NotNull(file.Isotonic);
        Assert.Equal([0.0, 0.5, 1.0], file.Isotonic!.X);
        Assert.Equal([0.0, 0.4, 1.0], file.Isotonic.Y);
        Assert.Equal(0.08, file.Fitted.EceBefore);
        Assert.Equal(0.01, file.Fitted.EceAfter);
    }

    [Fact]
    public void Parse_WrongFormat_Throws()
    {
        var json = ValidTemperatureJson();
        json["format"] = "not.tau.calibrator";

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString(), "test.json"));
        Assert.Contains("format", ex.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("test.json", ex.SourceName);
    }

    [Fact]
    public void Parse_WrongVersion_Throws()
    {
        var json = ValidTemperatureJson();
        json["version"] = 2;

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("version", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // uppercase not allowed
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")] // non-hex chars
    public void Parse_BadModelHash_Throws(string badHash)
    {
        var json = ValidTemperatureJson();
        json["modelHash"] = badHash;

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("modelHash", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_UnknownQuestionType_Throws()
    {
        var json = ValidTemperatureJson();
        json["questionType"] = "essay";

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("questionType", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_UnknownBucket_Throws()
    {
        var json = ValidTemperatureJson();
        json["bucket"] = "12-20";

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("bucket", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_UnknownMethod_Throws()
    {
        var json = ValidTemperatureJson();
        json["method"] = "platt";

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("method", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingTemperatureForTemperatureMethod_Throws()
    {
        var json = ValidTemperatureJson();
        json.Remove("temperature");

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("temperature", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NonPositiveTemperature_Throws()
    {
        var json = ValidTemperatureJson();
        json["temperature"] = 0.0;

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("temperature", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingIsotonicForIsotonicMethod_Throws()
    {
        var json = ValidIsotonicJson();
        json.Remove("isotonic");

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("isotonic", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_IsotonicXNotStrictlyAscending_Throws()
    {
        var json = ValidIsotonicJson();
        ((JsonObject)json["isotonic"]!)["x"] = new JsonArray(0.0, 0.5, 0.5);

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("ascending", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_IsotonicYNotNonDecreasing_Throws()
    {
        var json = ValidIsotonicJson();
        ((JsonObject)json["isotonic"]!)["y"] = new JsonArray(0.5, 0.4, 1.0);

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("non-decreasing", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_IsotonicValuesOutsideZeroOne_Throws()
    {
        var json = ValidIsotonicJson();
        ((JsonObject)json["isotonic"]!)["y"] = new JsonArray(0.0, 0.4, 1.5);

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("[0, 1]", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_IsotonicMismatchedXyLengths_Throws()
    {
        var json = ValidIsotonicJson();
        ((JsonObject)json["isotonic"]!)["x"] = new JsonArray(0.0, 0.5, 0.8, 1.0);

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("length", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_UnknownTopLevelField_Throws()
    {
        var json = ValidTemperatureJson();
        json["extra"] = "nope";

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("unknown field", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_UnknownFieldInFitted_Throws()
    {
        var json = ValidTemperatureJson();
        ((JsonObject)json["fitted"]!)["extra"] = "nope";

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("unknown field", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_UnknownFieldInIsotonic_Throws()
    {
        var json = ValidIsotonicJson();
        ((JsonObject)json["isotonic"]!)["extra"] = "nope";

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("unknown field", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingRequiredField_Throws()
    {
        var json = ValidTemperatureJson();
        json.Remove("model");

        var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse(json.ToJsonString()));
        Assert.Contains("model", ex.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NonObjectRoot_Throws()
    {
        Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse("[1,2,3]"));
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Parse("{not json"));
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<CalibratorValidationException>(() => CalibratorFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".calibrator.json")));
    }

    [Fact]
    public void WriteThenLoad_RoundTripsTemperatureCalibrator()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.calibrator.json");
        try
        {
            var original = CalibratorFile.CreateTemperature(
                "laya-base",
                ValidModelHash,
                QuestionType.Choice,
                OptionBucket.SixToTen,
                temperature: 1.42,
                fitted: new CalibratorFitted(500, "held-out-v2", "rev-3", "2026-09-27", 0.09, 0.02, "tau-fit", "1.0.0"));

            original.Write(path);
            var loaded = CalibratorFile.Load(path);

            Assert.Equal(original.Format, loaded.Format);
            Assert.Equal(original.Version, loaded.Version);
            Assert.Equal(original.Model, loaded.Model);
            Assert.Equal(original.ModelHash, loaded.ModelHash);
            Assert.Equal(original.QuestionType, loaded.QuestionType);
            Assert.Equal(original.Bucket, loaded.Bucket);
            Assert.Equal(original.Method, loaded.Method);
            Assert.Equal(original.Temperature, loaded.Temperature);
            Assert.Equal(original.Fitted, loaded.Fitted);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteThenLoad_RoundTripsIsotonicCalibrator()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.calibrator.json");
        try
        {
            var original = CalibratorFile.CreateIsotonic(
                "von-base",
                ValidModelHash,
                QuestionType.Noul,
                bucket: null,
                isotonic: new IsotonicKnots([0.0, 0.3, 0.6, 1.0], [0.0, 0.35, 0.55, 1.0]),
                fitted: new CalibratorFitted(1500, "held-out", null, "2026-09-27", null, null, "tau-fit", "1.0.0"));

            original.Write(path);
            var loaded = CalibratorFile.Load(path);

            Assert.Equal(original.Method, loaded.Method);
            Assert.Equal(original.Isotonic!.X, loaded.Isotonic!.X);
            Assert.Equal(original.Isotonic.Y, loaded.Isotonic.Y);
            Assert.Null(loaded.Bucket);
            Assert.Null(loaded.Fitted.DatasetRevision);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateTemperature_NonPositiveTemperature_Throws()
    {
        Assert.Throws<ArgumentException>(() => CalibratorFile.CreateTemperature(
            "m", ValidModelHash, QuestionType.Choice, null, 0.0,
            new CalibratorFitted(1, "d", null, "2026-09-27", null, null, "t", "v")));
    }

    [Fact]
    public void CreateTemperature_BadModelHash_Throws()
    {
        Assert.Throws<ArgumentException>(() => CalibratorFile.CreateTemperature(
            "m", "bad-hash", QuestionType.Choice, null, 1.0,
            new CalibratorFitted(1, "d", null, "2026-09-27", null, null, "t", "v")));
    }

    [Fact]
    public void CreateIsotonic_NonAscendingX_Throws()
    {
        Assert.Throws<ArgumentException>(() => CalibratorFile.CreateIsotonic(
            "m", ValidModelHash, QuestionType.Choice, null,
            new IsotonicKnots([0.5, 0.2], [0.1, 0.2]),
            new CalibratorFitted(1, "d", null, "2026-09-27", null, null, "t", "v")));
    }
}
