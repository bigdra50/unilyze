namespace Unilyze;

internal static class TypeNameFormat
{
    public static string NormalizeTypeReference(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return "";

        var normalized = typeName.Trim().TrimEnd('?');
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
            normalized = normalized["global::".Length..];
        if (normalized.EndsWith("[]", StringComparison.Ordinal))
            normalized = normalized[..^2];
        return normalized;
    }

    public static string StripGenericArgs(string typeName)
    {
        var normalized = NormalizeTypeReference(typeName);
        var angleIndex = normalized.IndexOf('<');
        return angleIndex >= 0 ? normalized[..angleIndex] : normalized;
    }

    public static int CountGenericArity(string typeName)
    {
        var normalized = NormalizeTypeReference(typeName);
        var angleStart = normalized.IndexOf('<');
        if (angleStart < 0)
            return 0;

        var angleEnd = normalized.LastIndexOf('>');
        if (angleEnd <= angleStart + 1)
            return 0;

        var inner = normalized[(angleStart + 1)..angleEnd];
        var depth = 0;
        var count = 1;
        foreach (var ch in inner)
        {
            switch (ch)
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    count++;
                    break;
            }
        }

        return count;
    }
}
