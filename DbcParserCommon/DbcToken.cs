namespace DbcEditor.Services.Parsing;

public enum DbcTokKind
{
    Ident,
    Int,
    Float,
    String,
    Colon,
    Semi,
    Comma,
    Pipe,
    At,
    Plus,
    Minus,
    LParen,
    RParen,
    LBracket,
    RBracket,
    Eof
}

public readonly record struct DbcToken(DbcTokKind Kind, string Text, int Line, int Column)
{
    public override string ToString() => $"{Kind}('{Text}')@{Line}:{Column}";
}

public sealed class DbcParseException : Exception
{
    public DbcParseException(string message) : base(message) { }
    public DbcParseException(string message, Exception inner) : base(message, inner) { }
}
