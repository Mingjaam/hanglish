namespace Hanglish.Core;

/// <summary>
/// Word probabilities over raw keystrokes: a lexicon unigram backed off to an
/// interpolated Witten-Bell character n-gram, so unknown words still get a score.
/// </summary>
public sealed class KeyModel
{
    const int Order = 5;
    const char Begin = '^', End = '$';
    const double LexiconWeight = 0.7;

    readonly Dictionary<string, double> lexicon = new();
    // context -> (next key -> count); "" is the unigram context.
    readonly Dictionary<string, Dictionary<char, double>> counts = new();
    readonly Dictionary<string, (double Total, int Types)> summaries = new();
    int alphabet;

    public bool Contains(string word) => lexicon.ContainsKey(word);

    public double Frequency(string word) => lexicon.GetValueOrDefault(word);

    public void Add(string keys, double frequency)
    {
        lexicon[keys] = lexicon.GetValueOrDefault(keys) + frequency;
    }

    /// <summary>Trains the n-gram on the lexicon, weighting words between type and token counts.</summary>
    public void Train()
    {
        foreach (var (keys, frequency) in lexicon)
        {
            double weight = 1 + Math.Log10(1 + frequency * 1e7);
            var padded = new string(Begin, Order - 1) + keys + End;
            for (int i = Order - 1; i < padded.Length; i++)
                for (int n = 0; n < Order; n++)
                {
                    var context = padded.Substring(i - n, n);
                    if (!counts.TryGetValue(context, out var next)) counts[context] = next = new();
                    next[padded[i]] = next.GetValueOrDefault(padded[i]) + weight;
                }
        }
        foreach (var (context, next) in counts) summaries[context] = (next.Values.Sum(), next.Count);
        alphabet = counts[""].Count + 1;
    }

    double Next(string context, char key)
    {
        double p = 1.0 / alphabet;
        for (int n = 0; n <= context.Length; n++)
        {
            var h = context.Substring(context.Length - n);
            if (!summaries.TryGetValue(h, out var s)) break;
            double c = counts[h].GetValueOrDefault(key);
            p = (c + s.Types * p) / (s.Total + s.Types);
        }
        return p;
    }

    /// <summary>log P(keys as one whole word) under the n-gram alone.</summary>
    public double NgramLog(string keys)
    {
        var padded = new string(Begin, Order - 1) + keys + End;
        double log = 0;
        for (int i = Order - 1; i < padded.Length; i++)
            log += Math.Log(Next(padded.Substring(i - Order + 1, Order - 1), padded[i]));
        return log;
    }

    /// <summary>log P(word): mixture of the lexicon frequency and the n-gram.</summary>
    public double WordLog(string keys) =>
        Math.Log(LexiconWeight * lexicon.GetValueOrDefault(keys) + (1 - LexiconWeight) * Math.Exp(NgramLog(keys)));
}
