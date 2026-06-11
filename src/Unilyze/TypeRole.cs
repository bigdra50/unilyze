using System.Text.Json.Serialization;

namespace Unilyze;

[JsonConverter(typeof(JsonStringEnumConverter<TypeRole>))]
public enum TypeRole
{
    PlainCSharp,
    MonoBehaviour,
    ScriptableObject,
    EditorExtension,
    EcsSystem,
    EcsJob,
    EcsComponentData
}
