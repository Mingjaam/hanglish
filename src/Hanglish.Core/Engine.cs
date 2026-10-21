namespace Hanglish.Core;

public enum PreviewMode
{
    /// <summary>Show Hangul while typing, like a Korean IME; switch to English at the word's end.</summary>
    Hangul,
    /// <summary>Let the Latin letters through and replace them at the word's end when Korean wins.</summary>
    Raw,
}

/// <summary>
/// What the keyboard layer must do for one key: optionally swallow it, erase
/// <see cref="Backspaces"/> characters, type <see cref="Text"/>, then replay the key.
/// </summary>
/// <summary>What the word being typed will become at its end, for the caret preview.</summary>
public sealed record Guess(string Text, bool Korean, string Alternative, double Confidence);

public readonly record struct KeyAction(bool Swallow, int Backspaces = 0, string Text = "", bool Replay = false)
{
    public static readonly KeyAction Pass = new(false);
    public bool Injects => Backspaces > 0 || Text.Length > 0;
}

/// <summary>
/// The typing state machine, independent of Windows. It tracks the word being typed,
/// what is shown in the text field for it, and the words committed since the caret last jumped,
/// so that a confident word can still correct ambiguous words before it ("so" + "good").
/// </summary>
public sealed class Engine
{
    sealed class Word
    {
        public required string Raw;
        public required string Before;      // preceding word, the context for remembered flips
        public required string Model;       // what the model alone chose
        public required string[] Options;   // [shown first, other reading]
        public int Shown;
        public string Trailing = "";        // boundary characters typed after the word
        public bool Waiting;                // ambiguous; a following word may still switch it
        public bool Chosen;                 // set by the user or by memory, never switched automatically
        public string Text => Options[Shown];
        public string Other => Options[1 - Shown];
    }

    const double AnchorConfidence = 0.9, StartAnchorConfidence = 0.97, WaitBelow = 0.75;
    const int WaitWindow = 3, ChainLimit = 20;

    readonly Decider decider;
    readonly CorrectionStore corrections;
    readonly List<Word> chain = new();
    string raw = "", shown = "";

    public Engine(Decider decider, CorrectionStore corrections)
    {
        this.decider = decider;
        this.corrections = corrections;
    }

    public PreviewMode Preview { get; set; } = PreviewMode.Hangul;

    public string Composing => raw;

    /// <summary>The expected result for the word being typed, or null when nothing is being typed.</summary>
    public Guess? Guess()
    {
        if (raw.Length == 0) return null;
        var (word, confidence) = Decide();
        return new Guess(word.Text, EndLanguage(word.Text) == Language.Korean && StartLanguage(word.Text) == Language.Korean,
            word.Other, confidence);
    }

    public KeyAction Letter(char key)
    {
        raw += key;
        if (Preview == PreviewMode.Raw) { shown = raw; return KeyAction.Pass; }
        return Show(PreviewText(), swallow: true);
    }

    public KeyAction Backspace()
    {
        if (raw.Length == 0)
        {
            // Erasing a trailing space keeps the words in play; erasing into a word ends tracking.
            if (chain.Count > 0 && chain[^1].Trailing.Length > 0) chain[^1].Trailing = chain[^1].Trailing[..^1];
            else chain.Clear();
            return KeyAction.Pass;
        }
        raw = raw[..^1];
        if (Preview == PreviewMode.Raw) { shown = raw; return KeyAction.Pass; }
        return Show(raw.Length == 0 ? "" : PreviewText(), swallow: true);
    }

    /// <param name="text">The character the key types (" ", ".", "1"), or null for Enter/Tab.</param>
    public KeyAction Boundary(string? text)
    {
        if (raw.Length == 0)
        {
            if (text is null) chain.Clear();
            else if (chain.Count > 0) chain[^1].Trailing += text;
            return KeyAction.Pass;
        }
        var (word, confidence) = Decide();
        int first = Backfill(word, confidence, out var oldTail);
        chain.Add(word);
        var action = Rewrite(first, oldTail) with { Swallow = true, Replay = true };
        word.Trailing = text ?? "";
        if (text is null) chain.Clear();
        Trim();
        return action;
    }

    /// <summary>Esc: keep the word being typed as the Latin letters.</summary>
    public KeyAction Escape()
    {
        if (raw.Length == 0 || shown == raw) { Reset(); return KeyAction.Pass; }
        var action = Show(raw, swallow: true);
        chain.Add(new Word
        {
            Raw = raw, Before = PreviousWord(), Model = raw, Options = new[] { raw, Hangul.Compose(raw) }, Chosen = true,
        });
        raw = shown = "";
        Trim();
        return action;
    }

