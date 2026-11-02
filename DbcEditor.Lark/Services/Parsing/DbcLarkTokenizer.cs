using System.IO;
using EtAlii.Lark;

namespace DbcEditor.Services.Parsing;

/// <summary>
/// Lark front-end: lexes the DBC with a Lark grammar, then the shared token model parser builds the database.
/// </summary>
public static class DbcLarkTokenizer
{
    private const string Grammar = """
        start: (HEX | FLOAT | INT | STRING | IDENT | COLON | SEMI | COMMA | PIPE | AT | PLUS | MINUS | LPAREN | RPAREN | LBRACK | RBRACK)*
        HEX.3: /0[xX][0-9a-fA-F]+/
        FLOAT.2: /[0-9]+\.[0-9]*([eE][+-]?[0-9]+)?/ | /\.[0-9]+([eE][+-]?[0-9]+)?/ | /[0-9]+[eE][+-]?[0-9]+/
        INT: /[0-9]+/
        STRING: /"([^"\\]|\\.|\"\")*"/
        IDENT: /[A-Za-z_][A-Za-z0-9_]*/
        COLON: ":"
        SEMI: ";"
        COMMA: ","
        PIPE: "|"
        AT: "@"
        PLUS: "+"
        MINUS: "-"
        LPAREN: "("
        RPAREN: ")"
        LBRACK: "["
        RBRACK: "]"
        %ignore /[ \t\r\n\f]+/
        %ignore /\/\/[^\n]*/
        """;

    private static readonly Lark Lexer = Lark.FromGrammar(Grammar).LexerOnly().Build();

    public static IReadOnlyList<DbcToken> Tokenize(string input)
    {
        try
        {
            return FromLark(input);
        }
        catch (UnexpectedCharactersException)
        {
            // Odd DBC exports: same fallback the Superpower front-end uses.
            return DbcHandLexer.Tokenize(input);
        }
    }

    private static List<DbcToken> FromLark(string input)
    {
        var list = new List<DbcToken>();
        foreach (var t in Lexer.Lex(new StringReader(input)))
        {
            var text = t.Value;
            var line = t.Line ?? 0;
            var col = t.Column ?? 0;
            switch (t.Type)
            {
                case "IDENT":
                    list.Add(new DbcToken(DbcTokKind.Ident, text, line, col));
                    break;
                case "HEX":
                    list.Add(new DbcToken(DbcTokKind.Int, text, line, col));
                    break;
                case "INT":
                case "FLOAT":
                    list.Add(new DbcToken(
                        text.Contains('.') || text.Contains('e') || text.Contains('E')
                            ? DbcTokKind.Float : DbcTokKind.Int,
                        text, line, col));
                    break;
                case "STRING":
                    list.Add(new DbcToken(DbcTokKind.String, Unquote(text), line, col));
                    break;
                case "COLON": list.Add(new DbcToken(DbcTokKind.Colon, ":", line, col)); break;
                case "SEMI": list.Add(new DbcToken(DbcTokKind.Semi, ";", line, col)); break;
                case "COMMA": list.Add(new DbcToken(DbcTokKind.Comma, ",", line, col)); break;
                case "PIPE": list.Add(new DbcToken(DbcTokKind.Pipe, "|", line, col)); break;
                case "AT": list.Add(new DbcToken(DbcTokKind.At, "@", line, col)); break;
                case "PLUS": list.Add(new DbcToken(DbcTokKind.Plus, "+", line, col)); break;
                case "MINUS": list.Add(new DbcToken(DbcTokKind.Minus, "-", line, col)); break;
                case "LPAREN": list.Add(new DbcToken(DbcTokKind.LParen, "(", line, col)); break;
                case "RPAREN": list.Add(new DbcToken(DbcTokKind.RParen, ")", line, col)); break;
                case "LBRACK": list.Add(new DbcToken(DbcTokKind.LBracket, "[", line, col)); break;
                case "RBRACK": list.Add(new DbcToken(DbcTokKind.RBracket, "]", line, col)); break;
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
