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

    [ViewContext] public ViewContext ViewContext { get; set; } = null!;

    public ViteEntryTagHelper(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;
        _config = config;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        var port = _config.GetValue<int>("Vite:DevServerPort", 5173);
        var distDir = _config.GetValue<string>("Vite:DistDir") ?? "dist";

        if (!_env.IsProduction())
        {
            var script = new TagBuilder("script");
            script.Attributes["type"] = "module";
            script.Attributes["src"] = $"http://localhost:{port}/{distDir}/@vite/client";
            output.Content.AppendHtml(script);

            var entryScript = new TagBuilder("script");
            entryScript.Attributes["type"] = "module";
            entryScript.Attributes["src"] = $"http://localhost:{port}/{distDir}/{Src}";
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
            output.Content.AppendHtml(script);
        }
    }
}
