using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceGrpc.ViewModels;

/// <summary>
/// What every page hands its Svelte component: the shell's data and the page's own view class. See ADR 0006.
/// </summary>
public sealed record PageView<TPage>(ShellView Shell, TPage Page);

/// <summary>
/// The strongly typed model of <c>_SvelteLayout</c>: which registered component to mount, and its props.
/// </summary>
public sealed class SveltePage {
    /// <summary>camelCase properties and enum values, matching the TypeScript view types by hand.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public required string Title { get; init; }

    /// <summary>The key the component is registered under in <c>app/src/pages/app.ts</c>.</summary>
    public required string Component { get; init; }

    /// <summary>The <see cref="PageView{TPage}"/> as base64 JSON, so it survives an HTML attribute untouched.</summary>
    public required string Props { get; init; }

    public static SveltePage For<TPage>(string component, string title, PageView<TPage> view) => new() {
        Title = title,
        Component = component,
        Props = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(view, JsonOptions)))
    };
}
