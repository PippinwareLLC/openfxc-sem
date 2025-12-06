using System.Text.Json.Serialization;

namespace OpenFXC.Sem;

internal sealed record EntryPointInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("symbolId")]
    public int? SymbolId { get; init; }

    [JsonPropertyName("stage")]
    public string? Stage { get; init; }

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }
}
