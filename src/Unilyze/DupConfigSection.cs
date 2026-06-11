using System.Text.Json.Serialization;

namespace Unilyze;

internal sealed record DupConfigSection(
    [property: JsonPropertyName("minTokens")]
    int? MinTokens = null,
    [property: JsonPropertyName("thirdPartyDirs")]
    IReadOnlyList<string>? ThirdPartyDirs = null);
