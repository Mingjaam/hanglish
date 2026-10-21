using System.Diagnostics;
using Hanglish.Core;

// Usage: Hanglish.Eval cases.txt   (each line: an expected sentence; raw keys are derived from it)
//        Hanglish.Eval              (interactive: type raw keys)
Console.OutputEncoding = System.Text.Encoding.UTF8;
var clock = Stopwatch.StartNew();
var decider = new Decider();
Console.WriteLine($"model ready in {clock.ElapsedMilliseconds} ms");

if (args.Length == 1 && args[0] == "--engine")
{
    int failures = EngineTests.Run(decider);
    Console.WriteLine(failures == 0 ? "engine tests passed" : $"{failures} engine test(s) failed");
    Environment.Exit(failures == 0 ? 0 : 1);
}

if (args.Length == 2 && args[0] == "--frames")
{
    Frames.Write(decider, args[1]);
    return;
}

if (args.Length >= 3 && args[0] == "--parity")
{
    // --parity out.tsv files...: "sentence<TAB>what this engine typed", for the macOS engine to match.
    var lines = args.Skip(2).SelectMany(f => File.ReadLines(f).Where(l => l.Trim().Length > 0).Take(250))
        .Select(s => s + "	" + CorpusEval.Type(decider, s));
    File.WriteAllLines(args[1], lines);
    return;
}

if (args.Length >= 2 && args[0] == "--tune")
{
    Tune.Run(decider, args.Skip(1).ToArray());
    return;
}

if (args.Length >= 2 && args[0] == "--explain")
{
    foreach (var raw in args.Skip(1))
        Console.WriteLine($"{raw}: " + string.Join(", ", decider.Decide(raw).Ranking.Take(4).Select(c => $"{c.Text}({c.Language}) {c.Probability:F4}")));
    return;
}

if (args.Length >= 1 && args[0] == "--corpus")
{
    // --corpus [-v] file...: word and sentence accuracy through the full engine.
    bool verbose = args.Contains("-v");
    if (args.Contains("--slip")) CorpusEval.SlipRate = 1.0;
    foreach (var file in args.Skip(1).Where(a => !a.StartsWith('-')))
    {
        var r = CorpusEval.Run(decider, file, verbose);
        Console.WriteLine($"{Path.GetFileName(file),-16} words {100.0 * r.WordHits / r.Words,5:F1}%  sentences {100.0 * r.SentenceHits / r.Sentences,5:F1}%  ({r.Sentences})");
    }
    return;
}

if (args.Length == 0)
{
    while (Console.ReadLine() is { } line)
    {
        var before = "";
        foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var d = decider.Decide(word, before);
            before += d.Best.Text + " ";
            Console.Write(d.Best.Text + " ");
        }
        Console.WriteLine();
    }
    return;
}

int total = 0, correct = 0;
foreach (var line in File.ReadLines(args[0]))
{
    if (line.Trim().Length == 0 || line.StartsWith('#')) continue;
    var context = "";
    foreach (var expected in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        // Raw keys are what a Dubeolsik typist presses for the expected text.
        var raw = string.Concat(expected.Select(c => Hangul.ToKeys(c.ToString()) ?? c.ToString()));
        var d = decider.Decide(raw, context);
        total++;
        if (d.Best.Text == expected) correct++;
        else Console.WriteLine($"MISS {raw} -> {d.Best.Text} (want {expected}) | "
            + string.Join(", ", d.Ranking.Take(3).Select(c => $"{c.Text}:{c.Probability:F3}")));
        context += d.Best.Text + " ";
    }
}
Console.WriteLine($"accuracy {correct}/{total} = {100.0 * correct / total:F1}%");
