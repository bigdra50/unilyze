using Unilyze.Discovery;
using Unilyze.Metrics;
using Unilyze.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Incremental;

internal static class SyntaxIncrementalCollector
{
    sealed record FileScanEntry(
        string RelativePath,
        string AbsolutePath,
        string Assembly,
        string ContentHash);

    public static SyntaxIncrementalCollectResult Collect(
        PipelineDiscoverState discover,
        AnalysisBuildOptions options)
    {
        var log = options.EffectiveLog;
        var fingerprint = SyntaxCacheFingerprint.ComputeGlobalFingerprint(discover, options, discover.Targets);
        var existing = SyntaxCacheStore.TryLoad(discover.ProjectRoot, fingerprint);
        var existingByPath = existing?.Files.ToDictionary(
            f => f.RelativePath,
            f => f,
            StringComparer.Ordinal) ?? new Dictionary<string, SyntaxCacheFileEntry>(StringComparer.Ordinal);

        var parseOptions = BuildParseOptions(discover.PreprocessorSymbols);
        var scannedFiles = EnumerateScannedFiles(discover, options);
        var filesToParse = new HashSet<string>(StringComparer.Ordinal);
        var rawTypesByFile = new Dictionary<string, IReadOnlyList<TypeNodeInfo>>(StringComparer.Ordinal);
        var syntaxTrees = new List<SyntaxTree>();
        var cachedEnrichment = new Dictionary<string, SyntaxCacheEnrichedType>(StringComparer.Ordinal);

        foreach (var entry in scannedFiles)
        {
            if (existingByPath.TryGetValue(entry.RelativePath, out var cached)
                && string.Equals(cached.ContentHash, entry.ContentHash, StringComparison.Ordinal))
            {
                rawTypesByFile[entry.AbsolutePath] = cached.RawTypes;
                foreach (var enriched in cached.EnrichedTypes)
                    cachedEnrichment[enriched.TypeId] = enriched;
                log.Info($"[incremental] cache hit: {entry.RelativePath}");
                continue;
            }

            filesToParse.Add(entry.AbsolutePath);
        }

        ExpandPartialInvalidations(rawTypesByFile, filesToParse);
        var reparsedFiles = new HashSet<string>(StringComparer.Ordinal);
        ParseFiles(filesToParse, rawTypesByFile, syntaxTrees, parseOptions, scannedFiles, reparsedFiles, log);

        ExpandInterfaceInvalidations(
            scannedFiles, rawTypesByFile, existing?.KnownInterfacesHashesByAssembly, filesToParse, reparsedFiles, log);
        ParseFiles(filesToParse, rawTypesByFile, syntaxTrees, parseOptions, scannedFiles, reparsedFiles, log);

        var mergedRawTypes = rawTypesByFile.Values
            .SelectMany(types => types)
            .OrderBy(t => t.FilePath, StringComparer.Ordinal)
            .ThenBy(t => t.StartLine)
            .ToList();
        var mergedTypes = TypeAnalyzer.ApplySyntaxPostProcessing(mergedRawTypes).ToList();
        var knownInterfacesHashes = ComputeKnownInterfacesHashes(mergedRawTypes, scannedFiles);

        var manifestDraft = new SyntaxCacheManifest(
            SyntaxCacheFingerprint.SchemaVersion,
            fingerprint,
            knownInterfacesHashes,
            []);

        return new SyntaxIncrementalCollectResult(
            mergedTypes,
            syntaxTrees,
            rawTypesByFile,
            cachedEnrichment,
            reparsedFiles,
            manifestDraft);
    }

    public static SyntaxCacheManifest BuildManifest(
        string projectRoot,
        SyntaxIncrementalCollectResult collect,
        IReadOnlyList<TypeMetrics> enrichedMetrics,
        IReadOnlyList<TypeNodeInfo> resolvedTypes)
    {
        var metricsByTypeId = enrichedMetrics.ToDictionary(
            m => TypeIdentity.GetTypeId(m),
            m => SyntaxCacheMetrics.StripCouplingFields(m),
            StringComparer.Ordinal);

        var enrichedByFile = new Dictionary<string, List<SyntaxCacheEnrichedType>>(StringComparer.Ordinal);
        foreach (var type in resolvedTypes)
        {
            var typeId = TypeIdentity.GetTypeId(type);
            if (!metricsByTypeId.TryGetValue(typeId, out var metrics))
                continue;

            var filePath = Path.GetFullPath(type.FilePath);
            if (!enrichedByFile.TryGetValue(filePath, out var list))
            {
                list = [];
                enrichedByFile[filePath] = list;
            }

            list.Add(new SyntaxCacheEnrichedType(typeId, metrics));
        }

        var files = new List<SyntaxCacheFileEntry>();
        foreach (var (absolutePath, rawTypes) in collect.RawTypesByFile.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            var assembly = rawTypes.FirstOrDefault()?.Assembly ?? "Assembly-CSharp";
            var relativePath = Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
            var hash = SyntaxCacheFingerprint.HashFileContent(absolutePath);
            enrichedByFile.TryGetValue(Path.GetFullPath(absolutePath), out var enrichedTypes);
            files.Add(new SyntaxCacheFileEntry(
                relativePath,
                hash,
                assembly,
                rawTypes,
                enrichedTypes ?? []));
        }

        return collect.ManifestDraft with { Files = files };
    }

