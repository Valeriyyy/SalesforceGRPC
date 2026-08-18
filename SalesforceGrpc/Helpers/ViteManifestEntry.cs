using System.Text.Json.Serialization;

namespace SalesforceGrpc.Helpers;

public class ViteManifestEntry
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("src")]
    public string? Src { get; set; }

    [JsonPropertyName("isEntry")]
    public bool IsEntry { get; set; }

    [JsonPropertyName("css")]
    public List<string>? Css { get; set; }

    [JsonPropertyName("assets")]
    public List<string>? Assets { get; set; }
}
