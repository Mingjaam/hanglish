using System.IO.Compression;
using System.Reflection;

namespace Hanglish.Core;

public enum Language { English, Korean, Mixed }

public sealed record Choice(string Text, Language Language, double Probability);

public sealed record Decision(string Raw, Choice Best, IReadOnlyList<Choice> Ranking)
{
    /// <summary>The best candidate in the other language, used by the flip key.</summary>
    public Choice Alternative => Ranking.FirstOrDefault(c => c.Text != Best.Text) ?? Best;
}

/// <summary>Chooses English, Hangul, or an English word with a Korean particle for one typed word.</summary>
public sealed class Decider
{
    // English stems that commonly take a Korean particle: "github" + "에서".
    static readonly string[] Particles =
    {
        "은", "는", "이", "가", "을", "를", "에", "에서", "의", "도", "로", "으로", "와", "과", "랑", "이랑",
        "하고", "에게", "한테", "까지", "부터", "만", "이다", "입니다", "이에요", "예요", "야", "이야", "처럼",
        "보다", "같은", "해", "해서", "하는", "한", "할", "했다", "했어", "했어요", "해요", "합니다", "하면",
        "하기", "된", "되는", "인", "이고", "에서는", "으로는", "에는", "이나", "나", "라고", "이라고", "께서",
        "이라서", "라서", "이니까", "니까", "이라", "라", "이랑은", "에게서", "으로서", "로서", "이지만", "지만",
        "이었어", "였어", "이었다", "였다", "이죠", "죠", "이네", "네", "이래", "래", "하자", "할게", "할까",
    };

    /// <summary>Scoring knobs, tuned on tests/corpus/*_dev.txt and checked on *_test.txt.</summary>
    public sealed record Tuning
    {
        public double MixedPrior { get; init; } = 0.02;
        /// <summary>Stems below this frequency (coupang, figma) still take particles, at a cost.</summary>
        public double MixedStemMin { get; init; } = 1e-7;
        public double UnknownStemPenalty { get; init; } = 0.05;
        /// <summary>Per syllable when the keys compose into whole syllables only (names: 오채호).</summary>
        public double SyllableBonus { get; init; } = 5;
        /// <summary>Prior for Korean when the previous words were all English / all Korean.</summary>
        public double PriorLow { get; init; } = 0.05;
        public double PriorHigh { get; init; } = 0.95;
        /// <summary>Each step back in the sentence counts this much less (1 = equal weights).</summary>
        public double ContextDecay { get; init; } = 0.6;
        public int ContextWords { get; init; } = 6;
        public double CommonWord { get; init; } = 1e-5;   // Zipf 4: everyday words in both readings
        public double LoosePenalty { get; init; } = 0.03;  // per jamo that does not join a syllable
        public double ShiftPenalty { get; init; } = 0.02;  // per Shift on a key where Dubeolsik ignores Shift
        /// <summary>Per letter when Shift is read as held over from the letter before (있다 typed as dlTEk).</summary>
        public double SlipPenalty { get; init; } = 0.2;
        /// <summary>Per lower-to-upper switch inside an English word (gkTEk is not English).</summary>
        public double CasePenalty { get; init; } = 0.05;
    }

    public Tuning Knobs { get; set; } = new();

    readonly KeyModel english = new(), korean = new();
    readonly Dictionary<string, string> particleKeys;

