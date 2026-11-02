using System.IO;
using DbcEditor.Models;
using DbcEditor.Services.Parsing;

namespace DbcEditor.Services;

/// <summary>
/// Lark LALR tree front-end. The grammar builds a syntax tree, a transformer turns that
/// tree into an ordered list of statements, and <see cref="DbcTreeModel"/> fills <see cref="DbcDatabase"/>.
/// The lexer-only Lark project is unchanged.
/// </summary>
public sealed class DbcAntlrLoader
{
    public DbcDatabase Load(string path)
    {
        var text = DbcTextEncoding.ReadAllText(path);
        return LoadText(text, path);
    }

    public DbcDatabase LoadText(string text, string path)
    {
        var displayName = string.IsNullOrEmpty(path)
            ? "Network"
            : Path.GetFileNameWithoutExtension(path);
        try
        {
            var db = DbcLarkTree.Parse(text, displayName!);
            db.FilePath = path ?? "";
            if (string.IsNullOrWhiteSpace(db.NetworkName))
                db.NetworkName = displayName!;
            return db;
        }
        catch (DbcParseException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DbcParseException($"Lark tree DBC load failed: {ex.Message}", ex);
        }
    }
}
