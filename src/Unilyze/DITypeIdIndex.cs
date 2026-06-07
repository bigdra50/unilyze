namespace Unilyze;

// Resolves DI registration endpoint names to TypeIds within the analyzed set.
// Qualified-name keys ("Namespace.Outer.Inner") take precedence; simple-name keys
// resolve only when unique across the analysis set, so ambiguous or external types
// stay unresolved (null) rather than producing a wrong edge.
internal sealed class DITypeIdIndex
{
    readonly Dictionary<string, string?> _byQualifiedName;
    readonly Dictionary<string, string?> _bySimpleName;

    DITypeIdIndex(Dictionary<string, string?> byQualifiedName, Dictionary<string, string?> bySimpleName)
    {
        _byQualifiedName = byQualifiedName;
        _bySimpleName = bySimpleName;
    }

    public static DITypeIdIndex Build(IReadOnlyList<TypeNodeInfo> types)
    {
        // Value is the TypeId when the key maps to exactly one type, or null when ambiguous.
        var byQualifiedName = new Dictionary<string, string?>(StringComparer.Ordinal);
        var bySimpleName = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            var typeId = TypeIdentity.GetTypeId(type);
            Register(byQualifiedName, TypeIdentity.GetQualifiedName(type), typeId);
            Register(bySimpleName, TypeIdentity.GetSimpleName(type), typeId);
        }

        return new DITypeIdIndex(byQualifiedName, bySimpleName);
    }

    // Marks a key ambiguous (null) once a second distinct TypeId maps to it.
    static void Register(Dictionary<string, string?> lookup, string key, string typeId)
    {
        if (string.IsNullOrEmpty(key))
            return;

        if (lookup.TryGetValue(key, out var existing))
        {
            if (existing is not null && existing != typeId)
                lookup[key] = null;
            return;
        }

        lookup[key] = typeId;
    }

    public string? Resolve(string simpleName, string? qualifiedName)
    {
        if (!string.IsNullOrEmpty(qualifiedName)
            && _byQualifiedName.TryGetValue(qualifiedName, out var byQualified)
            && byQualified is not null)
            return byQualified;

        if (!string.IsNullOrEmpty(simpleName)
            && _bySimpleName.TryGetValue(simpleName, out var bySimple))
            return bySimple;

        return null;
    }
}
