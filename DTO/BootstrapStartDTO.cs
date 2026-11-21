namespace DTO;

/// <summary>The Bootstrap authorize URL for the caller to send the user's browser to.</summary>
public record BootstrapStartDTO {
    public string AuthorizeUrl { get; set; } = "";
}
