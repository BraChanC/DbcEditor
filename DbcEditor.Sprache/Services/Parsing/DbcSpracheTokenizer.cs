using Sprache;

namespace DbcEditor.Services.Parsing;

/// <summary>
/// Sprache front-end: validates optional VERSION header with Sprache, then shared hand-lexer.
/// </summary>
public static class DbcSpracheTokenizer
{
    private static readonly Parser<string> OptionalVersion =
        from ws in Parse.WhiteSpace.Many().Text()
        from ver in (
            from kw in Parse.String("VERSION").Text()
            from ws2 in Parse.WhiteSpace.Many()
            from str in Parse.Char('"').Then(_ => Parse.CharExcept('"').Many().Text()).Then(s => Parse.Char('"').Return(s))
            from ws3 in Parse.WhiteSpace.Many()
            from semi in Parse.Char(';').Optional()
            select kw
        ).Optional()
        select ver.GetOrElse("");

    public static IReadOnlyList<DbcToken> Tokenize(string input)
    {
        try
        {
            OptionalVersion.Parse(input);
        }
        catch (ParseException ex)
        {
            throw new DbcParseException("Sprache: failed scanning DBC preamble. " + ex.Message, ex);
        }
        return DbcHandLexer.Tokenize(input);
    }
}
