namespace Tau.Calibration;

/// <summary>
/// A loaded collection of <c>*.calibrator.json</c> files, supporting most-specific-match lookup
/// by model, question type, and option-count bucket.
/// </summary>
public sealed class CalibratorSet
{
    private readonly List<(string Path, CalibratorFile File)> _entries;

    private CalibratorSet(List<(string Path, CalibratorFile File)> entries)
    {
        _entries = entries;
    }

    /// <summary>All loaded calibrators, in load order.</summary>
    public IReadOnlyList<CalibratorFile> All => _entries.Select(e => e.File).ToArray();

    /// <summary>
    /// Loads and validates every <c>*.calibrator.json</c> file under <paramref name="directory"/>,
    /// including subfolders, so one directory can hold the Workbench's <c>calibrators/&lt;model&gt;/</c> tree.
    /// Each file names its own model, and two files for the same key are rejected.
    /// </summary>
    /// <param name="directory">The directory to scan (recursive).</param>
    /// <exception cref="CalibratorValidationException">
    /// Thrown when any file fails to load/validate, or two files resolve to the same
    /// (model, questionType, bucket) key.
    /// </exception>
    public static CalibratorSet LoadDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var entries = new List<(string Path, CalibratorFile File)>();
        var seen = new Dictionary<(string Model, QuestionType QuestionType, OptionBucket? Bucket), string>();

        foreach (string path in Directory.EnumerateFiles(directory, "*.calibrator.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            CalibratorFile file = CalibratorFile.Load(path);
            var key = (file.Model, file.QuestionType, file.Bucket);
            if (seen.TryGetValue(key, out string? existingPath))
            {
                string bucketDescription = file.Bucket?.ToWireString() ?? "(none)";
                throw new CalibratorValidationException(
                    path,
                    $"duplicates calibrator for model='{file.Model}', questionType='{file.QuestionType.ToWireString()}', bucket='{bucketDescription}' already loaded from '{existingPath}'.");
            }

            seen.Add(key, path);
            entries.Add((path, file));
        }

        return new CalibratorSet(entries);
    }

    /// <summary>
    /// Finds the most specific calibrator for <paramref name="model"/>/<paramref name="questionType"/>
    /// and the bucket implied by <paramref name="optionCount"/>: a match on model + type + bucket
    /// wins over a match on model + type with no bucket; if neither exists, returns
    /// <see langword="null"/>.
    /// </summary>
    public CalibratorFile? Find(string model, QuestionType questionType, int optionCount)
    {
        ArgumentNullException.ThrowIfNull(model);
        OptionBucket bucket = OptionBucketExtensions.ForOptionCount(optionCount);

        foreach ((_, CalibratorFile file) in _entries)
        {
            if (file.Model == model && file.QuestionType == questionType && file.Bucket == bucket)
            {
                return file;
            }
        }

        foreach ((_, CalibratorFile file) in _entries)
        {
            if (file.Model == model && file.QuestionType == questionType && file.Bucket is null)
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates that every loaded calibrator's <see cref="CalibratorFile.ModelHash"/> matches the
    /// hash of the model currently installed for it, as reported by <paramref name="modelHashLookup"/>
    /// (keyed by <see cref="CalibratorFile.Model"/>; returning <see langword="null"/> means the
    /// model is not installed).
    /// </summary>
    /// <exception cref="CalibratorValidationException">
    /// Thrown naming the first calibrator whose model is not installed, or whose modelHash does
    /// not match the installed model's hash.
    /// </exception>
    public void Validate(Func<string, string?> modelHashLookup)
    {
        ArgumentNullException.ThrowIfNull(modelHashLookup);

        foreach ((string path, CalibratorFile file) in _entries)
        {
            string? installedHash = modelHashLookup(file.Model);
            if (installedHash is null)
            {
                throw new CalibratorValidationException(path, $"is fitted for model '{file.Model}', which is not installed.");
            }

            if (!string.Equals(installedHash, file.ModelHash, StringComparison.Ordinal))
            {
                throw new CalibratorValidationException(
                    path,
                    $"was fitted on modelHash '{file.ModelHash}' but the installed '{file.Model}' has hash '{installedHash}'.");
            }
        }
    }
}