    /// <summary>The flip key: switch the current or just-committed word to the other reading and remember it.</summary>
    public KeyAction Flip()
    {
        if (raw.Length > 0)
        {
            var (word, _) = Decide();
            word.Shown = 1 - word.Shown;
            word.Chosen = true;
            word.Waiting = false;
            var action = Show(word.Text, swallow: true);
            chain.Add(word);
            raw = shown = "";
            Learn(word);
            return action;
        }
        if (chain.Count == 0) return KeyAction.Pass;
        var last = chain[^1];
        int erase = last.Text.Length + last.Trailing.Length;
        last.Shown = 1 - last.Shown;
        last.Chosen = true;
        last.Waiting = false;
        Learn(last);
        return new KeyAction(true, erase, last.Text + last.Trailing);
    }

    /// <summary>The caret moved or focus changed: forget everything about the field.</summary>
    public void Reset()
    {
        raw = shown = "";
        chain.Clear();
    }

    string PreviousWord() => chain.Count > 0 ? chain[^1].Text : "";

    string BeforeText() => string.Concat(chain.Select(w => w.Text + w.Trailing));

    string PreviewText() => corrections.Lookup(raw, PreviousWord()) ?? Hangul.Compose(raw);

    (Word, double) Decide()
    {
        var decision = decider.Decide(raw, BeforeText());
        var model = decision.Best.Text;
        var other = decision.Best.Language == Language.English ? Hangul.Compose(raw) : raw;
        var before = PreviousWord();
        var remembered = corrections.Lookup(raw, before);
        if (remembered is not null && remembered != model)
            return (new Word { Raw = raw, Before = before, Model = model, Options = new[] { remembered, model }, Chosen = true }, 1);
        var word = new Word { Raw = raw, Before = before, Model = model, Options = new[] { model, other } };
        word.Waiting = remembered is null && Readable(other, raw)
                       && (decision.Best.Probability < WaitBelow || decider.BothCommon(raw));
        return (word, decision.Best.Probability);
    }

    /// <summary>
    /// Lets a confident new word switch the ambiguous words right before it to its own language.
    /// Returns the index of the first chain word whose text may change, and the text shown from there on.
    /// </summary>
    int Backfill(Word word, double confidence, out string oldTail)
    {
        int start = chain.Count;
        while (start > 0 && chain.Count - start < WaitWindow && chain[start - 1].Waiting && !chain[start - 1].Chosen
               && chain[start - 1].Trailing.Length > 0 && chain[start - 1].Trailing.All(c => c == ' '))
            start--;
        oldTail = string.Concat(chain.Skip(start).Select(w => w.Text + w.Trailing)) + shown;
        if (start == chain.Count || word.Waiting || confidence < AnchorConfidence) return start;

        // Each waiting word joins the language its neighbors meet it with: the script the
        // confident word starts in, and the script the word before the run ends in.
        var right = StartLanguage(word.Text);
        var waiting = chain.Skip(start).ToList();
        Language? left = start > 0 ? EndLanguage(chain[start - 1].Text) : null;
        bool settle = left == right || waiting.Count >= 2 || (left is null && confidence >= StartAnchorConfidence);
        foreach (var w in waiting)
        {
            // Switch only when the other reading is the one that meets the confident word in its language.
            if (settle && EndLanguage(w.Text) != right && EndLanguage(w.Other) == right) w.Shown = 1 - w.Shown;
            w.Waiting = false;
        }
        return start;
    }

    /// <summary>Replaces what is shown from chain[first] on (plus the current word) with the chain's text.</summary>
    KeyAction Rewrite(int first, string oldTail)
    {
        var newTail = string.Concat(chain.Skip(first).Select(w => w.Text + w.Trailing));
        int common = 0;
        while (common < oldTail.Length && common < newTail.Length && oldTail[common] == newTail[common]) common++;
        raw = shown = "";
        return new KeyAction(true, oldTail.Length - common, newTail[common..]);
    }

    void Trim()
    {
        if (chain.Count > ChainLimit) chain.RemoveRange(0, chain.Count - ChainLimit);
    }

    void Learn(Word word)
    {
        // Flipping back to the model's own choice undoes the memory for these keys here.
        if (word.Text == word.Model) corrections.Forget(word.Raw, word.Before);
        else corrections.Remember(word.Raw, word.Before, word.Text);
    }

    static Language StartLanguage(string text) =>
        text.Length > 0 && char.IsAsciiLetter(text[0]) ? Language.English : Language.Korean;

    static Language EndLanguage(string text) =>
        text.Length > 0 && char.IsAsciiLetter(text[^1]) ? Language.English : Language.Korean;

    /// <summary>A reading worth switching to later: whole syllables, or jamo the lexicon knows (ㅋㅋ).</summary>
    bool Readable(string text, string raw) =>
        text.All(c => c < 0x80 || c is >= '가' and <= '힣') || decider.KnownKorean(raw);

    KeyAction Show(string text, bool swallow)
    {
        int common = 0;
        while (common < shown.Length && common < text.Length && shown[common] == text[common]) common++;
        var action = new KeyAction(swallow, shown.Length - common, text[common..]);
        shown = text;
        return action;
    }
}
