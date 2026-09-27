using Tau.Workbench.Calibrate;
using Tau.Workbench.Report;

namespace Tau.Workbench.Tests;

public sealed class SelectionNoteTests
{
    private static ModelReport Report(string chosen, double isoEce, double tempEce) => new()
    {
        Model = "m",
        Calibration = new CalibrationSummary
        {
            Model = "m",
            ModelHash = "h",
            DatasetRevision = "r",
            ExcludedFailures = 0,
            ClassesWithoutCalibrationExamples = [],
            Notes = [],
            Calibrators =
            [
                new WrittenCalibrator("f", "question type", 1000, chosen,
                    new MethodFit("temperature", 7.6, null, tempEce, 1.58), new MethodFit("isotonic", null, 32, isoEce, 1.42), null, 0.24, 2.3),
            ],
        },
    };

    [Fact]
    public void ExplainsWhenTheChosenMethodHadClearlyWorseCalibrationEce()
    {
        var note = Assert.Single(ReportBuilder.SelectionNotes(Report("isotonic", isoEce: 0.20, tempEce: 0.016)));
        Assert.Contains("picked isotonic", note, StringComparison.Ordinal);
        Assert.Contains("not changed after seeing the held-out result", note, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysNothingWhenTheChoiceWasNotClearlyWorseOnEce()
    {
        Assert.Empty(ReportBuilder.SelectionNotes(Report("isotonic", isoEce: 0.03, tempEce: 0.02)));
        Assert.Empty(ReportBuilder.SelectionNotes(Report("temperature", isoEce: 0.20, tempEce: 0.016)));
    }
}
