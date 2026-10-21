using Hanglish.Core;

namespace Hanglish;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var single = new Mutex(true, @"Local\Hanglish", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Hanglish.Show");
        if (!first)
        {
            // Launched again from a shortcut: ask the running copy to open its settings window.
            showSignal.Set();
            return;
        }
        ApplicationConfiguration.Initialize();
        Decider decider;
        try { decider = new Decider(); }
        catch (Exception e)
        {
            MessageBox.Show("판단 모델을 불러오지 못했습니다.\n" + e.Message, "한글리쉬", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var app = new TrayApp(decider);
        app.ListenForShow(showSignal);
        // --snapshot=<dir> renders the settings sections and bubble styles to PNGs and exits,
        // without capturing the screen (for docs and visual checks).
        if (args.FirstOrDefault(a => a.StartsWith("--snapshot=")) is { } snapshot)
        {
            var dir = snapshot["--snapshot=".Length..];
            Directory.CreateDirectory(dir);
            for (int i = 0; i < 5; i++)
            {
                using var form = new SettingsForm(app, i) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
                form.Show();
                Application.DoEvents();
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(dir, $"settings-{i}.png"));
            }
            foreach (var style in Enum.GetValues<BubbleStyle>())
                foreach (var (text, korean, alt) in new[] { ("김민재", true, "rlaalswo"), ("hello", false, "ㅗ디ㅣㅐ") })
                {
                    using var bubble = BubbleRenderer.Render(new Guess(text, korean, alt, 0.99), style, 1.25f);
                    bubble.Save(Path.Combine(dir, $"bubble-{style}-{(korean ? "ko" : "en")}.png"));
                }
            app.ExitThread();
            return;
        }
        // --settings[=section] opens the settings window at launch (used for screenshots, too).
        if (args.FirstOrDefault(a => a.StartsWith("--settings")) is { } open)
            app.OpenSettings(open.Contains('=') ? int.Parse(open[(open.IndexOf('=') + 1)..]) : 0);
        Application.Run(app);
    }
}
