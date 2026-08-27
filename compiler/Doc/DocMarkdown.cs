using System.Text;

namespace Nebra.Doc;

/// <summary>
/// Renders a <see cref="DocComment"/> as Markdown for LSP <c>Hover</c>,
/// signature/completion documentation, and the doc-site generator. Sections
/// emit only when populated so the resulting blocks stay tight.
/// </summary>
public static class DocMarkdown
{
    public static string Render(DocComment doc, bool includeRemarks = true)
    {
        if (doc == null || doc.IsEmpty) return string.Empty;

        var sb = new StringBuilder();

        if (doc.Deprecated)
        {
            sb.Append("**⚠ Deprecated**");
            if (!string.IsNullOrEmpty(doc.DeprecatedReason))
                sb.Append(" — ").Append(doc.DeprecatedReason);
            sb.AppendLine();
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(doc.Summary))
        {
            sb.AppendLine(doc.Summary);
        }

        if (includeRemarks && !string.IsNullOrEmpty(doc.Remarks))
        {
            sb.AppendLine();
            sb.AppendLine(doc.Remarks);
        }

        if (doc.Params.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Parameters:**");
            foreach (var p in doc.Params)
            {
                sb.Append("- `").Append(p.Name);
                if (p.Optional) sb.Append('?');
                sb.Append('`');
                if (!string.IsNullOrEmpty(p.TypeText))
                    sb.Append(" *(").Append(p.TypeText).Append(")*");
                if (!string.IsNullOrEmpty(p.Description))
                    sb.Append(" — ").Append(p.Description);
                sb.AppendLine();
            }
        }

        if (doc.Returns.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Returns:**");
            foreach (var r in doc.Returns)
            {
                sb.Append("- ");
                if (!string.IsNullOrEmpty(r.Name)) sb.Append('`').Append(r.Name).Append("` ");
                if (!string.IsNullOrEmpty(r.TypeText)) sb.Append('*').Append('(').Append(r.TypeText).Append(")* ");
                if (!string.IsNullOrEmpty(r.Description)) sb.Append(r.Description);
                sb.AppendLine();
            }
        }

        if (doc.Generics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Type parameters:**");
            foreach (var g in doc.Generics)
            {
                sb.Append("- `").Append(g.Name).Append('`');
                if (!string.IsNullOrEmpty(g.Bound)) sb.Append(" extends `").Append(g.Bound).Append('`');
                sb.AppendLine();
            }
        }

        if (doc.Overloads.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Overloads:**");
            foreach (var o in doc.Overloads)
                sb.Append("- `").Append(o.Raw).AppendLine("`");
        }

        if (doc.Examples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Example:**");
            foreach (var ex in doc.Examples)
            {
                sb.AppendLine("```nebra");
                sb.AppendLine(ex);
                sb.AppendLine("```");
            }
        }

        if (!string.IsNullOrEmpty(doc.Since))
        {
            sb.AppendLine();
            sb.Append("*Since: ").Append(doc.Since).AppendLine("*");
        }

        if (doc.See.Count > 0)
        {
            sb.AppendLine();
            sb.Append("**See also:** ");
            sb.AppendLine(string.Join(", ", doc.See.Select(s => $"`{s}`")));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns just the description for a single parameter, used when
    /// populating <c>SignatureHelp</c> per-parameter docs.
    /// </summary>
    public static string? ParamDescription(DocComment? doc, string paramName)
    {
        if (doc == null) return null;
        var p = doc.Params.FirstOrDefault(p => string.Equals(p.Name, paramName, StringComparison.Ordinal));
        if (p == null) return null;
        return string.IsNullOrEmpty(p.Description) ? null : p.Description;
    }
}
