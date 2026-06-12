namespace Unilyze;

internal static class UnitySerializedAssetEnumerator
{
    public static IEnumerable<string> Enumerate(
        string assetsDir,
        IReadOnlyList<string>? excludeDirectories,
        bool applyAnyDepthExcludes)
    {
        foreach (var pattern in new[] { "*.unity", "*.prefab", "*.asset" })
        {
            foreach (var file in Directory.EnumerateFiles(assetsDir, pattern, SearchOption.AllDirectories))
            {
                if (DefaultExcludes.ShouldExcludeSourceFile(
                        file, excludeDirectories, excludeGeneratedCode: false, assetsDir, applyAnyDepthExcludes))
                    continue;
                yield return Path.GetFullPath(file);
            }
        }
    }
}
