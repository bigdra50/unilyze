namespace Unilyze;

public sealed record AssemblyMetrics(
    string AssemblyName,
    int TypeCount,
    int ClassCount,
    int RecordCount,
    int InterfaceCount,
    int EnumCount,
    int DelegateCount,
    int PublicTypeCount,
    int SealedTypeCount,
    int TotalMembers,
    IReadOnlyList<string> Namespaces)
{
    public static AssemblyMetrics Compute(string assemblyName, IReadOnlyList<TypeNodeInfo> types)
    {
        return new AssemblyMetrics(
            AssemblyName: assemblyName,
            TypeCount: types.Count,
            ClassCount: types.Count(t => t.Kind == "class"),
            RecordCount: types.Count(t => t.Kind is "record" or "record struct"),
            InterfaceCount: types.Count(t => t.Kind == "interface"),
            EnumCount: types.Count(t => t.Kind == "enum"),
            DelegateCount: types.Count(t => t.Kind == "delegate"),
            PublicTypeCount: types.Count(t => t.Modifiers.Contains("public")),
            SealedTypeCount: types.Count(t => t.Modifiers.Contains("sealed")),
            TotalMembers: types.Sum(t => t.Members.Count),
            Namespaces: types.Select(t => t.Namespace).Where(n => n.Length > 0).Distinct().Order().ToList());
    }
}
