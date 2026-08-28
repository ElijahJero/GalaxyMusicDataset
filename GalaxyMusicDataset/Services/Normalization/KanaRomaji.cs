using System.Text;
using System.Text.RegularExpressions;

namespace GalaxyMusicDataset.Services.Normalization;

/// <summary>
/// Hiragana/katakana to Hepburn-ish romaji. Kanji is left unchanged.
/// </summary>
public static partial class KanaRomaji
{
    [GeneratedRegex("n(?=[aiueoy])", RegexOptions.CultureInvariant)]
    private static partial Regex SyllabicNRegex();

    private static readonly Dictionary<string, string> Digraphs = new()
    {
        ["きゃ"] = "kya", ["きゅ"] = "kyu", ["きょ"] = "kyo",
        ["しゃ"] = "sha", ["しゅ"] = "shu", ["しょ"] = "sho",
        ["ちゃ"] = "cha", ["ちゅ"] = "chu", ["ちょ"] = "cho",
        ["にゃ"] = "nya", ["にゅ"] = "nyu", ["にょ"] = "nyo",
        ["ひゃ"] = "hya", ["ひゅ"] = "hyu", ["ひょ"] = "hyo",
        ["みゃ"] = "mya", ["みゅ"] = "myu", ["みょ"] = "myo",
        ["りゃ"] = "rya", ["りゅ"] = "ryu", ["りょ"] = "ryo",
        ["ぎゃ"] = "gya", ["ぎゅ"] = "gyu", ["ぎょ"] = "gyo",
        ["じゃ"] = "ja", ["じゅ"] = "ju", ["じょ"] = "jo",
        ["びゃ"] = "bya", ["びゅ"] = "byu", ["びょ"] = "byo",
        ["ぴゃ"] = "pya", ["ぴゅ"] = "pyu", ["ぴょ"] = "pyo",
        ["ヴァ"] = "va", ["ヴィ"] = "vi", ["ヴェ"] = "ve", ["ヴォ"] = "vo", ["ヴ"] = "vu",
    };

    private static readonly Dictionary<char, string> Singles = new()
    {
        ['あ'] = "a", ['い'] = "i", ['う'] = "u", ['え'] = "e", ['お'] = "o",
        ['か'] = "ka", ['き'] = "ki", ['く'] = "ku", ['け'] = "ke", ['こ'] = "ko",
        ['さ'] = "sa", ['し'] = "shi", ['す'] = "su", ['せ'] = "se", ['そ'] = "so",
        ['た'] = "ta", ['ち'] = "chi", ['つ'] = "tsu", ['て'] = "te", ['と'] = "to",
        ['な'] = "na", ['に'] = "ni", ['ぬ'] = "nu", ['ね'] = "ne", ['の'] = "no",
        ['は'] = "ha", ['ひ'] = "hi", ['ふ'] = "fu", ['へ'] = "he", ['ほ'] = "ho",
        ['ま'] = "ma", ['み'] = "mi", ['む'] = "mu", ['め'] = "me", ['も'] = "mo",
        ['や'] = "ya", ['ゆ'] = "yu", ['よ'] = "yo",
        ['ら'] = "ra", ['り'] = "ri", ['る'] = "ru", ['れ'] = "re", ['ろ'] = "ro",
        ['わ'] = "wa", ['を'] = "o", ['ん'] = "n",
        ['が'] = "ga", ['ぎ'] = "gi", ['ぐ'] = "gu", ['げ'] = "ge", ['ご'] = "go",
        ['ざ'] = "za", ['じ'] = "ji", ['ず'] = "zu", ['ぜ'] = "ze", ['ぞ'] = "zo",
        ['だ'] = "da", ['ぢ'] = "ji", ['づ'] = "zu", ['で'] = "de", ['ど'] = "do",
        ['ば'] = "ba", ['び'] = "bi", ['ぶ'] = "bu", ['べ'] = "be", ['ぼ'] = "bo",
        ['ぱ'] = "pa", ['ぴ'] = "pi", ['ぷ'] = "pu", ['ぺ'] = "pe", ['ぽ'] = "po",
        ['ぁ'] = "a", ['ぃ'] = "i", ['ぅ'] = "u", ['ぇ'] = "e", ['ぉ'] = "o",
        ['ゃ'] = "ya", ['ゅ'] = "yu", ['ょ'] = "yo", ['っ'] = "",
        ['ー'] = "",
    };

    public static string Convert(string input)
    {
        var hiragana = ToHiragana(input);
        var builder = new StringBuilder(hiragana.Length * 2);
        for (var i = 0; i < hiragana.Length; i++)
        {
            if (i + 1 < hiragana.Length)
            {
                var pair = hiragana[i..(i + 2)];
                if (Digraphs.TryGetValue(pair, out var di))
                {
                    builder.Append(di);
                    i++;
                    continue;
                }
            }

            var c = hiragana[i];
            if (c == 'っ' && i + 1 < hiragana.Length)
            {
                var next = Convert(hiragana[(i + 1)..]);
                if (next.Length > 0)
                {
                    var doubled = next[0] == 'c' ? 't' : next[0];
                    builder.Append(doubled);
                }

                continue;
            }

            if (Singles.TryGetValue(c, out var romaji))
            {
                builder.Append(romaji);
            }
            else
            {
                builder.Append(c);
            }
        }

        var result = builder.ToString();
        return SyllabicNRegex().Replace(result, "n'");
    }

    private static string ToHiragana(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c is >= '\u30A1' and <= '\u30F6')
            {
                builder.Append((char)(c - 0x60));
            }
            else if (c is >= '\uFF66' and <= '\uFF9D')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
