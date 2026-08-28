using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GalaxyMusicDataset.Services.Normalization;

public static partial class TextNormalizer
{
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\((?:instrumental|inst\.?|off vocal|tv size|short ver\.?|album ver\.?|remix)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionParenRegex();

    [GeneratedRegex(@"\b(?:feat\.?|ft\.?|featuring)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FeaturingRegex();

    [GeneratedRegex(@"[""'`´‘’“”「」『』【】\[\]()（）〈〉《》]", RegexOptions.CultureInvariant)]
    private static partial Regex QuotesRegex();

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Trim().Normalize(NormalizationForm.FormKC);
        text = ReplaceFullWidthAscii(text);
        text = VersionParenRegex().Replace(text, " ");
        text = QuotesRegex().Replace(text, " ");
        text = FeaturingRegex().Replace(text, " ");
        text = text.Replace("&", " and ", StringComparison.Ordinal);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.ToLowerInvariant();
    }

    public static bool ContainsCjk(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (IsCjk(c))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsKana(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (IsKana(c))
            {
                return true;
            }
        }

        return false;
    }

    public static string? RomanizeIfKana(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ContainsKana(value))
        {
            return null;
        }

        var romanized = KanaRomaji.Convert(value);
        var normalized = Normalize(romanized);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    public static bool IsCjk(char c)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        if (category is UnicodeCategory.OtherLetter)
        {
            return c is >= '\u3040' and <= '\u30FF'
                or >= '\u3400' and <= '\u9FFF'
                or >= '\uF900' and <= '\uFAFF'
                or >= '\uFF66' and <= '\uFF9D';
        }

        return c is >= '\u3040' and <= '\u30FF' or >= '\u3400' and <= '\u9FFF';
    }

    public static bool IsKana(char c) =>
        c is >= '\u3040' and <= '\u30FF' or >= '\uFF66' and <= '\uFF9D';

    private static string ReplaceFullWidthAscii(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '\uFF01' and <= '\uFF5E')
            {
                builder.Append((char)(c - 0xFEE0));
            }
            else if (c == '\u3000')
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
