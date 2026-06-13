using System.Text.Json.Serialization;

namespace Unilyze.Pipeline;

[JsonConverter(typeof(JsonStringEnumConverter<TypeRole>))]
internal enum TypeRole
{
    PlainCSharp,
    MonoBehaviour,
    ScriptableObject,
    EditorExtension,
    EcsSystem,
    EcsJob,
    EcsComponentData
}