    public Decider(Stream? lexicon = null)
    {
        lexicon ??= Assembly.GetExecutingAssembly().GetManifestResourceStream("Hanglish.Core.lexicon.tsv.gz")
                    ?? throw new InvalidOperationException("Lexicon resource missing");
        using var reader = new StreamReader(new GZipStream(lexicon, CompressionMode.Decompress));
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            double frequency = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            if (parts[0] == "e") english.Add(parts[1], frequency);
            else if (Hangul.ToKeys(parts[1]) is { } keys) korean.Add(keys, frequency);
        }
        // Chat shorthand made only of jamo: ㅋㅋ, ㅎㅎ, ㅠㅠ, ㅇㅋ ...
        foreach (var jamo in new[] { "ㅋ", "ㅎ", "ㅠ", "ㅜ" })
            for (int n = 2; n <= 8; n++) korean.Add(Hangul.ToKeys(string.Concat(Enumerable.Repeat(jamo, n)))!, 2e-4 / n);
        foreach (var shorthand in new[] { "ㅇㅇ", "ㄴㄴ", "ㄱㄱ", "ㅇㅋ", "ㄷㄷ", "ㅊㅋ", "ㅈㅅ", "ㄱㅅ", "ㅅㄱ", "ㅂㅂ", "ㅎㅇ", "ㄹㅇ" })
            korean.Add(Hangul.ToKeys(shorthand)!, 5e-5);
        english.Train();
        korean.Train();
        particleKeys = Particles.ToDictionary(p => Hangul.ToKeys(p)!, p => p);
    }

    /// <param name="raw">Latin letters as typed.</param>
    /// <param name="before">Text already typed before this word in the same field.</param>
    public Decision Decide(string raw, string before = "")
    {
        double koreanPrior = KoreanPrior(before);
        var scores = new List<(string Text, Language Language, double Log)>
        {
            (raw, Language.English, Math.Log(1 - koreanPrior) + EnglishLog(raw)),
            (Hangul.Compose(raw), Language.Korean, Math.Log(koreanPrior) + KoreanLog(raw)),
        };
        // Shift held a key too long: any capital right after another capital may really be unshifted.
        foreach (var (unslipped, slips) in Unslips(raw))
            scores.Add((Hangul.Compose(unslipped), Language.Korean,
                Math.Log(koreanPrior) + KoreanLog(unslipped) + slips * Math.Log(Knobs.SlipPenalty)));
        for (int split = 2; split < raw.Length; split++)
        {
            var stem = raw[..split];
            bool known = english.Frequency(stem.ToLowerInvariant()) >= Knobs.MixedStemMin;
            if (!known && split < 3) continue;
            if (particleKeys.TryGetValue(Hangul.KoreanKeys(raw[split..]), out var particle))
                scores.Add((stem + particle, Language.Mixed,
                    Math.Log(Knobs.MixedPrior) + EnglishLog(stem) + Math.Log(1.0 / Particles.Length)
                    + (known ? 0 : Math.Log(Knobs.UnknownStemPenalty))));
        }
        double max = scores.Max(s => s.Log);
        double sum = scores.Sum(s => Math.Exp(s.Log - max));
        // Readings that produce the same text pool their probability; the likeliest names the language.
        var ranking = scores
            .GroupBy(s => s.Text)
            .Select(g => new Choice(g.Key, g.MaxBy(s => s.Log).Language, g.Sum(s => Math.Exp(s.Log - max)) / sum))
            .OrderByDescending(c => c.Probability).ToList();
        return new Decision(raw, ranking[0], ranking);
    }

    /// <summary>
    /// Prior for Korean from the words before this one, judged by the script each word ends in
    /// (so "github에서" counts as Korean for what follows), recent words weighing more.
    /// </summary>
    double KoreanPrior(string before)
    {
        var words = before.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.LastOrDefault(c => char.IsAsciiLetter(c) || c >= 0x3131))
            .Where(c => c != default).TakeLast(Knobs.ContextWords).Reverse().ToList();
        if (words.Count == 0) return 0.5;
        double weight = 1, total = 0, korean = 0;
        foreach (var last in words)
        {
            total += weight;
            if (last >= 0x3131) korean += weight;
            weight *= Knobs.ContextDecay;
        }
        return Knobs.PriorLow + (Knobs.PriorHigh - Knobs.PriorLow) * korean / total;
    }

    /// <summary>Both readings are everyday words (to/새, do/애), so only the following words can tell.</summary>
    public bool BothCommon(string raw) =>
        english.Frequency(raw.ToLowerInvariant()) >= Knobs.CommonWord
        && korean.Frequency(Hangul.KoreanKeys(raw)) >= Knobs.CommonWord;

    /// <summary>The Hangul reading of these keys is a word Korean speakers actually write (ㅋㅋ, 예).</summary>
    public bool KnownKorean(string raw) => korean.Contains(Hangul.KoreanKeys(raw));

    double EnglishLog(string raw)
    {
        // "Hello" and "NASA" are fine; a capital after a small letter (heLLo) is not how English is typed.
        int switches = 0;
        for (int i = 1; i < raw.Length; i++)
            if (char.IsAsciiLetterLower(raw[i - 1]) && char.IsAsciiLetterUpper(raw[i])) switches++;
        return english.WordLog(raw.ToLowerInvariant()) + switches * Math.Log(Knobs.CasePenalty);
    }

    /// <summary>Every way of lowering capitals that directly follow another capital (at most 4 such keys).</summary>
    static IEnumerable<(string Keys, int Slips)> Unslips(string raw)
    {
        var followers = Enumerable.Range(1, Math.Max(0, raw.Length - 1))
            .Where(i => char.IsAsciiLetterUpper(raw[i - 1]) && char.IsAsciiLetterUpper(raw[i])).Take(4).ToList();
        for (int mask = 1; mask < 1 << followers.Count; mask++)
        {
            var chars = raw.ToCharArray();
            int slips = 0;
            for (int b = 0; b < followers.Count; b++)
                if ((mask >> b & 1) != 0) { chars[followers[b]] = char.ToLowerInvariant(chars[followers[b]]); slips++; }
            yield return (new string(chars), slips);
        }
    }

    double KoreanLog(string raw)
    {
        var keys = Hangul.KoreanKeys(raw);
        int ignoredShifts = raw.Zip(keys).Count(p => p.First != p.Second);
        var composed = Hangul.Compose(raw);
        int loose = korean.Contains(keys) ? 0 : Hangul.LooseJamo(composed);
        double whole = loose == 0 && composed.All(c => c is >= '가' and <= '힣')
            ? composed.Length * Math.Log(Knobs.SyllableBonus) : 0;
        return whole + korean.WordLog(keys) + ignoredShifts * Math.Log(Knobs.ShiftPenalty) + loose * Math.Log(Knobs.LoosePenalty);
    }
}
