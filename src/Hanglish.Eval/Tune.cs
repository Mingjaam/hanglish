using Hanglish.Core;

/// <summary>Coordinate search over <see cref="Decider.Tuning"/> on the dev sets, equal weight per set.</summary>
static class Tune
{
    public static void Run(Decider decider, string[] devFiles)
    {
        double Score(Decider.Tuning t)
        {
            decider.Knobs = t;
            return devFiles.Average(f => { var r = CorpusEval.Run(decider, f, false); return (double)r.SentenceHits / r.Sentences; });
        }
        var best = new Decider.Tuning();
        double bestScore = Score(best);
        Console.WriteLine($"start {bestScore:P2}");
        var axes = new (string Name, double[] Values, Func<Decider.Tuning, double, Decider.Tuning> Set)[]
        {
            ("SyllableBonus", new[] { 1.0, 1.5, 2, 3, 5, 8 }, (t, v) => t with { SyllableBonus = v }),
            ("UnknownStemPenalty", new[] { 0.001, 0.01, 0.05, 0.2, 1 }, (t, v) => t with { UnknownStemPenalty = v }),
            ("MixedPrior", new[] { 0.005, 0.01, 0.02, 0.05, 0.1 }, (t, v) => t with { MixedPrior = v }),
            ("ContextDecay", new[] { 0.3, 0.5, 0.6, 0.8, 1.0 }, (t, v) => t with { ContextDecay = v }),
            ("PriorLow", new[] { 0.05, 0.1, 0.2, 0.3 }, (t, v) => t with { PriorLow = v, PriorHigh = 1 - v }),
            ("LoosePenalty", new[] { 0.005, 0.01, 0.03, 0.1, 0.3 }, (t, v) => t with { LoosePenalty = v }),
            ("CommonWord", new[] { 3e-6, 1e-5, 3e-5, 1e-4 }, (t, v) => t with { CommonWord = v }),
        };
        for (int round = 0; round < 2; round++)
            foreach (var (name, values, set) in axes)
                foreach (var v in values)
                {
                    var candidate = set(best, v);
                    double s = Score(candidate);
                    if (s > bestScore + 1e-9) { best = candidate; bestScore = s; Console.WriteLine($"  {name}={v} -> {s:P2}"); }
                }
        decider.Knobs = best;
        Console.WriteLine($"best {bestScore:P2}: {best}");
    }
}
