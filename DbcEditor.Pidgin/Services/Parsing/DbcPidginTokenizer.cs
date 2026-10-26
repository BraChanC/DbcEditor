using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;

namespace DbcEditor.Services.Parsing;

/// <summary>
/// Pidgin front-end: validates optional VERSION header with Pidgin, then shared hand-lexer.
/// </summary>
public static class DbcPidginTokenizer
{
    private static readonly Parser<char, Unit> OptionalVersion =
        SkipWhitespaces
            .Then(
                Try(String("VERSION")
                    .Then(SkipWhitespaces)
                    .Then(Char('"').Then(Any.Until(Char('"'))).Then(SkipWhitespaces).Then(Try(Char(';')).IgnoreResult().Or(Return(Unit.Value))))
                    .IgnoreResult())
                .Or(Return(Unit.Value)));

    public static IReadOnlyList<DbcToken> Tokenize(string input)
    {
        var result = OptionalVersion.Parse(input);
        if (!result.Success)
        {
            throw new DbcParseException(
                "Pidgin: failed scanning DBC preamble. " + (result.Error?.ToString() ?? ""));
        }
        return DbcHandLexer.Tokenize(input);
    }
}
