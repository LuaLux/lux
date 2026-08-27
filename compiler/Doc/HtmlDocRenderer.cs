using System.Net;
using System.Text;

namespace Nebra.Doc;

/// <summary>
/// Renders a <see cref="DocSite"/> into a browsable static HTML site:
/// <c>index.html</c> + one page per module under <c>html/&lt;name&gt;.html</c>,
/// shared CSS in <c>html/assets/style.css</c>. No JS, no external assets.
/// Source-code blocks keep their <c>nebra</c> language hint for downstream
/// highlighters that hook the page later.
/// </summary>
public sealed class HtmlDocRenderer
{
    public IReadOnlyDictionary<string, string> Render(DocSite site)
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        pages["html/assets/style.css"] = Css;
        pages["html/index.html"] = RenderIndex(site);
        foreach (var module in site.Modules)
            pages[$"html/modules/{module.Name}.html"] = RenderModule(site, module);
        return pages;
    }

    public void WriteTo(DocSite site, string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var (rel, content) in Render(site))
        {
            var full = Path.Combine(outDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
    }

    private static string RenderIndex(DocSite site)
    {
        var sb = new StringBuilder();
        BeginPage(sb, site, currentSlug: null, "Overview", "");
        sb.AppendLine($"<h1>{Esc(site.ProjectName)}</h1>");
        if (!string.IsNullOrEmpty(site.ProjectVersion))
            sb.AppendLine($"<p class=\"version\">version <code>{Esc(site.ProjectVersion!)}</code></p>");

        sb.AppendLine("<h2>Modules</h2>");
        sb.AppendLine("<ul class=\"module-list\">");
        foreach (var module in site.Modules)
        {
            sb.Append("<li><a href=\"modules/").Append(Esc(module.Name)).Append(".html\"><code>")
              .Append(Esc(module.Name)).Append("</code></a>");
            if (module.Doc != null && !string.IsNullOrEmpty(module.Doc.Summary))
                sb.Append(" <span class=\"summary\">").Append(Esc(SingleLine(module.Doc.Summary))).Append("</span>");
            sb.AppendLine("</li>");
        }
        sb.AppendLine("</ul>");
        EndPage(sb);
        return sb.ToString();
    }

    private static string RenderModule(DocSite site, DocModule module)
    {
        var sb = new StringBuilder();
        BeginPage(sb, site, module.Name, $"Module {module.Name}", "../");
        sb.Append("<h1>Module <code>").Append(Esc(module.Name)).AppendLine("</code></h1>");

        if (module.Doc != null)
            RenderDoc(sb, module.Doc);

        if (module.Symbols.Count == 0)
        {
            sb.AppendLine("<p><em>No exported symbols.</em></p>");
            EndPage(sb);
            return sb.ToString();
        }

        var groups = module.Symbols.GroupBy(s => s.Kind).OrderBy(g => (int)g.Key);
        foreach (var group in groups)
        {
            sb.Append("<h2>").Append(Esc(KindHeading(group.Key))).AppendLine("</h2>");
            foreach (var symbol in group.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                sb.Append("<section class=\"symbol\" id=\"").Append(Esc(SafeId(symbol.Name))).AppendLine("\">");
                sb.Append("<h3><code>").Append(Esc(symbol.Name)).AppendLine("</code></h3>");
                sb.Append("<pre class=\"signature\"><code class=\"language-nebra\">")
                  .Append(Esc(symbol.Signature))
                  .AppendLine("</code></pre>");

                if (symbol.BaseTypes.Count > 0)
                    sb.Append("<p class=\"meta\">Extends: ")
                      .Append(string.Join(", ", symbol.BaseTypes.Select(b => $"<code>{Esc(b)}</code>")))
                      .AppendLine("</p>");
                if (symbol.Implements.Count > 0)
                    sb.Append("<p class=\"meta\">Implements: ")
                      .Append(string.Join(", ", symbol.Implements.Select(b => $"<code>{Esc(b)}</code>")))
                      .AppendLine("</p>");

                if (symbol.Doc != null) RenderDoc(sb, symbol.Doc);

                if (symbol.Members.Count > 0) RenderMembers(sb, symbol);

                sb.AppendLine("</section>");
            }
        }

        EndPage(sb);
        return sb.ToString();
    }

    private static void RenderMembers(StringBuilder sb, DocSymbol symbol)
    {
        var groups = symbol.Members.GroupBy(m => m.Kind).OrderBy(g => (int)g.Key);
        foreach (var group in groups)
        {
            sb.Append("<h4>").Append(Esc(MemberHeading(group.Key))).AppendLine("</h4>");
            foreach (var member in group)
            {
                sb.Append("<section class=\"member\" id=\"").Append(Esc(SafeId(symbol.Name + "." + member.Name))).AppendLine("\">");
                sb.Append("<h5><code>").Append(Esc(member.Name)).AppendLine("</code></h5>");

                var modParts = new List<string>();
                if (member.IsAbstract) modParts.Add("abstract");
                if (member.IsStatic) modParts.Add("static");
                if (member.IsProtected) modParts.Add("protected");
                if (modParts.Count > 0)
                    sb.Append("<p class=\"modifiers\">").Append(Esc(string.Join(" ", modParts))).AppendLine("</p>");

                sb.Append("<pre class=\"signature\"><code class=\"language-nebra\">")
                  .Append(Esc(member.Signature))
                  .AppendLine("</code></pre>");

                if (member.Doc != null) RenderDoc(sb, member.Doc);
                sb.AppendLine("</section>");
            }
        }
    }

    private static void RenderDoc(StringBuilder sb, DocComment doc)
    {
        if (doc.IsEmpty) return;

        if (doc.Deprecated)
        {
            sb.Append("<p class=\"deprecated\"><strong>Deprecated</strong>");
            if (!string.IsNullOrEmpty(doc.DeprecatedReason))
                sb.Append(" — ").Append(Esc(doc.DeprecatedReason));
            sb.AppendLine("</p>");
        }
        if (!string.IsNullOrEmpty(doc.Summary))
            sb.Append("<p class=\"summary\">").Append(Esc(doc.Summary)).AppendLine("</p>");
        if (!string.IsNullOrEmpty(doc.Remarks))
            sb.Append("<p class=\"remarks\">").Append(Esc(doc.Remarks)).AppendLine("</p>");

        if (doc.Params.Count > 0)
        {
            sb.AppendLine("<dl class=\"params\"><dt>Parameters</dt>");
            foreach (var p in doc.Params)
            {
                sb.Append("<dd><code>").Append(Esc(p.Name));
                if (p.Optional) sb.Append('?');
                sb.Append("</code>");
                if (!string.IsNullOrEmpty(p.TypeText)) sb.Append(" <em>(").Append(Esc(p.TypeText!)).Append(")</em>");
                if (!string.IsNullOrEmpty(p.Description)) sb.Append(" — ").Append(Esc(p.Description));
                sb.AppendLine("</dd>");
            }
            sb.AppendLine("</dl>");
        }

        if (doc.Returns.Count > 0)
        {
            sb.AppendLine("<dl class=\"returns\"><dt>Returns</dt>");
            foreach (var r in doc.Returns)
            {
                sb.Append("<dd>");
                if (!string.IsNullOrEmpty(r.Name)) sb.Append("<code>").Append(Esc(r.Name!)).Append("</code> ");
                if (!string.IsNullOrEmpty(r.TypeText)) sb.Append("<em>(").Append(Esc(r.TypeText!)).Append(")</em> ");
                if (!string.IsNullOrEmpty(r.Description)) sb.Append(Esc(r.Description));
                sb.AppendLine("</dd>");
            }
            sb.AppendLine("</dl>");
        }

        if (doc.Examples.Count > 0)
        {
            sb.AppendLine("<h6>Example</h6>");
            foreach (var ex in doc.Examples)
                sb.Append("<pre><code class=\"language-nebra\">").Append(Esc(ex)).AppendLine("</code></pre>");
        }

        if (!string.IsNullOrEmpty(doc.Since))
            sb.Append("<p class=\"since\"><em>Since: ").Append(Esc(doc.Since!)).AppendLine("</em></p>");

        if (doc.See.Count > 0)
            sb.Append("<p class=\"see\"><strong>See also:</strong> ")
              .Append(string.Join(", ", doc.See.Select(s => $"<code>{Esc(s)}</code>")))
              .AppendLine("</p>");
    }

    private static void BeginPage(StringBuilder sb, DocSite site, string? currentSlug, string title, string assetPrefix)
    {
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(Esc(title)).Append(" — ").Append(Esc(site.ProjectName)).AppendLine("</title>");
        sb.Append("<link rel=\"stylesheet\" href=\"").Append(assetPrefix).AppendLine("assets/style.css\">");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<aside class=\"sidebar\">");
        sb.Append("<a class=\"home\" href=\"").Append(assetPrefix).Append("index.html\"><strong>")
          .Append(Esc(site.ProjectName)).AppendLine("</strong></a>");
        if (!string.IsNullOrEmpty(site.ProjectVersion))
            sb.Append("<p class=\"version\">v").Append(Esc(site.ProjectVersion!)).AppendLine("</p>");
        sb.AppendLine("<nav><ul>");
        foreach (var module in site.Modules)
        {
            var isCurrent = currentSlug != null && string.Equals(module.Name, currentSlug, StringComparison.Ordinal);
            sb.Append("<li").Append(isCurrent ? " class=\"current\"" : "").Append(">");
            sb.Append("<a href=\"").Append(assetPrefix).Append("modules/").Append(Esc(module.Name)).Append(".html\">")
              .Append(Esc(module.Name)).Append("</a></li>\n");
        }
        sb.AppendLine("</ul></nav>");
        sb.AppendLine("</aside>");
        sb.AppendLine("<main>");
    }

    private static void EndPage(StringBuilder sb)
    {
        sb.AppendLine("</main>");
        sb.AppendLine("</body></html>");
    }

    private static string SingleLine(string s) =>
        s.Replace('\n', ' ').Replace('\r', ' ').Trim();

    private static string Esc(string s) => WebUtility.HtmlEncode(s);

    private static string SafeId(string s)
    {
        var arr = s.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(arr).Trim('-');
    }

    private static string KindHeading(DocSymbolKind kind) => kind switch
    {
        DocSymbolKind.Function => "Functions",
        DocSymbolKind.Class => "Classes",
        DocSymbolKind.Interface => "Interfaces",
        DocSymbolKind.Enum => "Enums",
        DocSymbolKind.Variable => "Variables",
        _ => kind.ToString()
    };

    private static string MemberHeading(DocMemberKind kind) => kind switch
    {
        DocMemberKind.Field => "Fields",
        DocMemberKind.Method => "Methods",
        DocMemberKind.Constructor => "Constructor",
        DocMemberKind.Accessor => "Accessors",
        DocMemberKind.EnumMember => "Members",
        _ => kind.ToString()
    };

    private const string Css = """
:root {
    --bg: #1a1d23;
    --fg: #d8dee9;
    --muted: #8b95a3;
    --accent: #88c0d0;
    --code-bg: #232830;
    --border: #2e3440;
    --warn: #d08770;
    --sidebar: #161a1f;
}
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; }
body {
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    background: var(--bg);
    color: var(--fg);
    display: grid;
    grid-template-columns: 260px 1fr;
    min-height: 100vh;
}
.sidebar {
    background: var(--sidebar);
    border-right: 1px solid var(--border);
    padding: 1.5rem 1rem;
    overflow-y: auto;
    position: sticky;
    top: 0;
    height: 100vh;
}
.sidebar .home { color: var(--accent); text-decoration: none; font-size: 1.05rem; }
.sidebar .version { color: var(--muted); font-size: 0.85rem; margin: 0.25rem 0 1rem; }
.sidebar nav ul { list-style: none; padding: 0; margin: 0; }
.sidebar nav li { padding: 0.25rem 0; }
.sidebar nav a { color: var(--fg); text-decoration: none; font-size: 0.9rem; }
.sidebar nav a:hover { color: var(--accent); }
.sidebar nav li.current a { color: var(--accent); font-weight: 600; }
main {
    padding: 2rem 3rem;
    max-width: 920px;
    width: 100%;
}
h1, h2, h3, h4, h5, h6 { color: var(--fg); }
h1 { border-bottom: 1px solid var(--border); padding-bottom: 0.5rem; }
h2 { margin-top: 2.5rem; border-bottom: 1px solid var(--border); padding-bottom: 0.3rem; }
h3 { margin-top: 1.75rem; color: var(--accent); }
h4 { margin-top: 1.25rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.04em; font-size: 0.8rem; }
h5 { margin: 1rem 0 0.5rem; color: var(--accent); }
section.symbol, section.member {
    margin: 1rem 0 2rem;
    padding-left: 1rem;
    border-left: 2px solid var(--border);
}
section.member { margin-left: 1rem; }
code, pre {
    font-family: "JetBrains Mono", "Fira Code", Consolas, monospace;
}
code {
    background: var(--code-bg);
    padding: 0.1rem 0.3rem;
    border-radius: 3px;
    font-size: 0.9em;
}
pre {
    background: var(--code-bg);
    padding: 0.75rem 1rem;
    border-radius: 6px;
    overflow-x: auto;
    border: 1px solid var(--border);
}
pre code {
    background: transparent;
    padding: 0;
}
.summary { font-size: 1.05rem; }
.remarks { color: var(--fg); }
.modifiers { font-style: italic; color: var(--muted); margin: 0; }
.meta, .since, .see { color: var(--muted); font-size: 0.9rem; }
.deprecated { background: rgba(208, 135, 112, 0.12); border-left: 3px solid var(--warn); padding: 0.5rem 0.75rem; }
dl { margin: 0.75rem 0; }
dl dt { font-weight: 600; color: var(--accent); margin-bottom: 0.4rem; }
dl dd { margin-left: 1rem; padding: 0.15rem 0; }
.module-list { list-style: none; padding: 0; }
.module-list li { padding: 0.4rem 0; border-bottom: 1px solid var(--border); }
.module-list .summary { color: var(--muted); margin-left: 0.5rem; }
""";
}
