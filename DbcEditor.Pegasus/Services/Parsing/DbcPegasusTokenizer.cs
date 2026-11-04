namespace DbcEditor.Services.Parsing;

/// <summary>
/// Pegasus PEG front-end. The generated parser lexes the file; the shared token model parser builds the database.
/// </summary>
public static class DbcPegasusTokenizer
{
    public static IReadOnlyList<DbcToken> Tokenize(string input)
    {
        try
        {
            var parsed = new DbcPegasusParser().Parse(input ?? "");
            var list = new List<DbcToken>(parsed.Count + 1);
            list.AddRange(parsed);
            list.Add(new DbcToken(DbcTokKind.Eof, "", 0, 0));
            return list;
        }
        catch (FormatException)
        {
            return DbcHandLexer.Tokenize(input ?? "");
        }
    }
}