    static CSharpParseOptions BuildParseOptions(IReadOnlyList<string> preprocessorSymbols)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        if (preprocessorSymbols is { Count: > 0 })
            parseOptions = parseOptions.WithPreprocessorSymbols(preprocessorSymbols);
        return parseOptions;
    }

    static List<FileScanEntry> EnumerateScannedFiles(
        PipelineDiscoverState discover,
        AnalysisBuildOptions options)
    {
        var files = new List<FileScanEntry>();
        foreach (var asm in discover.Targets)
        {
            var mergedExcludes = AnalysisPipelineDiscovery.MergeExcludeDirectoriesPublic(
                asm.ExcludeDirectories, options.ExcludeDirectories);
            var csFiles = Directory
                .EnumerateFiles(asm.Directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !DefaultExcludes.ShouldExcludeSourceFile(
                    f, mergedExcludes, options.ExcludeGeneratedCode, asm.Directory, options.ApplyAnyDepthExcludes));

            foreach (var absolutePath in csFiles)
            {
                var relativePath = Path.GetRelativePath(discover.ProjectRoot, absolutePath)
                    .Replace('\\', '/');
                files.Add(new FileScanEntry(
                    relativePath,
                    Path.GetFullPath(absolutePath),
                    asm.Name,
                    SyntaxCacheFingerprint.HashFileContent(absolutePath)));
            }
        }

        return files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
    }

    static void ParseFiles(
        HashSet<string> filesToParse,
        Dictionary<string, IReadOnlyList<TypeNodeInfo>> rawTypesByFile,
        List<SyntaxTree> syntaxTrees,
        CSharpParseOptions parseOptions,
        IReadOnlyList<FileScanEntry> scannedFiles,
        HashSet<string> reparsedFiles,
        IAnalysisLogSink log)
    {
        var scanByAbsolute = scannedFiles.ToDictionary(f => f.AbsolutePath, f => f, StringComparer.Ordinal);
        foreach (var absolutePath in filesToParse.OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!scanByAbsolute.TryGetValue(absolutePath, out var scan))
                continue;

            if (reparsedFiles.Contains(absolutePath))
                continue;

            syntaxTrees.RemoveAll(t => string.Equals(t.FilePath, absolutePath, StringComparison.Ordinal));
            var (tree, rawTypes) = TypeAnalyzer.ParseSingleFile(absolutePath, scan.Assembly, parseOptions);
            syntaxTrees.Add(tree);
            rawTypesByFile[absolutePath] = rawTypes;
            reparsedFiles.Add(absolutePath);
            log.Info($"[incremental] re-parsed: {scan.RelativePath}");
        }
    }

    static void ExpandPartialInvalidations(
        IReadOnlyDictionary<string, IReadOnlyList<TypeNodeInfo>> rawTypesByFile,
        HashSet<string> filesToParse)
    {
        var partialGroups = rawTypesByFile.Values
            .SelectMany(types => types)
            .Where(t => t.Modifiers.Contains("partial"))
            .GroupBy(TypeIdentity.GetTypeId, StringComparer.Ordinal);

        foreach (var group in partialGroups)
        {
            var groupFiles = group.Select(t => Path.GetFullPath(t.FilePath)).Distinct(StringComparer.Ordinal).ToList();
            if (groupFiles.Any(filesToParse.Contains))
            {
                foreach (var file in groupFiles)
                    filesToParse.Add(file);
            }
        }
    }

    static void ExpandInterfaceInvalidations(
        IReadOnlyList<FileScanEntry> scannedFiles,
        IReadOnlyDictionary<string, IReadOnlyList<TypeNodeInfo>> rawTypesByFile,
        IReadOnlyDictionary<string, string>? cachedKnownInterfacesHashes,
        HashSet<string> filesToParse,
        HashSet<string> reparsedFiles,
        IAnalysisLogSink log)
    {
        if (cachedKnownInterfacesHashes is null || cachedKnownInterfacesHashes.Count == 0)
            return;

        var currentHashes = ComputeKnownInterfacesHashes(
            rawTypesByFile.Values.SelectMany(v => v).ToList(),
            scannedFiles);

        foreach (var asmGroup in scannedFiles.GroupBy(f => f.Assembly, StringComparer.Ordinal))
        {
            currentHashes.TryGetValue(asmGroup.Key, out var currentHash);
            cachedKnownInterfacesHashes.TryGetValue(asmGroup.Key, out var cachedHash);
            if (string.Equals(currentHash, cachedHash, StringComparison.Ordinal))
                continue;

            log.Info($"[incremental] interface set changed in assembly {asmGroup.Key}; re-parsing assembly files");
            foreach (var file in asmGroup)
            {
                filesToParse.Add(file.AbsolutePath);
                reparsedFiles.Remove(file.AbsolutePath);
            }
        }
    }

    static Dictionary<string, string> ComputeKnownInterfacesHashes(
        IReadOnlyList<TypeNodeInfo> mergedRawTypes,
        IReadOnlyList<FileScanEntry> scannedFiles)
    {
        var fileAssembly = scannedFiles.ToDictionary(
            f => Path.GetFullPath(f.AbsolutePath),
            f => f.Assembly,
            StringComparer.Ordinal);

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asmGroup in mergedRawTypes.GroupBy(t => fileAssembly.GetValueOrDefault(Path.GetFullPath(t.FilePath), t.Assembly)))
        {
            hashes[asmGroup.Key] = TypeAnalyzer.ComputeKnownInterfacesHash(asmGroup.ToList());
        }

        return hashes;
    }
}
