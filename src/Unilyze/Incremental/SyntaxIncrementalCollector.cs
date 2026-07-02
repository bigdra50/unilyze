using Unilyze.Discovery;
using Unilyze.Metrics;
using Unilyze.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

        // At a semantic level the syntax trees back a real CSharpCompilation, so the cached
        // (unchanged) files must contribute trees too — otherwise every cached type's
        // SemanticModel cannot resolve symbols declared in the files we skipped, corrupting
        // CBO/DIT/RFC for the whole project. Parse them for trees only; they stay out of
        // reparsedFiles so they are never marked for re-enrichment.
        if (options.RequestedLevel != AnalysisLevel.Syntax)
            CompleteSyntaxTreeSet(
                scannedFiles, syntaxTrees, reparsedFiles, parseOptions, options.EffectiveMaxParallelism);

        var mergedRawTypes = rawTypesByFile.Values
            .SelectMany(types => types)
            .OrderBy(t => t.FilePath, StringComparer.Ordinal)
            .ThenBy(t => t.StartLine)
            .ToList();
        var mergedTypes = TypeAnalyzer.ApplySyntaxPostProcessing(mergedRawTypes).ToList();
        var knownInterfacesHashes = ComputeKnownInterfacesHashes(mergedRawTypes, scannedFiles);

        var isSemantic = options.RequestedLevel != AnalysisLevel.Syntax;
        var globalUsingsHashes = isSemantic
            ? ComputeGlobalUsingsHashes(scannedFiles, syntaxTrees)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var usingsHashByFile = ComputeUsingsHashByFile(scannedFiles, syntaxTrees, reparsedFiles, existingByPath);

        // The body-only fast path is only sound at a semantic level once a cache exists; the
        // cold/no-cache path reparses everything (so SEED already covers every type).
        var requiresFullReEnrich = isSemantic && existing is not null && HasStructuralChange(
            existing, existingByPath, scannedFiles, reparsedFiles, rawTypesByFile, globalUsingsHashes,
            usingsHashByFile, log);

        var manifestDraft = new SyntaxCacheManifest(
            SyntaxCacheFingerprint.SchemaVersion,
            fingerprint,
            knownInterfacesHashes,
            [],
            globalUsingsHashes);

        return new SyntaxIncrementalCollectResult(
            mergedTypes,
            syntaxTrees,
            rawTypesByFile,
            cachedEnrichment,
            reparsedFiles,
            manifestDraft,
            requiresFullReEnrich,
            usingsHashByFile);
    }

    static bool HasStructuralChange(
        SyntaxCacheManifest existing,
        IReadOnlyDictionary<string, SyntaxCacheFileEntry> existingByPath,
        IReadOnlyList<FileScanEntry> scannedFiles,
        IReadOnlySet<string> reparsedFiles,
        IReadOnlyDictionary<string, IReadOnlyList<TypeNodeInfo>> rawTypesByFile,
        IReadOnlyDictionary<string, string> currentGlobalUsingsHashes,
        IReadOnlyDictionary<string, string> currentUsingsHashByFile,
        IAnalysisLogSink log)
    {
        var scannedByRelative = scannedFiles.ToDictionary(f => f.RelativePath, f => f, StringComparer.Ordinal);

        if (scannedFiles.Any(f => !existingByPath.ContainsKey(f.RelativePath)))
            return LogFullReEnrich(log, "a source file was added");
        if (existing.Files.Any(e => !scannedByRelative.ContainsKey(e.RelativePath)))
            return LogFullReEnrich(log, "a source file was deleted");

        foreach (var file in scannedFiles)
        {
            if (!reparsedFiles.Contains(file.AbsolutePath))
                continue;
            if (!existingByPath.TryGetValue(file.RelativePath, out var oldEntry)
                || !rawTypesByFile.TryGetValue(file.AbsolutePath, out var newRawTypes))
                continue;
            if (StructuralChangeDetector.FileStructureChanged(oldEntry.RawTypes, newRawTypes))
                return LogFullReEnrich(log, $"declaration shape changed in {file.RelativePath}");
            // A per-file using retarget (e.g. an alias pointing at a different type) can shift
            // what an unqualified name in this file resolves to without touching any
            // declaration shape FileStructureChanged looks at, so it must independently force
            // a full re-enrich rather than falling through to the body-only fast path.
            if (currentUsingsHashByFile.TryGetValue(file.AbsolutePath, out var newUsingsHash)
                && !string.Equals(oldEntry.UsingsHash, newUsingsHash, StringComparison.Ordinal))
                return LogFullReEnrich(log, $"using directives changed in {file.RelativePath}");
        }

        var cachedGlobalUsings = existing.GlobalUsingsHashesByAssembly
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (GlobalUsingsChanged(cachedGlobalUsings, currentGlobalUsingsHashes))
            return LogFullReEnrich(log, "the global using set changed");

        return false;
    }

    static bool LogFullReEnrich(IAnalysisLogSink log, string reason)
    {
        log.Info($"[incremental] full re-enrich: {reason}");
        return true;
    }

    static bool GlobalUsingsChanged(
        IReadOnlyDictionary<string, string> cached,
        IReadOnlyDictionary<string, string> current)
    {
        if (cached.Count != current.Count)
            return true;
        foreach (var (assembly, hash) in cached)
        {
            if (!current.TryGetValue(assembly, out var currentHash)
                || !string.Equals(hash, currentHash, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    static Dictionary<string, string> ComputeGlobalUsingsHashes(
        IReadOnlyList<FileScanEntry> scannedFiles,
        IReadOnlyList<SyntaxTree> syntaxTrees)
    {
        var assemblyByPath = scannedFiles.ToDictionary(
            f => f.AbsolutePath, f => f.Assembly, StringComparer.Ordinal);
        var byAssembly = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var tree in syntaxTrees)
        {
            var path = Path.GetFullPath(tree.FilePath);
            if (!assemblyByPath.TryGetValue(path, out var assembly)
                || tree.GetRoot() is not CompilationUnitSyntax root)
                continue;

            foreach (var directive in root.Usings)
            {
                if (!directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
                    continue;
                if (!byAssembly.TryGetValue(assembly, out var list))
                {
                    list = [];
                    byAssembly[assembly] = list;
                }
                list.Add(NormalizeUsingDirective(directive));
            }
        }

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (assembly, usings) in byAssembly)
            hashes[assembly] = SyntaxCacheFingerprint.HashStrings(usings.OrderBy(u => u, StringComparer.Ordinal));
        return hashes;
    }

    static string NormalizeUsingDirective(UsingDirectiveSyntax directive) =>
        string.Join(' ', directive.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // Per-file using-directive hash keyed by absolute path: reuses the cached value for files
    // that were not reparsed this generation, and recomputes from the fresh tree otherwise.
    // `global using` directives are excluded — they are already covered project-wide by
    // ComputeGlobalUsingsHashes/GlobalUsingsChanged, so including them here would only produce
    // a duplicate (and differently-worded) full-re-enrich reason for the same root cause.
    static Dictionary<string, string> ComputeUsingsHashByFile(
        IReadOnlyList<FileScanEntry> scannedFiles,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        IReadOnlySet<string> reparsedFiles,
        IReadOnlyDictionary<string, SyntaxCacheFileEntry> existingByPath)
    {
        var treeByPath = syntaxTrees.ToDictionary(t => Path.GetFullPath(t.FilePath), t => t, StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in scannedFiles)
        {
            if (reparsedFiles.Contains(entry.AbsolutePath) && treeByPath.TryGetValue(entry.AbsolutePath, out var tree))
            {
                result[entry.AbsolutePath] = ComputeFileUsingsHash(tree);
                continue;
            }

            result[entry.AbsolutePath] = existingByPath.TryGetValue(entry.RelativePath, out var cached)
                ? cached.UsingsHash
                : SyntaxCacheFingerprint.HashStrings([]);
        }

        return result;
    }

    // Order-insensitive, whitespace-insensitive hash of a file's non-global using directives,
    // including ones nested inside a namespace block (not just the ones directly under the
    // compilation unit).
    internal static string ComputeFileUsingsHash(SyntaxTree tree)
    {
        if (tree.GetRoot() is not CompilationUnitSyntax root)
            return SyntaxCacheFingerprint.HashStrings([]);

        var usings = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(u => !u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
            .Select(NormalizeUsingDirective)
            .OrderBy(u => u, StringComparer.Ordinal);
        return SyntaxCacheFingerprint.HashStrings(usings);
    }

    public static SyntaxCacheManifest BuildManifest(
        string projectRoot,
        SyntaxIncrementalCollectResult collect,
        IReadOnlyList<TypeMetrics> enrichedMetrics,
        IReadOnlyList<TypeNodeInfo> resolvedTypes,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? usedTypesByTypeId = null)
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

            var usedTypes = usedTypesByTypeId?.GetValueOrDefault(typeId) ?? [];
            list.Add(new SyntaxCacheEnrichedType(typeId, metrics, usedTypes));
        }

        var files = new List<SyntaxCacheFileEntry>();
        foreach (var (absolutePath, rawTypes) in collect.RawTypesByFile.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            var assembly = rawTypes.FirstOrDefault()?.Assembly ?? "Assembly-CSharp";
            var relativePath = Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
            var hash = SyntaxCacheFingerprint.HashFileContent(absolutePath);
            enrichedByFile.TryGetValue(Path.GetFullPath(absolutePath), out var enrichedTypes);
            var usingsHash = collect.UsingsHashByFile?.GetValueOrDefault(absolutePath) ?? string.Empty;
            files.Add(new SyntaxCacheFileEntry(
                relativePath,
                hash,
                assembly,
                rawTypes,
                enrichedTypes ?? [],
                usingsHash));
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

    static void CompleteSyntaxTreeSet(
        IReadOnlyList<FileScanEntry> scannedFiles,
        List<SyntaxTree> syntaxTrees,
        IReadOnlySet<string> reparsedFiles,
        CSharpParseOptions parseOptions,
        int maxParallelism)
    {
        var treedPaths = new HashSet<string>(
            syntaxTrees.Select(t => t.FilePath), StringComparer.Ordinal);

        var toParse = scannedFiles
            .Where(e => !reparsedFiles.Contains(e.AbsolutePath) && !treedPaths.Contains(e.AbsolutePath))
            .ToList();

        // Match the full collector's parallel parse: a sequential reparse of the cached files
        // here would erase much of the enrichment-elision win on a large project.
        var parsed = new System.Collections.Concurrent.ConcurrentBag<SyntaxTree>();
        Parallel.ForEach(toParse, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism }, entry =>
        {
            var (tree, _) = TypeAnalyzer.ParseSingleFile(entry.AbsolutePath, entry.Assembly, parseOptions);
            parsed.Add(tree);
        });
        syntaxTrees.AddRange(parsed);
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
