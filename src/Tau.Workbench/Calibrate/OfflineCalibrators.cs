using System.Text.Json;
using System.Text.Json.Nodes;
using Tau.Calibration;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Calibrate;

/// <summary>
/// Calibrators for a hosted endpoint, which can't load calibrators and has no model hash. They are fitted exactly as
/// for a local model but written as offline-only files, applied by the Workbench and never by the Runtime. Two things
/// keep them away from the Runtime: the file name ends in <see cref="Suffix"/>, which the Runtime's loader
/// (<c>*.calibrator.json</c>) doesn't match, and the file is a wrapper marked <c>"offlineOnly": true</c> rather than a
/// calibrator document, so even a renamed copy fails validation instead of loading.
/// </summary>
public static class OfflineCalibrators
{
    /// <summary>The file-name ending of an offline-only calibrator.</summary>
    public const string Suffix = ".offline-calibrator.json";

    /// <summary>Why a hosted endpoint's calibrators are offline only, for notes and the report.</summary>
    public const string Why =
        "a hosted endpoint can't load calibrators and has no model hash, so its calibrators are applied by the Workbench only, and its calibrated view is the offline one. The files end in .offline-calibrator.json, which the Runtime never loads.";

    /// <summary>The offline-only file name for a calibrator.</summary>
    /// <param name="model">The hosted endpoint's id.</param>
    /// <param name="questionType">The question type's wire name.</param>
    /// <param name="bucket">The bucket's file-name form, or null for the type-level calibrator.</param>
    public static string FileName(string model, string questionType, string? bucket) =>
        bucket is null ? $"{model}.{questionType}{Suffix}" : $"{model}.{questionType}.{bucket}{Suffix}";

    /// <summary>Writes an offline-only calibrator.</summary>
    /// <param name="path">The file (its name must end in <see cref="Suffix"/>).</param>
    /// <param name="calibrator">The fitted calibrator.</param>
    /// <param name="hosted">The hosted endpoint it was fitted for.</param>
    /// <param name="boundTo">What the calibrator's modelHash field holds, in words.</param>
    /// <exception cref="ArgumentException">The name doesn't end in <see cref="Suffix"/>.</exception>
    public static void Write(string path, CalibratorFile calibrator, ExternalModelSpec hosted, string boundTo)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(calibrator);
        ArgumentNullException.ThrowIfNull(hosted);
        if (!path.EndsWith(Suffix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"An offline-only calibrator's name must end in {Suffix}.", nameof(path));
        }

        var doc = new JsonObject
        {
            ["offlineOnly"] = true,
            ["why"] = "Offline only: " + Why,
            ["hostedEndpoint"] = new JsonObject { ["id"] = hosted.Id, ["endpoint"] = hosted.Endpoint.AbsoluteUri, ["model"] = hosted.Model },
            ["modelHashIs"] = boundTo,
            ["calibrator"] = calibrator.ToJson(),
        };
        WorkbenchJson.WriteAllTextAtomic(path, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    /// <summary>Reads an offline-only calibrator back.</summary>
    /// <param name="path">The file.</param>
    /// <exception cref="WorkbenchException">The file is not an offline-only calibrator.</exception>
    public static CalibratorFile Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject { } doc
            || doc["offlineOnly"] is not JsonValue flag || !flag.TryGetValue<bool>(out var offline) || !offline
            || doc["calibrator"] is not JsonObject calibrator)
        {
            throw new WorkbenchException($"{path} is not an offline-only calibrator.");
        }

        return CalibratorFile.Parse(calibrator.ToJsonString(), path);
    }
}
