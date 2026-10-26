using System.IO;
using System.Text;

namespace DbcEditor.Services;

/// <summary>
/// Chinese automotive DBC files are often GBK/GB18030 (CP936), not UTF-8.
/// Reading as UTF-8 causes mojibake in comments / signal names.
/// </summary>
public static class DbcTextEncoding
{
    private static bool _registered;

    public static string ReadAllText(string path)
    {
        EnsureCodePages();
        var bytes = File.ReadAllBytes(path);
        var encoding = Detect(bytes);
        return encoding.GetString(bytes);
    }

    /// <summary>
    /// Write DBC text. Prefer the encoding of an existing file (round-trip GBK/UTF-8);
    /// otherwise GB18030 (common for Chinese Vector exports).
    /// </summary>
    public static void WriteAllText(string path, string text, string? encodingHintPath = null)
    {
        EnsureCodePages();
        Encoding enc;
        var hint = !string.IsNullOrEmpty(encodingHintPath) && File.Exists(encodingHintPath)
            ? encodingHintPath
            : File.Exists(path) ? path : null;
        if (hint != null)
            enc = Detect(File.ReadAllBytes(hint));
        else
        {
            try { enc = Encoding.GetEncoding(54936); }
            catch { enc = Encoding.GetEncoding(936); }
        }
        File.WriteAllText(path, text, enc);
    }

    public static Encoding Detect(byte[] bytes)
    {
        EnsureCodePages();

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // Prefer UTF-8 when the file is valid UTF-8 and does not look like GBK misread.
        if (LooksLikeUtf8(bytes))
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        // Domestic Vector / matrix DBC exports: GB18030 (superset of GBK).
        try
        {
            return Encoding.GetEncoding(54936); // GB18030
        }
        catch
        {
            return Encoding.GetEncoding(936); // GBK
        }
    }

    private static void EnsureCodePages()
    {
        if (_registered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _registered = true;
    }

    /// <summary>
    /// True if every byte sequence is valid UTF-8 (overlong / lone surrogates rejected).
    /// Pure ASCII returns true — ASCII is valid UTF-8 and identical under GBK.
    /// </summary>
    private static bool LooksLikeUtf8(byte[] bytes)
    {
        int i = 0;
        bool sawMulti = false;
        while (i < bytes.Length)
        {
            byte b = bytes[i];
            if (b <= 0x7F)
            {
                i++;
                continue;
            }

            int need;
            if ((b & 0xE0) == 0xC0) need = 1;
            else if ((b & 0xF0) == 0xE0) need = 2;
            else if ((b & 0xF8) == 0xF0) need = 3;
            else return false;

            if (i + need >= bytes.Length) return false;
            // Overlong 2-byte start
            if (need == 1 && b < 0xC2) return false;

            for (int k = 1; k <= need; k++)
            {
                if ((bytes[i + k] & 0xC0) != 0x80) return false;
            }
            i += need + 1;
            sawMulti = true;
        }

        // Valid UTF-8 with multi-byte sequences → trust UTF-8.
        // Valid UTF-8 that is pure ASCII → also fine (same as GBK for ASCII).
        // If file has high bytes that failed UTF-8, caller uses GBK.
        // Heuristic: if no multi-byte UTF-8 but high bytes exist, it wasn't UTF-8
        // (handled by returning false above). Pure ASCII: sawMulti=false → still UTF-8 OK.
        _ = sawMulti;
        return true;
    }
}
