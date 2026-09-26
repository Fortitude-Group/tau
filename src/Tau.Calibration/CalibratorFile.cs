using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tau.Calibration;

/// <summary>Isotonic calibration knots, as stored in a <see cref="CalibratorFile"/>.</summary>
/// <param name="X">Knot x-coordinates: ascending, each in <c>[0, 1]</c>.</param>
/// <param name="Y">Knot y-coordinates: non-decreasing, each in <c>[0, 1]</c>.</param>
public sealed record IsotonicKnots(IReadOnlyList<double> X, IReadOnlyList<double> Y);

/// <summary>Provenance recorded alongside a fitted calibrator, per <c>tau.calibrator</c> v1.</summary>
/// <param name="N">Number of held-out samples the calibrator was fitted on.</param>
/// <param name="Dataset">Dataset identifier the calibrator was fitted on.</param>
/// <param name="DatasetRevision">Optional dataset revision/version.</param>
/// <param name="Date">The fitting date (as recorded by the fitting tool; not format-validated here).</param>
/// <param name="EceBefore">Optional expected calibration error before fitting.</param>
/// <param name="EceAfter">Optional expected calibration error after fitting.</param>
/// <param name="Tool">Name of the tool that produced the calibrator.</param>
/// <param name="ToolVersion">Version of the tool that produced the calibrator.</param>
public sealed record CalibratorFitted(
    int N,
    string Dataset,
    string? DatasetRevision,
    string Date,
    double? EceBefore,
    double? EceAfter,
    string Tool,
    string ToolVersion);

/// <summary>
/// A parsed, validated <c>tau.calibrator</c> v1 file: what model/question-type/option-bucket it
/// applies to, which method it uses, and the fitted parameters for that method.
/// </summary>
/// <remarks>
/// Construct via <see cref="Load"/> or <see cref="Parse"/>, both of which fully validate the
/// document and throw <see cref="CalibratorValidationException"/> naming the offending file and
/// the specific problem on any violation of the <c>tau.calibrator</c> v1 contract
/// (<c>specs/001-runtime-onnx-parity/contracts/calibrator.schema.json</c>).
/// </remarks>
public sealed record CalibratorFile
{
    private static readonly Regex ModelHashPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    private CalibratorFile(
        string model,
        string modelHash,
        QuestionType questionType,
        OptionBucket? bucket,
        CalibrationMethod method,
        double? temperature,
        IsotonicKnots? isotonic,
        CalibratorFitted fitted)
    {
        Model = model;
        ModelHash = modelHash;
        QuestionType = questionType;
        Bucket = bucket;
        Method = method;
        Temperature = temperature;
        Isotonic = isotonic;
        Fitted = fitted;
    }

    /// <summary>The fixed format discriminator, always <c>"tau.calibrator"</c>.</summary>
    public string Format => "tau.calibrator";

    /// <summary>The fixed format version, always <c>1</c>.</summary>
    public int Version => 1;

    /// <summary>The model identifier this calibrator applies to.</summary>
    public string Model { get; }

    /// <summary>The lower-hex sha256 hash of the ONNX model this calibrator was fitted on.</summary>
    public string ModelHash { get; }

    /// <summary>The question type this calibrator applies to.</summary>
    public QuestionType QuestionType { get; }

    /// <summary>The optional option-count bucket this calibrator applies to.</summary>
    public OptionBucket? Bucket { get; }

    /// <summary>The calibration method.</summary>
    public CalibrationMethod Method { get; }

    /// <summary>The fitted temperature. Present when <see cref="Method"/> is <see cref="CalibrationMethod.Temperature"/>.</summary>
    public double? Temperature { get; }

    /// <summary>The fitted isotonic knots. Present when <see cref="Method"/> is <see cref="CalibrationMethod.Isotonic"/>.</summary>
    public IsotonicKnots? Isotonic { get; }

    /// <summary>Fitting provenance.</summary>
    public CalibratorFitted Fitted { get; }

