using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tau.Workbench;

/// <summary>
/// JSON and JSONL helpers shared by every stage. Artefacts use snake_case names, invariant number
/// formatting and a trailing newline, and are written atomically (temp file, then rename) so a crashed
/// run never leaves a half-written artefact that looks current.
/// </summary>
public static class WorkbenchJson
{
    /// <summary>Options for indented JSON artefacts (summaries, report.json).</summary>
    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    /// <summary>Options for one-object-per-line JSONL artefacts.</summary>
    public static JsonSerializerOptions Line { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Serialises <paramref name="value"/> as indented JSON and writes it atomically.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="path">Destination file.</param>
    /// <param name="value">The value.</param>
    public static void WriteJson<T>(string path, T value) =>
        WriteAllTextAtomic(path, JsonSerializer.Serialize(value, Indented) + "\n");

    /// <summary>Reads and deserialises an indented JSON artefact.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="path">The file.</param>
    /// <exception cref="WorkbenchException">The file is missing or does not parse.</exception>
    public static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new WorkbenchException($"Expected artefact {path} does not exist. Run the stage that produces it first.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Indented)
                   ?? throw new WorkbenchException($"{path} holds JSON null.");
        }
        catch (JsonException e)
        {
            throw new WorkbenchException($"{path} does not parse: {e.Message}", e);
        }
    }

    /// <summary>Writes <paramref name="values"/> as JSONL (one compact object per line), atomically.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">Destination file.</param>
    /// <param name="values">The records, written in the order given.</param>
    public static void WriteJsonl<T>(string path, IEnumerable<T> values)
    {
        var sb = new StringBuilder();
        foreach (var v in values)
        {
            sb.Append(JsonSerializer.Serialize(v, Line)).Append('\n');
        }

        WriteAllTextAtomic(path, sb.ToString());
    }

    /// <summary>Reads a JSONL file, skipping blank lines.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The file.</param>
    /// <exception cref="WorkbenchException">The file is missing or a line does not parse (the line number is given).</exception>
    public static List<T> ReadJsonl<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new WorkbenchException($"Expected artefact {path} does not exist. Run the stage that produces it first.");
        }

        var result = new List<T>();
        int lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                result.Add(JsonSerializer.Deserialize<T>(line, Line)
                           ?? throw new WorkbenchException($"{path}:{lineNumber} is JSON null."));
            }
            catch (JsonException e)
            {
                throw new WorkbenchException($"{path}:{lineNumber} does not parse: {e.Message}", e);
            }
        }

        return result;
    }

    /// <summary>Writes text to <paramref name="path"/> via a temporary sibling file and a rename.</summary>
    /// <param name="path">Destination file. Its directory is created if missing.</param>
    /// <param name="content">The text (UTF-8, no BOM).</param>
    public static void WriteAllTextAtomic(string path, string content)
    {
        var full = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        var temp = full + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, full, overwrite: true);
    }

    /// <summary>Lower-hex sha256 of a file's bytes.</summary>
    /// <param name="path">The file.</param>
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Lower-hex sha256 of a UTF-8 string.</summary>
    /// <param name="text">The text.</param>
    public static string Sha256Text(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
