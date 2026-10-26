using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace DbcEditor.Services.Parsing;

/// <summary>
/// Superpower front-end: full tokenize via TokenizerBuilder → shared DbcToken model.
/// </summary>
public static class DbcSuperpowerTokenizer
{
    private enum SpKind
    {
        Ident, Int, Float, String,
        Colon, Semi, Comma, Pipe, At, Plus, Minus,
        LParen, RParen, LBracket, RBracket
    }

    private static readonly Tokenizer<SpKind> Tokenizer = new TokenizerBuilder<SpKind>()
        .Ignore(Span.WhiteSpace)
        .Ignore(Comment.CPlusPlusStyle)
        .Match(Character.EqualTo(':'), SpKind.Colon)
        .Match(Character.EqualTo(';'), SpKind.Semi)
        .Match(Character.EqualTo(','), SpKind.Comma)
        .Match(Character.EqualTo('|'), SpKind.Pipe)
        .Match(Character.EqualTo('@'), SpKind.At)
        .Match(Character.EqualTo('+'), SpKind.Plus)
        .Match(Character.EqualTo('-'), SpKind.Minus)
        .Match(Character.EqualTo('('), SpKind.LParen)
        .Match(Character.EqualTo(')'), SpKind.RParen)
        .Match(Character.EqualTo('['), SpKind.LBracket)
        .Match(Character.EqualTo(']'), SpKind.RBracket)
        .Match(Span.Regex("0[xX][0-9a-fA-F]+"), SpKind.Int)
        .Match(Span.Regex("[0-9]+(\\.[0-9]*)?([eE][+-]?[0-9]+)?"), SpKind.Float)
        .Match(Span.Regex("\\.[0-9]+([eE][+-]?[0-9]+)?"), SpKind.Float)
        .Match(Numerics.Integer, SpKind.Int)
        .Match(Span.Regex("\"([^\"\\\\]|\\\\.|\"\")*\""), SpKind.String)
        .Match(Span.Regex("[A-Za-z_][A-Za-z0-9_]*"), SpKind.Ident)
        .Build();

    public static IReadOnlyList<DbcToken> Tokenize(string input)
    {
        var result = Tokenizer.TryTokenize(input);
        if (!result.HasValue)
        {
            // Fallback: shared hand lexer (robust for odd DBC exports)
            return DbcHandLexer.Tokenize(input);
        }

        var list = new List<DbcToken>();
        foreach (var t in result.Value)
        {
            var text = t.ToStringValue();
            var line = t.Position.Line;
            var col = t.Position.Column;
            switch (t.Kind)
            {
                case SpKind.Ident:
                    list.Add(new DbcToken(DbcTokKind.Ident, text, line, col));
                    break;
                case SpKind.Int:
                    // Integer regex may have matched floats first — normalize
                    list.Add(new DbcToken(
                        text.Contains('.') || text.Contains('e') || text.Contains('E')
                            ? DbcTokKind.Float : DbcTokKind.Int,
                        text, line, col));
                    break;
                case SpKind.Float:
                    list.Add(new DbcToken(
                        text.Contains('.') || text.Contains('e') || text.Contains('E')
                            ? DbcTokKind.Float : DbcTokKind.Int,
                        text, line, col));
                    break;
                case SpKind.String:
                    var inner = Unquote(text);
                    list.Add(new DbcToken(DbcTokKind.String, inner, line, col));
                    break;
                case SpKind.Colon: list.Add(new DbcToken(DbcTokKind.Colon, ":", line, col)); break;
                case SpKind.Semi: list.Add(new DbcToken(DbcTokKind.Semi, ";", line, col)); break;
                case SpKind.Comma: list.Add(new DbcToken(DbcTokKind.Comma, ",", line, col)); break;
                case SpKind.Pipe: list.Add(new DbcToken(DbcTokKind.Pipe, "|", line, col)); break;
                case SpKind.At: list.Add(new DbcToken(DbcTokKind.At, "@", line, col)); break;
                case SpKind.Plus: list.Add(new DbcToken(DbcTokKind.Plus, "+", line, col)); break;
                case SpKind.Minus: list.Add(new DbcToken(DbcTokKind.Minus, "-", line, col)); break;
                case SpKind.LParen: list.Add(new DbcToken(DbcTokKind.LParen, "(", line, col)); break;
                case SpKind.RParen: list.Add(new DbcToken(DbcTokKind.RParen, ")", line, col)); break;
                case SpKind.LBracket: list.Add(new DbcToken(DbcTokKind.LBracket, "[", line, col)); break;
                case SpKind.RBracket: list.Add(new DbcToken(DbcTokKind.RBracket, "]", line, col)); break;
            }
        }
        list.Add(new DbcToken(DbcTokKind.Eof, "", 0, 0));
        return list;
    }

    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1];
        return text.Replace("\"\"", "\"", StringComparison.Ordinal)
                   .Replace("\\\"", "\"", StringComparison.Ordinal);
    }
}
