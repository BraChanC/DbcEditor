namespace DbcEditor.Services.Parsing;

/// <summary>Shared DBC lexer used after Pidgin/Sprache header checks, and as Superpower fallback map target.</summary>
public static class DbcHandLexer
{
    public static IReadOnlyList<DbcToken> Tokenize(string input)
    {
        var list = new List<DbcToken>();
        int i = 0, line = 1, col = 1;

        void Adv(int n = 1)
        {
            for (var k = 0; k < n && i < input.Length; k++)
            {
                if (input[i] == '\n') { line++; col = 1; }
                else col++;
                i++;
            }
        }

        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c)) { Adv(); continue; }

            // // comment
            if (c == '/' && i + 1 < input.Length && input[i + 1] == '/')
            {
                while (i < input.Length && input[i] != '\n') Adv();
                continue;
            }

            var sl = line;
            var sc = col;

            switch (c)
            {
                case ':': list.Add(new(DbcTokKind.Colon, ":", sl, sc)); Adv(); continue;
                case ';': list.Add(new(DbcTokKind.Semi, ";", sl, sc)); Adv(); continue;
                case ',': list.Add(new(DbcTokKind.Comma, ",", sl, sc)); Adv(); continue;
                case '|': list.Add(new(DbcTokKind.Pipe, "|", sl, sc)); Adv(); continue;
                case '@': list.Add(new(DbcTokKind.At, "@", sl, sc)); Adv(); continue;
                case '+': list.Add(new(DbcTokKind.Plus, "+", sl, sc)); Adv(); continue;
                case '-': list.Add(new(DbcTokKind.Minus, "-", sl, sc)); Adv(); continue;
                case '(': list.Add(new(DbcTokKind.LParen, "(", sl, sc)); Adv(); continue;
                case ')': list.Add(new(DbcTokKind.RParen, ")", sl, sc)); Adv(); continue;
                case '[': list.Add(new(DbcTokKind.LBracket, "[", sl, sc)); Adv(); continue;
                case ']': list.Add(new(DbcTokKind.RBracket, "]", sl, sc)); Adv(); continue;
            }

            if (c == '"')
            {
                Adv();
                var sb = new System.Text.StringBuilder();
                while (i < input.Length)
                {
                    if (input[i] == '"')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '"')
                        {
                            sb.Append('"');
                            Adv(2);
                            continue;
                        }
                        Adv();
                        break;
                    }
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        sb.Append(input[i + 1]);
                        Adv(2);
                        continue;
                    }
                    sb.Append(input[i]);
                    Adv();
                }
                list.Add(new(DbcTokKind.String, sb.ToString(), sl, sc));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < input.Length && char.IsDigit(input[i + 1])))
            {
                var start = i;
                // hex
                if (c == '0' && i + 1 < input.Length && (input[i + 1] == 'x' || input[i + 1] == 'X'))
                {
                    Adv(2);
                    while (i < input.Length && IsHex(input[i])) Adv();
                    list.Add(new(DbcTokKind.Int, input[start..i], sl, sc));
                    continue;
                }

                while (i < input.Length && char.IsDigit(input[i])) Adv();
                var isFloat = false;
                if (i < input.Length && input[i] == '.')
                {
                    isFloat = true;
                    Adv();
                    while (i < input.Length && char.IsDigit(input[i])) Adv();
                }
                if (i < input.Length && (input[i] == 'e' || input[i] == 'E'))
                {
                    isFloat = true;
                    Adv();
                    if (i < input.Length && (input[i] == '+' || input[i] == '-')) Adv();
                    while (i < input.Length && char.IsDigit(input[i])) Adv();
                }
                list.Add(new(isFloat ? DbcTokKind.Float : DbcTokKind.Int, input[start..i], sl, sc));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                Adv();
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                    Adv();
                list.Add(new(DbcTokKind.Ident, input[start..i], sl, sc));
                continue;
            }

            // Skip unknown char
            Adv();
        }

        list.Add(new(DbcTokKind.Eof, "", line, col));
        return list;
    }

    private static bool IsHex(char c) =>
        char.IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
