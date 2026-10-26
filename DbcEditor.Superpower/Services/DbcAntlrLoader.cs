using System.IO;
using DbcEditor.Models;
using DbcEditor.Services.Parsing;

namespace DbcEditor.Services;

/// <summary>
/// Same public API as the ANTLR loader — implementation uses <b>Superpower</b> + shared token model parser.
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
            var tokens = DbcSuperpowerTokenizer.Tokenize(text);
            var db = DbcTokenModelParser.Parse(tokens, displayName!);
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
            throw new DbcParseException($"Superpower DBC load failed: {ex.Message}", ex);
        }
    }
}
