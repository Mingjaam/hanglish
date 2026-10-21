using Hanglish.Core;

/// <summary>
/// Types each expected sentence through the engine as a Dubeolsik typist would and compares
/// the final text field with the sentence, so back-corrections count too.
/// </summary>
static class CorpusEval
{
    /// <summary>When set, Shift is held one key too long after this share of intended shifted keys.</summary>
    public static double SlipRate;

    public static (int Words, int WordHits, int Sentences, int SentenceHits) Run(Decider decider, string path, bool verbose)
    {
        var random = new Random(5);
        int words = 0, wordHits = 0, sentences = 0, sentenceHits = 0;
        foreach (var expected in File.ReadLines(path).Where(l => l.Trim().Length > 0))
        {
            var field = Type(decider, expected, random);
            var got = field.Split(' ');
            var want = expected.Split(' ');
            sentences++;
            if (field == expected) sentenceHits++;
            else if (verbose) Console.WriteLine($"  {expected}  =>  {field}");
            words += want.Length;
            wordHits += want.Zip(got).Count(p => p.First == p.Second);
        }
        return (words, wordHits, sentences, sentenceHits);
    }

    /// The text field after typing the sentence's Dubeolsik keys and pressing Return.
    public static string Type(Decider decider, string expected, Random? random = null)
    {
        var engine = new Engine(decider, new CorrectionStore(null));
        var field = "";
        void Apply(KeyAction a, string? passed)
        {
            field = field[..Math.Max(0, field.Length - a.Backspaces)] + a.Text;
            if ((!a.Swallow || a.Replay) && passed is not null) field += passed;
        }
        // Keys as typed: Hangul becomes its Dubeolsik keys, everything else stays as is.
        var keys = string.Concat(expected.Select(c => c >= 0xAC00 && Hangul.ToKeys(c.ToString()) is { } k ? k : c.ToString()))
            .ToCharArray();
        if (random is not null && SlipRate > 0)
            for (int i = 0; i + 1 < keys.Length; i++)
                if (char.IsAsciiLetterUpper(keys[i]) && char.IsAsciiLetterLower(keys[i + 1]) && random.NextDouble() < SlipRate)
                    keys[i + 1] = char.ToUpperInvariant(keys[++i]);   // one key too long, not a cascade
        foreach (var c in keys)
        {
            if (char.IsAsciiLetter(c)) Apply(engine.Letter(c), c.ToString());
            else Apply(engine.Boundary(c.ToString()), c.ToString());
        }
        Apply(engine.Boundary(null), null);
        return field;
    }
}
