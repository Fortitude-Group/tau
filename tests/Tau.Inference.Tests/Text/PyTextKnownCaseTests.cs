using System.Text.Json.Nodes;
using Tau.Inference.Text;

namespace Tau.Inference.Tests.Text;

/// <summary>Hand-written cases that document the rules; the fixture tests cover the corpus.</summary>
public class PyTextKnownCaseTests
{
    [Theory]
    [InlineData("1", "1")]
    [InlineData("-0", "0")]
    [InlineData("1180591620717411303424", "1180591620717411303424")] // 2**70
    [InlineData("1.0", "1.0")]
    [InlineData("1e2", "100.0")]
    [InlineData("-0.0", "-0.0")]
    [InlineData("0.1", "0.1")]
    [InlineData("1e-5", "1e-05")]
    [InlineData("0.0001", "0.0001")]
    [InlineData("1e16", "1e+16")]
    [InlineData("9999999999999998.0", "9999999999999998.0")]
    [InlineData("1.5e300", "1.5e+300")]
    [InlineData("5e-324", "5e-324")]
    [InlineData("1e400", "Infinity")]
    [InlineData("-1e400", "-Infinity")]
    [InlineData("\"caf\\u00e9 \\u0001 \\u007f\"", "\"café \\u0001 \u007f\"")]
    [InlineData("\"tab\\t\\\"q\\\" back\\\\\"", "\"tab\\t\\\"q\\\" back\\\\\"")]
    [InlineData("{\"b\": 1, \"a\": [true, false, null]}", "{\"b\": 1, \"a\": [true, false, null]}")]
    [InlineData("{}", "{}")]
    [InlineData("[]", "[]")]
    [InlineData("null", "null")]
    public void Dumps_known_cases(string raw, string expected) =>
        Assert.Equal(expected, PyJson.Dumps(JsonNode.Parse(raw)));

    [Theory]
    [InlineData("1e400", "inf")]
    [InlineData("-1e400", "-inf")]
    [InlineData("1e-5", "1e-05")]
    [InlineData("\"plain\"", "plain")]
    [InlineData("true", "True")]
    [InlineData("null", "None")]
    [InlineData("{\"a\": 1, \"b\": true, \"c\": null}", "{'a': 1, 'b': True, 'c': None}")]
    [InlineData("[\"it's\"]", "[\"it's\"]")]
    [InlineData("[\"say \\\"hi\\\"\"]", "['say \"hi\"']")]
    [InlineData("[\"both ' and \\\"\"]", "['both \\' and \"']")]
    [InlineData("[\"\\u00a0\\u00ad\\u3000\\u200b\\ud83d\\ude00\\u00e9\"]", "['\\xa0\\xad\\u3000\\u200b\U0001F600é']")]
    [InlineData("[\"\\u0000\\u007f\\t\\n\\r\\\\\"]", "['\\x00\\x7f\\t\\n\\r\\\\']")]
    [InlineData("[\"\\udb40\\udc01\"]", "['\\U000e0001']")]
    public void Str_known_cases(string raw, string expected) =>
        Assert.Equal(expected, PyRepr.Str(JsonNode.Parse(raw)));

    [Fact]
    public void Str_of_null_node_is_None() => Assert.Equal("None", PyRepr.Str(null));

    [Fact]
    public void Dumps_of_null_node_is_null() => Assert.Equal("null", PyJson.Dumps(null));

    [Fact]
    public void DumpsState_returns_a_string_state_unquoted()
    {
        Assert.Equal("line \"one\"\nline two", PyJson.DumpsState(JsonNode.Parse("\"line \\\"one\\\"\\nline two\"")));
        Assert.Equal("{\"a\": 1}", PyJson.DumpsState(JsonNode.Parse("{\"a\":1}")));
        Assert.Equal("null", PyJson.DumpsState(null));
    }

    [Fact]
    public void FormatVonState_renders_one_line_per_key()
    {
        var state = JsonNode.Parse("{\"name\": \"Ana\", \"tags\": [\"a\", \"b\"], \"meta\": {\"k\": 1.0}, \"n\": null}");
        Assert.Equal("name: Ana\ntags: ['a', 'b']\nmeta: {'k': 1.0}\nn: None", PyRepr.FormatVonState(state));
        Assert.Equal(string.Empty, PyRepr.FormatVonState(new JsonObject()));
        Assert.Equal("raw text", PyRepr.FormatVonState(JsonValue.Create("raw text")));
        Assert.Equal("[1, 2]", PyRepr.FormatVonState(JsonNode.Parse("[1,2]")));
        Assert.Equal("None", PyRepr.FormatVonState(null));
    }

    [Fact]
    public void Code_built_values_are_classified_by_clr_type()
    {
        // No original spelling to go on: a double is a Python float, an integer type a Python int.
        Assert.Equal("100.0", PyJson.Dumps(JsonValue.Create(100.0)));
        Assert.Equal("1e-05", PyJson.Dumps(JsonValue.Create(1e-5)));
        Assert.Equal("100", PyJson.Dumps(JsonValue.Create(100)));
        Assert.Equal("100", PyJson.Dumps(JsonValue.Create(100L)));
        Assert.Equal("0.5", PyRepr.Str(JsonValue.Create(0.5f)));
        Assert.Equal("[1, 'x', True]", PyRepr.Str(new JsonArray(1, "x", true)));
        Assert.Equal("{\"k\": [1.5, null]}", PyJson.Dumps(new JsonObject { ["k"] = new JsonArray(1.5, null) }));
    }

    [Fact]
    public void Printable_table_follows_python_not_dotnet()
    {
        // U+2EBF0 was assigned in Unicode 15.1, after Python 3.12's 15.0 database: Python escapes it.
        Assert.Equal("['\\U0002ebf0']", PyRepr.Str(new JsonArray("\U0002EBF0")));
        Assert.True(PyUnicodePrintable.IsPrintable('a'));
        Assert.True(PyUnicodePrintable.IsPrintable(' '));
        Assert.False(PyUnicodePrintable.IsPrintable(0x00A0));
        Assert.False(PyUnicodePrintable.IsPrintable(0x10FFFF));
        Assert.True(PyUnicodePrintable.IsPrintable(0x1F600));
    }
}
