namespace Unilyze;

internal static class QueryEvidenceAssembler
{
    public static QueryResult Build(AnalysisResult analysis, IReadOnlyList<TypeMetrics> selectedTypes)
    {
        var dependencies = analysis.Dependencies ?? [];
        var packs = selectedTypes
            .Select(type => BuildPack(type, dependencies))
            .ToList();

        return new QueryResult(analysis.ProjectPath, analysis.AnalyzedAt, packs);
    }

    static TypeEvidencePack BuildPack(TypeMetrics type, IReadOnlyList<TypeDependency> dependencies)
    {
        var typeId = TypeIdentity.GetTypeId(type);
        var anchor = FormatAnchor(type.FilePath, type.StartLine);
        var smells = (type.CodeSmells ?? [])
            .Select(s => new TypeEvidenceSmell(
                s.Kind,
                s.Severity,
                s.MethodName,
                s.Message,
                ResolveSmellAnchor(type, s),
                s.Id,
                s.Triage))
            .ToList();

        var inbound = GroupDependencies(
            dependencies.Where(d => MatchesInbound(d, type, typeId)),
            d => d.FromType);
        var outbound = GroupDependencies(
            dependencies.Where(d => MatchesOutbound(d, type, typeId)),
            d => d.ToType);

        var topMethods = type.Methods
            .OrderByDescending(m => m.CognitiveComplexity)
            .ThenBy(m => m.MethodName, StringComparer.Ordinal)
            .Take(3)
            .Select(m => new TypeEvidenceMethod(
                m.MethodName,
                m.CognitiveComplexity,
                FormatAnchor(type.FilePath, m.StartLine)))
            .ToList();

        return new TypeEvidencePack(
            type.TypeName,
            string.IsNullOrEmpty(type.Namespace) ? null : type.Namespace,
            TypeIdentity.GetQualifiedName(type),
            typeId,
            anchor,
            new TypeEvidenceMetrics(
                type.CodeHealth,
                type.Cbo,
                type.Lcom,
                type.Dit,
                type.Wmc,
                type.LineCount,
                type.MethodCount,
                type.MaxCognitiveComplexity,
                type.BoxingCount,
                type.ClosureCaptureCount,
                type.ParamsAllocationCount),
            smells,
            inbound,
            outbound,
            topMethods);
    }

    static IReadOnlyList<TypeEvidenceDependencyGroup> GroupDependencies(
        IEnumerable<TypeDependency> deps,
        Func<TypeDependency, string> peerSelector) =>
        deps.GroupBy(d => d.Kind)
            .OrderBy(g => g.Key)
            .Select(g => new TypeEvidenceDependencyGroup(
                g.Key,
                g.Count(),
                g.Select(peerSelector)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList()))
            .ToList();

    static bool MatchesInbound(TypeDependency dep, TypeMetrics type, string typeId)
    {
        if (!string.IsNullOrEmpty(dep.ToTypeId))
            return dep.ToTypeId == typeId;
        return dep.ToType.Equals(type.TypeName, StringComparison.Ordinal)
            || dep.ToType.Equals(TypeIdentity.GetQualifiedName(type), StringComparison.Ordinal);
    }

    static bool MatchesOutbound(TypeDependency dep, TypeMetrics type, string typeId)
    {
        if (!string.IsNullOrEmpty(dep.FromTypeId))
            return dep.FromTypeId == typeId;
        return dep.FromType.Equals(type.TypeName, StringComparison.Ordinal)
            || dep.FromType.Equals(TypeIdentity.GetQualifiedName(type), StringComparison.Ordinal);
    }

    static string? ResolveSmellAnchor(TypeMetrics type, CodeSmell smell)
    {
        if (smell.Line is > 0)
            return FormatAnchor(type.FilePath, smell.Line);

        if (smell.MethodName != null)
        {
            var methodLine = type.Methods
                .FirstOrDefault(m => m.MethodName == smell.MethodName)
                ?.StartLine;
            if (methodLine is > 0)
                return FormatAnchor(type.FilePath, methodLine);
        }

        return FormatAnchor(type.FilePath, type.StartLine);
    }

    static string? FormatAnchor(string? filePath, int? line)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;
        return line is > 0 ? $"{filePath}:{line}" : filePath;
    }
}