    /// <summary>Loads and validates a <c>tau.calibrator</c> v1 file from disk.</summary>
    /// <param name="path">The file path to load.</param>
    /// <exception cref="CalibratorValidationException">Thrown when the document fails validation.</exception>
    public static CalibratorFile Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CalibratorValidationException(path, $"could not be read: {ex.Message}");
        }

        return Parse(json, path);
    }

    /// <summary>Parses and validates a <c>tau.calibrator</c> v1 document from a JSON string.</summary>
    /// <param name="json">The JSON document text.</param>
    /// <param name="source">An identifier for the document, used in error messages (defaults to <c>"&lt;string&gt;"</c>).</param>
    /// <exception cref="CalibratorValidationException">Thrown when the document fails validation.</exception>
    public static CalibratorFile Parse(string json, string source = "<string>")
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CalibratorValidationException(source, $"is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CalibratorValidationException(source, "root must be a JSON object.");
            }

            RejectUnknownProperties(root, source, "root",
                "format", "version", "model", "modelHash", "questionType", "bucket", "method", "temperature", "isotonic", "fitted");

            string format = RequireString(root, source, "root", "format");
            if (format != "tau.calibrator")
            {
                throw new CalibratorValidationException(source, $"has unsupported format '{format}' (expected 'tau.calibrator').");
            }

            int version = RequireInt(root, source, "root", "version");
            if (version != 1)
            {
                throw new CalibratorValidationException(source, $"has unsupported version {version} (expected 1).");
            }

            string model = RequireString(root, source, "root", "model");
            string modelHash = RequireString(root, source, "root", "modelHash");
            if (!ModelHashPattern.IsMatch(modelHash))
            {
                throw new CalibratorValidationException(source, $"has an invalid modelHash '{modelHash}' (expected 64 lower-case hex characters).");
            }

            string questionTypeRaw = RequireString(root, source, "root", "questionType");
            QuestionType? questionType = QuestionTypeExtensions.FromWireString(questionTypeRaw);
            if (questionType is null)
            {
                throw new CalibratorValidationException(source, $"has an unknown questionType '{questionTypeRaw}' (expected choice, score, or noul).");
            }

            OptionBucket? bucket = null;
            if (root.TryGetProperty("bucket", out JsonElement bucketElement))
            {
                string bucketRaw = ExpectString(bucketElement, source, "bucket");
                bucket = OptionBucketExtensions.FromWireString(bucketRaw);
                if (bucket is null)
                {
                    throw new CalibratorValidationException(source, $"has an unknown bucket '{bucketRaw}' (expected 2, 3-5, 6-10, or 11+).");
                }
            }

            string methodRaw = RequireString(root, source, "root", "method");
            CalibrationMethod? method = CalibrationMethodExtensions.FromWireString(methodRaw);
            if (method is null)
            {
                throw new CalibratorValidationException(source, $"has an unknown method '{methodRaw}' (expected temperature or isotonic).");
            }

            double? temperature = null;
            if (method == CalibrationMethod.Temperature)
            {
                if (!root.TryGetProperty("temperature", out JsonElement temperatureElement))
                {
                    throw new CalibratorValidationException(source, "has method 'temperature' but is missing the 'temperature' field.");
                }

                temperature = ExpectNumber(temperatureElement, source, "temperature");
                if (temperature <= 0)
                {
                    throw new CalibratorValidationException(source, $"has a non-positive temperature {temperature.Value.ToString(CultureInfo.InvariantCulture)}.");
                }
            }
            else if (root.TryGetProperty("temperature", out JsonElement extraTemperatureElement))
            {
                temperature = ExpectNumber(extraTemperatureElement, source, "temperature");
                if (temperature <= 0)
                {
                    throw new CalibratorValidationException(source, $"has a non-positive temperature {temperature.Value.ToString(CultureInfo.InvariantCulture)}.");
                }
            }

            IsotonicKnots? isotonic = null;
            if (method == CalibrationMethod.Isotonic)
            {
                if (!root.TryGetProperty("isotonic", out JsonElement isotonicElement))
                {
                    throw new CalibratorValidationException(source, "has method 'isotonic' but is missing the 'isotonic' field.");
                }

                isotonic = ParseIsotonic(isotonicElement, source);
            }
            else if (root.TryGetProperty("isotonic", out JsonElement extraIsotonicElement))
            {
                isotonic = ParseIsotonic(extraIsotonicElement, source);
            }

            if (!root.TryGetProperty("fitted", out JsonElement fittedElement))
            {
                throw new CalibratorValidationException(source, "is missing the required 'fitted' field.");
            }

            CalibratorFitted fitted = ParseFitted(fittedElement, source);

            return new CalibratorFile(model, modelHash, questionType.Value, bucket, method.Value, temperature, isotonic, fitted);
        }
    }

    /// <summary>Serialises this calibrator to stable, indented JSON and writes it to <paramref name="path"/>.</summary>
    public void Write(string path)
    {
        var obj = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["model"] = Model,
            ["modelHash"] = ModelHash,
            ["questionType"] = QuestionType.ToWireString(),
        };

        if (Bucket is not null)
        {
            obj["bucket"] = Bucket.Value.ToWireString();
        }

        obj["method"] = Method.ToWireString();

        if (Temperature is not null)
        {
            obj["temperature"] = Temperature.Value;
        }

        if (Isotonic is not null)
        {
            var isotonicObj = new JsonObject
            {
                ["x"] = new JsonArray(Isotonic.X.Select(v => JsonValue.Create(v)).ToArray()),
                ["y"] = new JsonArray(Isotonic.Y.Select(v => JsonValue.Create(v)).ToArray()),
            };
            obj["isotonic"] = isotonicObj;
        }

        var fittedObj = new JsonObject
        {
            ["n"] = Fitted.N,
            ["dataset"] = Fitted.Dataset,
        };
        if (Fitted.DatasetRevision is not null)
        {
            fittedObj["datasetRevision"] = Fitted.DatasetRevision;
        }

        fittedObj["date"] = Fitted.Date;
        if (Fitted.EceBefore is not null)
        {
            fittedObj["eceBefore"] = Fitted.EceBefore.Value;
        }

        if (Fitted.EceAfter is not null)
        {
            fittedObj["eceAfter"] = Fitted.EceAfter.Value;
        }

        fittedObj["tool"] = Fitted.Tool;
        fittedObj["toolVersion"] = Fitted.ToolVersion;
        obj["fitted"] = fittedObj;

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = obj.ToJsonString(options);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    /// <summary>Creates a temperature-method calibrator.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="temperature"/> is not positive.</exception>
    public static CalibratorFile CreateTemperature(
        string model,
        string modelHash,
        QuestionType questionType,
        OptionBucket? bucket,
        double temperature,
        CalibratorFitted fitted)
    {
        ValidateModelHash(modelHash);
        if (temperature <= 0)
        {
            throw new ArgumentException("Temperature must be strictly positive.", nameof(temperature));
        }

        return new CalibratorFile(model, modelHash, questionType, bucket, CalibrationMethod.Temperature, temperature, null, fitted);
    }

    /// <summary>Creates an isotonic-method calibrator.</summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the knots have mismatched lengths, fewer than two points, non-ascending x,
    /// non-decreasing-violating y, or values outside <c>[0, 1]</c>.
    /// </exception>
    public static CalibratorFile CreateIsotonic(
        string model,
        string modelHash,
        QuestionType questionType,
        OptionBucket? bucket,
        IsotonicKnots isotonic,
        CalibratorFitted fitted)
    {
        ValidateModelHash(modelHash);
        ArgumentNullException.ThrowIfNull(isotonic);
        ValidateIsotonicKnots(isotonic, "<isotonic>", forConstruction: true);

        return new CalibratorFile(model, modelHash, questionType, bucket, CalibrationMethod.Isotonic, null, isotonic, fitted);
    }

    private static void ValidateModelHash(string modelHash)
    {
        if (modelHash is null || !ModelHashPattern.IsMatch(modelHash))
        {
            throw new ArgumentException("modelHash must be 64 lower-case hex characters.", nameof(modelHash));
        }
    }

    private static IsotonicKnots ParseIsotonic(JsonElement element, string source)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CalibratorValidationException(source, "'isotonic' must be a JSON object.");
        }

        RejectUnknownProperties(element, source, "isotonic", "x", "y");

        if (!element.TryGetProperty("x", out JsonElement xElement) || xElement.ValueKind != JsonValueKind.Array)
        {
            throw new CalibratorValidationException(source, "'isotonic.x' is required and must be an array.");
        }

        if (!element.TryGetProperty("y", out JsonElement yElement) || yElement.ValueKind != JsonValueKind.Array)
        {
            throw new CalibratorValidationException(source, "'isotonic.y' is required and must be an array.");
        }

        var x = xElement.EnumerateArray().Select(e => ExpectNumber(e, source, "isotonic.x")).ToArray();
        var y = yElement.EnumerateArray().Select(e => ExpectNumber(e, source, "isotonic.y")).ToArray();

        if (x.Length < 2 || y.Length < 2)
        {
            throw new CalibratorValidationException(source, "'isotonic.x'/'isotonic.y' must each contain at least 2 points.");
        }

        var knots = new IsotonicKnots(x, y);
        ValidateIsotonicKnots(knots, source, forConstruction: false);
        return knots;
    }

    private static void ValidateIsotonicKnots(IsotonicKnots knots, string source, bool forConstruction)
    {
        void Throw(string problem)
        {
            if (forConstruction)
            {
                throw new ArgumentException(problem, nameof(knots));
            }

            throw new CalibratorValidationException(source, problem);
        }

        if (knots.X.Count != knots.Y.Count)
        {
            Throw("'isotonic.x' and 'isotonic.y' must have the same length.");
        }

        if (knots.X.Count < 2)
        {
            Throw("'isotonic' must contain at least 2 points.");
        }

        for (int i = 0; i < knots.X.Count; i++)
        {
            if (knots.X[i] < 0 || knots.X[i] > 1)
            {
                Throw($"'isotonic.x[{i}]' = {knots.X[i].ToString(CultureInfo.InvariantCulture)} is outside [0, 1].");
            }

            if (knots.Y[i] < 0 || knots.Y[i] > 1)
            {
                Throw($"'isotonic.y[{i}]' = {knots.Y[i].ToString(CultureInfo.InvariantCulture)} is outside [0, 1].");
            }

            if (i > 0)
            {
                if (knots.X[i] <= knots.X[i - 1])
                {
                    Throw("'isotonic.x' is not strictly ascending.");
                }

                if (knots.Y[i] < knots.Y[i - 1])
                {
                    Throw("'isotonic.y' is not non-decreasing.");
                }
            }
        }
    }

    private static CalibratorFitted ParseFitted(JsonElement element, string source)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CalibratorValidationException(source, "'fitted' must be a JSON object.");
        }

        RejectUnknownProperties(element, source, "fitted",
            "n", "dataset", "datasetRevision", "date", "eceBefore", "eceAfter", "tool", "toolVersion");

        int n = RequireInt(element, source, "fitted", "n");
        if (n < 1)
        {
            throw new CalibratorValidationException(source, $"has 'fitted.n' = {n}, must be >= 1.");
        }

        string dataset = RequireString(element, source, "fitted", "dataset");
        string? datasetRevision = OptionalString(element, source, "fitted", "datasetRevision");
        string date = RequireString(element, source, "fitted", "date");
        double? eceBefore = OptionalNumber(element, source, "fitted", "eceBefore");
        double? eceAfter = OptionalNumber(element, source, "fitted", "eceAfter");
        string tool = RequireString(element, source, "fitted", "tool");
        string toolVersion = RequireString(element, source, "fitted", "toolVersion");

        return new CalibratorFitted(n, dataset, datasetRevision, date, eceBefore, eceAfter, tool, toolVersion);
    }

    private static void RejectUnknownProperties(JsonElement element, string source, string context, params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowedSet.Contains(property.Name))
            {
                throw new CalibratorValidationException(source, $"has an unknown field '{property.Name}' in '{context}'.");
            }
        }
    }

    private static string RequireString(JsonElement parent, string source, string context, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
        {
            throw new CalibratorValidationException(source, $"is missing the required '{propertyName}' field in '{context}'.");
        }

        return ExpectString(element, source, propertyName);
    }

    private static string? OptionalString(JsonElement parent, string source, string context, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
        {
            return null;
        }

        return ExpectString(element, source, propertyName);
    }

    private static string ExpectString(JsonElement element, string source, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new CalibratorValidationException(source, $"'{propertyName}' must be a string.");
        }

        return element.GetString()!;
    }

    private static int RequireInt(JsonElement parent, string source, string context, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
        {
            throw new CalibratorValidationException(source, $"is missing the required '{propertyName}' field in '{context}'.");
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int value))
        {
            throw new CalibratorValidationException(source, $"'{propertyName}' must be an integer.");
        }

        return value;
    }

    private static double ExpectNumber(JsonElement element, string source, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            throw new CalibratorValidationException(source, $"'{propertyName}' must be a number.");
        }

        return element.GetDouble();
    }

    private static double? OptionalNumber(JsonElement parent, string source, string context, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
        {
            return null;
        }

        return ExpectNumber(element, source, propertyName);
    }
}
