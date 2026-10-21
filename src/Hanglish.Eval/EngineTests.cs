using Hanglish.Core;

/// <summary>Drives the engine against a simulated text field.</summary>
static class EngineTests
{
    sealed class Field
    {
        public string Text = "";
        public readonly Engine Engine;
        public Field(Decider decider, PreviewMode mode, CorrectionStore? store = null) =>
            Engine = new Engine(decider, store ?? new CorrectionStore(null)) { Preview = mode };

        void Apply(KeyAction a, string passed)
        {
            Text = Text[..Math.Max(0, Text.Length - a.Backspaces)] + a.Text;
            if (!a.Swallow || a.Replay) Text = passed == "\b" ? Text[..Math.Max(0, Text.Length - 1)] : Text + passed;
        }

        /// <summary>Types keys; "_" is Space, "!" is the flip key, "<" is Backspace, "~" is Esc.</summary>
        public Field Type(string keys)
        {
            foreach (var k in keys)
            {
                switch (k)
                {
                    case '_': Apply(Engine.Boundary(" "), " "); break;
                    case '.': Apply(Engine.Boundary("."), "."); break;
                    case '!': { var a = Engine.Flip(); Text = Text[..(Text.Length - a.Backspaces)] + a.Text; break; }
                    case '<': Apply(Engine.Backspace(), "\b"); break;
                    case '~': { var a = Engine.Escape(); Text = Text[..(Text.Length - a.Backspaces)] + a.Text; break; }
                    default: Apply(Engine.Letter(k), k.ToString()); break;
                }
            }
            return this;
        }
    }

    public static int Run(Decider decider)
    {
        int failures = 0;
        void Check(string name, PreviewMode mode, string keys, string expected)
        {
            var field = new Field(decider, mode).Type(keys);
            bool ok = field.Text == expected;
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} [{mode}] {name}: \"{field.Text}\"" + (ok ? "" : $" (want \"{expected}\")"));
        }
        foreach (var mode in new[] { PreviewMode.Hangul, PreviewMode.Raw })
        {
            Check("korean", mode, "dkssudgktpdy_", "안녕하세요 ");
            Check("english", mode, "hello_world_", "hello world ");
            Check("mixed", mode, "githubdptj_", "github에서 ");
            Check("backspace mid-word", mode, "dkssus<d_", "안녕 ");
            Check("flip committed word", mode, "dkssud_!", "dkssud ");
            Check("flip twice restores", mode, "dkssud_!!", "안녕 ");
            Check("flip while typing", mode, "hello!", "ㅗ디ㅣㅐ");
            Check("escape keeps latin", mode, "dkssud~", "dkssud");
            Check("punctuation", mode, "dkssud.", "안녕.");
            Check("flip after trailing backspace", mode, "dkssud_<!", "dkssud");
            Check("name", mode, "rlaalswo_", "김민재 ");
            Check("backfill to do list", mode, "dhsmf_to_do_list_", "오늘 to do list ");
            Check("korean stays korean", mode, "dkssud_go_", "안녕 해 ");
            Check("backfill so good", mode, "so_good_", "so good ");
            Check("backfill go home", mode, "go_home_", "go home ");
            Check("korean after ambiguous", mode, "so_dkssud_", "내 안녕 ");
            Check("shift held one key too long", mode, "dlTEk_", "있다 ");
        }
        // A flip is remembered after the same preceding word only.
        var store = new CorrectionStore(null);
        string Run(string keys) { var f = new Field(decider, PreviewMode.Hangul, store); f.Type(keys); return f.Text; }
        Run("rmfo_dkssud_!");
        void Expect(string name, string got, string want)
        {
            bool ok = got == want;
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: \"{got}\"" + (ok ? "" : $" (want \"{want}\")"));
        }
        Expect("remembers flip in context", Run("rmfo_dkssud_"), "그래 dkssud ");
        Expect("flip not applied elsewhere", Run("dkssud_"), "안녕 ");
        Run("wjsms_dkssud_!");
        Expect("two contexts make it global", Run("dkssud_"), "dkssud ");
        return failures;
    }
}
