using System.Text;

namespace Nebra.Doc;

/// <summary>
/// Renders a <see cref="DocSite"/> into a tree of Markdown files: an
/// <c>index.md</c> with the module list and one <c>&lt;module&gt;.md</c>
/// per module under <c>modules/</c>. Output is writable into any static-site
/// generator (MkDocs, Docusaurus, Hugo) without further processing.
/// </summary>
public sealed class MarkdownDocRenderer
{
    public IReadOnlyDictionary<string, string> Render(DocSite site)
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        pages["index.md"] = RenderIndex(site);
        foreach (var module in site.Modules)
            pages[$"modules/{module.Name}.md"] = RenderModule(module);
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
        sb.AppendLine("---");
        sb.Append("title: ").AppendLine(EscapeYaml(site.ProjectName));
        if (!string.IsNullOrEmpty(site.ProjectVersion))
            sb.Append("version: ").AppendLine(EscapeYaml(site.ProjectVersion));
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append("# ").AppendLine(site.ProjectName);
        if (!string.IsNullOrEmpty(site.ProjectVersion))
            sb.Append("Version `").Append(site.ProjectVersion).AppendLine("`");
        sb.AppendLine();
        sb.AppendLine("## Modules");
        sb.AppendLine();
        foreach (var module in site.Modules)
        {
            sb.Append("- [`").Append(module.Name).Append("`](modules/").Append(module.Name).Append(".md)");
            if (module.Doc != null && !string.IsNullOrEmpty(module.Doc.Summary))
                sb.Append(" — ").Append(SingleLine(module.Doc.Summary));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string RenderModule(DocModule module)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.Append("title: ").AppendLine(EscapeYaml(module.Name));
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append("# Module `").Append(module.Name).AppendLine("`");
        sb.AppendLine();

        if (module.Doc != null)
        {
            var rendered = DocMarkdown.Render(module.Doc);
            if (!string.IsNullOrEmpty(rendered)) sb.AppendLine(rendered).AppendLine();
        }

        if (module.Symbols.Count == 0)
        {
            sb.AppendLine("_No exported symbols._");
            return sb.ToString();
        }

        var groups = module.Symbols.GroupBy(s => s.Kind).OrderBy(g => (int)g.Key);
        foreach (var group in groups)
        {
            sb.AppendLine().Append("## ").AppendLine(KindHeading(group.Key));
            foreach (var symbol in group.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                sb.AppendLine();
                sb.Append("### `").Append(symbol.Name).AppendLine("`");
                sb.AppendLine();
                sb.AppendLine("```nebra");
                sb.AppendLine(symbol.Signature);
                sb.AppendLine("```");

                if (symbol.BaseTypes.Count > 0)
                    sb.AppendLine().Append("Extends: ").AppendLine(string.Join(", ", symbol.BaseTypes.Select(b => $"`{b}`")));
                if (symbol.Implements.Count > 0)
                    sb.AppendLine().Append("Implements: ").AppendLine(string.Join(", ", symbol.Implements.Select(b => $"`{b}`")));

                if (symbol.Doc != null)
                {
                    var rendered = DocMarkdown.Render(symbol.Doc);
                    if (!string.IsNullOrEmpty(rendered)) sb.AppendLine().AppendLine(rendered);
                }

                if (symbol.Members.Count > 0)
                    RenderMembers(sb, symbol);
            }
        }

        return sb.ToString();
    }

    private static void RenderMembers(StringBuilder sb, DocSymbol symbol)
    {
        var byKind = symbol.Members.GroupBy(m => m.Kind).OrderBy(g => (int)g.Key);
        foreach (var group in byKind)
        {
            sb.AppendLine().Append("#### ").AppendLine(MemberHeading(group.Key));
            foreach (var member in group)
            {
                sb.AppendLine();
                sb.Append("##### `").Append(member.Name).AppendLine("`");
                if (member.IsStatic || member.IsProtected || member.IsAbstract)
                {
                    var mods = new List<string>();
                    if (member.IsAbstract) mods.Add("abstract");
                    if (member.IsStatic) mods.Add("static");
                    if (member.IsProtected) mods.Add("protected");
                    sb.Append("_").Append(string.Join(" ", mods)).AppendLine("_");
                }
                sb.AppendLine();
                sb.AppendLine("```nebra");
                sb.AppendLine(member.Signature);
                sb.AppendLine("```");
                if (member.Doc != null)
                {
                    var rendered = DocMarkdown.Render(member.Doc);
                    if (!string.IsNullOrEmpty(rendered)) sb.AppendLine().AppendLine(rendered);
                }
            }
        }
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

    private static string SingleLine(string s) =>
        s.Replace('\n', ' ').Replace('\r', ' ').Trim();

    private static string EscapeYaml(string s) =>
        s.Contains(':') || s.Contains('"') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
}
