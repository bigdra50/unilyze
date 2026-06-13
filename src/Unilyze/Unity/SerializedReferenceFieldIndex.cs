using Unilyze.Pipeline;
namespace Unilyze.Unity;

internal static class SerializedReferenceFieldIndex
{
    public static IReadOnlyDictionary<string, HashSet<string>> Build(IReadOnlyList<TypeNodeInfo> types)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (!IsUnityComponentType(type))
                continue;

            var typeId = TypeIdentity.GetTypeId(type);
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in type.Members)
            {
                if (!IsSerializedField(member) || IsSkippedFieldType(member.Type))
                    continue;
                fields.Add(member.Name);
            }

            if (fields.Count > 0)
                result[typeId] = fields;
        }

        return result;
    }

    static bool IsUnityComponentType(TypeNodeInfo type)
    {
        var role = UnityContextClassifier.ClassifyRole(type, null, null);
        return role is TypeRole.MonoBehaviour or TypeRole.ScriptableObject;
    }

    static bool IsSerializedField(MemberInfo member)
    {
        if (member.MemberKind != "Field")
            return false;
        if (member.Modifiers.Contains("public"))
            return true;

        return member.Attributes.Any(attr =>
            attr.Name.EndsWith("SerializeField", StringComparison.Ordinal));
    }

    static bool IsSkippedFieldType(string fieldType)
    {
        var simpleName = fieldType.TrimEnd('?').Split('<')[0].Split('.')[^1];
        return simpleName is "GameObject" or "Transform" or "Component";
    }
}
