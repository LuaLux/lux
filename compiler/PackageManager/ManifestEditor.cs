using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Nebra.PackageManager;

/// <summary>
/// In-place editor for <c>nebra.toml</c> that preserves user formatting, comments and trivia
/// by mutating a <see cref="DocumentSyntax"/> tree instead of re-serializing via reflection.
/// </summary>
internal static class ManifestEditor
{
    public static string GroupTableName(DependencyGroup group) => group switch
    {
        DependencyGroup.Runtime => "dependencies",
        DependencyGroup.Dev => "dev_dependencies",
        DependencyGroup.Peer => "peer_dependencies",
        _ => "dependencies",
    };

    /// <summary>
    /// Adds or updates <paramref name="name"/> in the target dependency table. Creates the
    /// table if it does not exist. Preserves all surrounding trivia. Writes the file back.
    /// </summary>
    public static bool AddOrUpdate(string tomlPath, DependencyGroup group, string name, string specString)
    {
        var doc = Load(tomlPath);
        if (doc is null) return false;

        var tableName = GroupTableName(group);
        var table = FindTable(doc, tableName) ?? CreateTable(doc, tableName);

        var existing = FindKeyValue(table, name);
        var newValue = new StringValueSyntax(specString);
        if (existing is not null)
        {
            existing.Value = newValue;
        }
        else
        {
            var kv = new KeyValueSyntax(new KeySyntax(name), newValue)
            {
                EndOfLineToken = SyntaxFactory.NewLine(),
            };
            table.Items.Add(kv);
        }

        Save(tomlPath, doc);
        return true;
    }

    /// <summary>
    /// Removes <paramref name="name"/> from all dependency tables. Returns true if anything
    /// was removed. Preserves surrounding trivia.
    /// </summary>
    public static bool Remove(string tomlPath, string name)
    {
        var doc = Load(tomlPath);
        if (doc is null) return false;

        var removed = false;
        foreach (var group in new[] { DependencyGroup.Runtime, DependencyGroup.Dev, DependencyGroup.Peer })
        {
            var table = FindTable(doc, GroupTableName(group));
            if (table is null) continue;
            for (var i = table.Items.ChildrenCount - 1; i >= 0; i--)
            {
                if (table.Items.GetChild(i) is KeyValueSyntax kv && KeyMatches(kv, name))
                {
                    table.Items.RemoveChildAt(i);
                    removed = true;
                }
            }
        }

        if (removed) Save(tomlPath, doc);
        return removed;
    }

    private static DocumentSyntax? Load(string tomlPath)
    {
        if (!File.Exists(tomlPath)) return null;
        var text = File.ReadAllText(tomlPath);
        var doc = SyntaxParser.Parse(text, Path.GetFileName(tomlPath), validate: false);
        return doc.HasErrors ? null : doc;
    }

    private static void Save(string tomlPath, DocumentSyntax doc)
    {
        File.WriteAllText(tomlPath, doc.ToString());
    }

    private static TableSyntax? FindTable(DocumentSyntax doc, string name)
    {
        foreach (var t in doc.Tables)
        {
            if (t is TableSyntax ts && NameMatches(ts, name)) return ts;
        }
        return null;
    }

    private static TableSyntax CreateTable(DocumentSyntax doc, string name)
    {
        var table = new TableSyntax(name)
        {
            EndOfLineToken = SyntaxFactory.NewLine(),
        };
        table.AddLeadingTriviaNewLine();
        doc.Tables.Add(table);
        return table;
    }

    private static KeyValueSyntax? FindKeyValue(TableSyntax table, string name)
    {
        foreach (var item in table.Items)
        {
            if (KeyMatches(item, name)) return item;
        }
        return null;
    }

    private static bool KeyMatches(KeyValueSyntax kv, string name)
    {
        return KeyText(kv.Key) == name;
    }

    private static bool NameMatches(TableSyntax table, string name)
    {
        return KeyText(table.Name) == name;
    }

    private static string KeyText(KeySyntax? key)
    {
        if (key is null) return string.Empty;
        var raw = key.ToString() ?? string.Empty;
        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"') raw = raw[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        else if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'') raw = raw[1..^1];
        return raw;
    }
}
