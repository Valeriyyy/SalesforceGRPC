using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesforceGrpc.Helpers;

namespace SalesforceGrpc.TagHelpers;

[HtmlTargetElement("vite-entry", TagStructure = TagStructure.WithoutEndTag)]
public class ViteEntryTagHelper : TagHelper
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    [HtmlAttributeName("src")]
    public string Src { get; set; } = string.Empty;

    /// <summary>
    /// The entry's stylesheet, linked directly from the Vite dev server so the first paint is styled. In
    /// Production the manifest lists the built CSS instead, and this is not used.
    /// </summary>
    [HtmlAttributeName("css")]
    public string? Css { get; set; }

    [ViewContext] public ViewContext ViewContext { get; set; } = null!;

    public ViteEntryTagHelper(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;
        _config = config;
    }

    /// <remarks>
    /// The entry script is marked <c>blocking="render"</c>: every page is drawn by Svelte in the browser, so
    /// without it the browser paints the empty mount point first and a navigation flashes a blank page. Browsers
    /// that do not support the attribute ignore it and paint early, as before.
    /// </remarks>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        var port = _config.GetValue<int>("Vite:DevServerPort", 5173);
        var distDir = _config.GetValue<string>("Vite:DistDir") ?? "dist";

        if (!_env.IsProduction())
        {
            // Vite's dev server normally adds CSS from JavaScript, after every module has loaded, which leaves the
            // first paint unstyled. It serves the file as plain CSS to a <link> as well. The JavaScript copy still
            // arrives and is the one hot reload updates; the linked copy is refreshed by a full reload.
            if (!string.IsNullOrEmpty(Css))
            {
                var link = new TagBuilder("link");
                link.Attributes["rel"] = "stylesheet";
                link.Attributes["href"] = $"http://localhost:{port}/{distDir}/{Css}";
                output.Content.AppendHtml(link);
            }

            var script = new TagBuilder("script");
            script.Attributes["type"] = "module";
            script.Attributes["src"] = $"http://localhost:{port}/{distDir}/@vite/client";
            output.Content.AppendHtml(script);

            var entryScript = new TagBuilder("script");
            entryScript.Attributes["type"] = "module";
            entryScript.Attributes["src"] = $"http://localhost:{port}/{distDir}/{Src}";
            entryScript.Attributes["blocking"] = "render";
            output.Content.AppendHtml(entryScript);
        }
        else
        {
            var entry = ViteManifest.GetEntry(Src, distDir);
            if (entry == null) return;

            if(entry.Css != null)
            {
                foreach (var cssFile in entry.Css)
                {
                    var link = new TagBuilder("link");
                    link.Attributes["rel"] = "stylesheet";
                    link.Attributes["href"] = $"/{distDir}/{cssFile}";
                    output.Content.AppendHtml(link);
                }
            }

            var script = new TagBuilder("script");
            script.Attributes["type"] = "module";
            script.Attributes["src"] = $"/{distDir}/{entry.File}";
            script.Attributes["blocking"] = "render";
            output.Content.AppendHtml(script);
        }
    }
}
