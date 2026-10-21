using System.Text;

namespace Hanglish.Core;

/// <summary>Dubeolsik (두벌식) key ↔ Hangul conversion.</summary>
public static class Hangul
{
    const string Choseong = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    const string Jungseong = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    // Index 0 is "no final consonant".
    const string Jongseong = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

    static readonly Dictionary<char, char> KeyToJamo = new()
    {
        ['q'] = 'ㅂ', ['w'] = 'ㅈ', ['e'] = 'ㄷ', ['r'] = 'ㄱ', ['t'] = 'ㅅ', ['y'] = 'ㅛ', ['u'] = 'ㅕ',
        ['i'] = 'ㅑ', ['o'] = 'ㅐ', ['p'] = 'ㅔ', ['a'] = 'ㅁ', ['s'] = 'ㄴ', ['d'] = 'ㅇ', ['f'] = 'ㄹ',
        ['g'] = 'ㅎ', ['h'] = 'ㅗ', ['j'] = 'ㅓ', ['k'] = 'ㅏ', ['l'] = 'ㅣ', ['z'] = 'ㅋ', ['x'] = 'ㅌ',
        ['c'] = 'ㅊ', ['v'] = 'ㅍ', ['b'] = 'ㅠ', ['n'] = 'ㅜ', ['m'] = 'ㅡ',
        ['Q'] = 'ㅃ', ['W'] = 'ㅉ', ['E'] = 'ㄸ', ['R'] = 'ㄲ', ['T'] = 'ㅆ', ['O'] = 'ㅒ', ['P'] = 'ㅖ',
    };

    static readonly Dictionary<(char, char), char> VowelPairs = new()
    {
        [('ㅗ', 'ㅏ')] = 'ㅘ', [('ㅗ', 'ㅐ')] = 'ㅙ', [('ㅗ', 'ㅣ')] = 'ㅚ',
        [('ㅜ', 'ㅓ')] = 'ㅝ', [('ㅜ', 'ㅔ')] = 'ㅞ', [('ㅜ', 'ㅣ')] = 'ㅟ', [('ㅡ', 'ㅣ')] = 'ㅢ',
    };

    static readonly Dictionary<(char, char), char> FinalPairs = new()
    {
        [('ㄱ', 'ㅅ')] = 'ㄳ', [('ㄴ', 'ㅈ')] = 'ㄵ', [('ㄴ', 'ㅎ')] = 'ㄶ', [('ㄹ', 'ㄱ')] = 'ㄺ',
        [('ㄹ', 'ㅁ')] = 'ㄻ', [('ㄹ', 'ㅂ')] = 'ㄼ', [('ㄹ', 'ㅅ')] = 'ㄽ', [('ㄹ', 'ㅌ')] = 'ㄾ',
        [('ㄹ', 'ㅍ')] = 'ㄿ', [('ㄹ', 'ㅎ')] = 'ㅀ', [('ㅂ', 'ㅅ')] = 'ㅄ',
    };

    static readonly Dictionary<char, (char, char)> FinalSplit =
        FinalPairs.ToDictionary(p => p.Value, p => p.Key);

    // Declared after the pair tables: static fields initialize in textual order.
    static readonly Dictionary<char, string> JamoToKeys = BuildReverse();

    static Dictionary<char, string> BuildReverse()
    {
        var map = new Dictionary<char, string>();
        foreach (var (key, jamo) in KeyToJamo) map[jamo] = key.ToString();
        foreach (var ((a, b), v) in VowelPairs.Concat(FinalPairs))
            map[v] = map[a] + map[b];
        return map;
    }

    public static bool IsVowel(char jamo) => Jungseong.Contains(jamo);

    /// <summary>The jamo a key produces, honoring Shift only where Dubeolsik uses it.</summary>
    public static char? Jamo(char key)
    {
        if (KeyToJamo.TryGetValue(key, out var j)) return j;
        if (char.IsAsciiLetterUpper(key) && KeyToJamo.TryGetValue(char.ToLowerInvariant(key), out j)) return j;
        return null;
    }

    /// <summary>Normalizes raw keys to what matters for Hangul: Shift kept only on QWERTOP.</summary>
    public static string KoreanKeys(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
            sb.Append(char.IsAsciiLetterUpper(c) && !KeyToJamo.ContainsKey(c) ? char.ToLowerInvariant(c) : c);
        return sb.ToString();
    }

    /// <summary>Composes raw Latin keystrokes into Hangul. Non-letter characters pass through.</summary>
    public static string Compose(string raw)
    {
        var output = new StringBuilder();
        char cho = '\0', jung = '\0', jong = '\0';

        void Flush()
        {
            if (cho != '\0' && jung != '\0') output.Append(Syllable(cho, jung, jong));
            else if (cho != '\0') output.Append(cho);
            else if (jung != '\0') output.Append(jung);
            cho = jung = jong = '\0';
        }

        foreach (var key in raw)
        {
            var jamo = Jamo(key);
            if (jamo is null) { Flush(); output.Append(key); continue; }
            var j = jamo.Value;
            if (!IsVowel(j))
            {
                if (jung == '\0' || cho == '\0') { Flush(); cho = j; }
                else if (jong == '\0')
                {
                    if (Jongseong.Contains(j)) jong = j;
                    else { Flush(); cho = j; }
                }
                else if (FinalPairs.TryGetValue((jong, j), out var pair)) jong = pair;
                else { Flush(); cho = j; }
            }
            else
            {
                if (jong != '\0')
                {
                    char next;
                    if (FinalSplit.TryGetValue(jong, out var split)) { jong = split.Item1; next = split.Item2; }
                    else { next = jong; jong = '\0'; }
                    Flush();
                    cho = next;
                    jung = j;
                }
                else if (jung != '\0')
                {
                    if (VowelPairs.TryGetValue((jung, j), out var pair)) jung = pair;
                    else { Flush(); jung = j; }
                }
                else jung = j;
            }
        }
        Flush();
        return output.ToString();
    }

    static char Syllable(char cho, char jung, char jong) =>
        (char)(0xAC00 + (Choseong.IndexOf(cho) * 21 + Jungseong.IndexOf(jung)) * 28
               + (jong == '\0' ? 0 : Jongseong.IndexOf(jong)));

    /// <summary>Converts Hangul text back to the Dubeolsik keys that type it.</summary>
    public static string? ToKeys(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3)
            {
                int index = c - 0xAC00;
                sb.Append(JamoToKeys[Choseong[index / 588]]);
                sb.Append(JamoToKeys[Jungseong[index % 588 / 28]]);
                if (index % 28 != 0) sb.Append(JamoToKeys[Jongseong[index % 28]]);
            }
            else if (JamoToKeys.TryGetValue(c, out var keys)) sb.Append(keys);
            else return null;
        }
        return sb.ToString();
    }

    /// <summary>Counts compatibility jamo left unattached to a syllable.</summary>
    public static int LooseJamo(string text) => text.Count(c => c >= 0x3131 && c <= 0x3163);
}
