using System.Text.Json.Nodes;
using Tau.Inference.Routing;

namespace Tau.Inference.Tests.Routing;

public sealed class PyPrimitivesTests
{
    // Expected values are Python 3.12's round(x, 4), checked in the interpreter.
    [Theory]
    [InlineData(0.03125, 0.0312)]   // exactly representable midpoint: half to even
    [InlineData(0.09375, 0.0938)]
    [InlineData(0.00625, 0.0063)]   // stored a hair above the midpoint
    [InlineData(0.00025, 0.0003)]   // stored a hair above the midpoint
    [InlineData(0.12345, 0.1235)]   // stored a hair above the midpoint
    [InlineData(0.00035, 0.0003)]   // stored a hair below the midpoint
    [InlineData(0.00015, 0.0001)]   // stored a hair below the midpoint
    [InlineData(1.00005, 1.0001)]
    [InlineData(0.5555, 0.5555)]
    [InlineData(0.3125, 0.3125)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0 / 3.0, 0.3333)]
    [InlineData(2.0 / 3.0, 0.6667)]
    public void Round_to_four_places_matches_python(double x, double expected)
    {
        Assert.Equal(expected, PyRound.Round(x, 4));
    }

    [Fact]
    public void Scaled_math_round_would_disagree_with_python()
    {
        // Why PyRound exists: Math.Round scales by 10^4 first, and 0.00625 * 10000 lands exactly on
        // 62.5, which rounds half-even to 62; Python rounds the stored value (just above 0.00625) up.
        Assert.Equal(0.0062, Math.Round(0.00625, 4, MidpointRounding.ToEven));
        Assert.Equal(0.0063, PyRound.Round(0.00625, 4));
        Assert.Equal(0.0004, Math.Round(0.00035, 4, MidpointRounding.ToEven));
        Assert.Equal(0.0003, PyRound.Round(0.00035, 4));
    }

    [Fact]
    public void Tables_answer_known_code_points()
    {
        Assert.True(PyUnicodeTables.IsAlpha('a'));
        Assert.False(PyUnicodeTables.IsAlpha('1'));
        Assert.True(PyUnicodeTables.IsAlpha(0x20000));          // CJK Ext-B
        Assert.False(PyUnicodeTables.IsAlpha(0x1F600));         // emoji
        Assert.True(PyUnicodeTables.IsNumeric(0x00BD));         // ½
        Assert.False(PyUnicodeTables.IsDecimal(0x00BD));
        Assert.True(PyUnicodeTables.IsLetterClass(0x00B2));     // ² is in [^\W\d_]
        Assert.False(PyUnicodeTables.IsLetterClass('_'));
        Assert.False(PyUnicodeTables.IsLetterClass('7'));
        Assert.True(PyUnicodeTables.IsUpper('A'));
        Assert.False(PyUnicodeTables.IsUpper(0x01C5));          // titlecase Dž
        Assert.True(PyUnicodeTables.IsCombining(0x0301));
        Assert.Equal('k', PyUnicodeTables.Lower(0x212A));       // Kelvin sign
        Assert.Equal(0x00DF, PyUnicodeTables.Lower(0x1E9E));    // capital sharp s
        Assert.Equal(0x0130, PyUnicodeTables.Lower(0x0130));    // handled by the caller
        Assert.Equal(0x10FFFF, PyUnicodeTables.Lower(0x10FFFF));
    }

    [Fact]
    public void State_text_counts_code_points_not_utf16_units()
    {
        var text = ScriptAnalyser.StateText(JsonValue.Create(string.Concat(Enumerable.Repeat("𠀀", 4100))));
        Assert.Equal(4000, text.Length);
        Assert.All(text, cp => Assert.Equal(0x20000, cp));
    }
}
