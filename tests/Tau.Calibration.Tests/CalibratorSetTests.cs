using Tau.Calibration;

namespace Tau.Calibration.Tests;

public class CalibratorSetTests
{
    private const string ModelHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static CalibratorFitted Fitted() => new(100, "held-out", null, "2026-09-27", null, null, "tau-fit", "1.0.0");

    private static string NewTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tau-calibrator-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void LoadDirectory_FindsMostSpecificMatch_ModelTypeBucket()
    {
        string dir = NewTempDirectory();
        try
        {
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, null, 1.0, Fitted())
                .Write(Path.Combine(dir, "laya-choice-general.calibrator.json"));
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, OptionBucket.ThreeToFive, 1.5, Fitted())
                .Write(Path.Combine(dir, "laya-choice-3-5.calibrator.json"));

            var set = CalibratorSet.LoadDirectory(dir);

            var specific = set.Find("laya", QuestionType.Choice, optionCount: 4);
            Assert.NotNull(specific);
            Assert.Equal(OptionBucket.ThreeToFive, specific!.Bucket);
            Assert.Equal(1.5, specific.Temperature);

            var general = set.Find("laya", QuestionType.Choice, optionCount: 20);
            Assert.NotNull(general);
            Assert.Null(general!.Bucket);
            Assert.Equal(1.0, general.Temperature);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Find_NoMatch_ReturnsNull()
    {
        string dir = NewTempDirectory();
        try
        {
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, null, 1.0, Fitted())
                .Write(Path.Combine(dir, "laya-choice.calibrator.json"));

            var set = CalibratorSet.LoadDirectory(dir);

            Assert.Null(set.Find("von", QuestionType.Choice, 4));
            Assert.Null(set.Find("laya", QuestionType.Score, 4));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(1, "2")]
    [InlineData(2, "2")]
    [InlineData(3, "3-5")]
    [InlineData(5, "3-5")]
    [InlineData(6, "6-10")]
    [InlineData(10, "6-10")]
    [InlineData(11, "11+")]
    [InlineData(50, "11+")]
    public void Find_UsesLayaBucketingRule(int optionCount, string expectedBucketWire)
    {
        string dir = NewTempDirectory();
        try
        {
            OptionBucket bucket = OptionBucketExtensions.FromWireString(expectedBucketWire)!.Value;
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, bucket, 1.3, Fitted())
                .Write(Path.Combine(dir, "laya-choice-bucket.calibrator.json"));

            var set = CalibratorSet.LoadDirectory(dir);

            Assert.NotNull(set.Find("laya", QuestionType.Choice, optionCount));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDirectory_DuplicateModelTypeBucket_Throws()
    {
        string dir = NewTempDirectory();
        try
        {
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, OptionBucket.ThreeToFive, 1.0, Fitted())
                .Write(Path.Combine(dir, "a.calibrator.json"));
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, OptionBucket.ThreeToFive, 1.7, Fitted())
                .Write(Path.Combine(dir, "b.calibrator.json"));

            var ex = Assert.Throws<CalibratorValidationException>(() => CalibratorSet.LoadDirectory(dir));
            Assert.Contains("duplicate", ex.Problem, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_MismatchedModelHash_Throws()
    {
        string dir = NewTempDirectory();
        try
        {
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, null, 1.0, Fitted())
                .Write(Path.Combine(dir, "laya.calibrator.json"));

            var set = CalibratorSet.LoadDirectory(dir);

            var ex = Assert.Throws<CalibratorValidationException>(() => set.Validate(model => "different-hash"));
            Assert.Contains("hash", ex.Problem, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_ModelNotInstalled_Throws()
    {
        string dir = NewTempDirectory();
        try
        {
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, null, 1.0, Fitted())
                .Write(Path.Combine(dir, "laya.calibrator.json"));

            var set = CalibratorSet.LoadDirectory(dir);

            Assert.Throws<CalibratorValidationException>(() => set.Validate(model => null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_MatchingHash_DoesNotThrow()
    {
        string dir = NewTempDirectory();
        try
        {
            CalibratorFile.CreateTemperature("laya", ModelHash, QuestionType.Choice, null, 1.0, Fitted())
                .Write(Path.Combine(dir, "laya.calibrator.json"));

            var set = CalibratorSet.LoadDirectory(dir);

            set.Validate(model => ModelHash);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
