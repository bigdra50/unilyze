namespace Unilyze.Serve;

/// <summary>
/// Maps the input-stamp delta between two successive analyses to the opaque fileIds the
/// live viewer focuses after an edit. Pure: the previous-stamp baseline is owned by
/// <see cref="SnapshotBuilder"/>. A null baseline (the first build) yields nothing, so the
/// initial snapshot never triggers a spurious focus.
/// </summary>
internal static class ServeChangedFiles
{
    public static IReadOnlyList<string> Detect(
        IReadOnlyDictionary<string, string>? previousStamps,
        IReadOnlyDictionary<string, string> currentStamps,
        IReadOnlyDictionary<string, string> fileIdToDisplayPath)
    {
        if (previousStamps is null)
            return [];

        var changedPaths = new HashSet<string>(SourcePathBoundary.PathComparer);
        foreach (var (path, stamp) in currentStamps)
        {
            if (!previousStamps.TryGetValue(path, out var prior)
                || !string.Equals(prior, stamp, StringComparison.Ordinal))
                changedPaths.Add(path);
        }

        if (changedPaths.Count == 0)
            return [];

        // Both sides express a source file as Path.GetRelativePath(projectRoot, file) with
        // forward slashes, so a fileId's display path matches the stamp's relative key.
        // Inputs that carry no fileId (config / .meta / .csproj / .dll) simply don't match.
        return fileIdToDisplayPath
            .Where(entry => changedPaths.Contains(entry.Value))
            .Select(entry => entry.Key)
            .ToList();
    }
}
