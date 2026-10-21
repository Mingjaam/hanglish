using System.Runtime.InteropServices;

// End-to-end check: with Hanglish running, type physical-looking keys into a real
// TextBox and write what the box ends up containing to result.txt.
// Script syntax: letters as-is, "_" Space, "<" Backspace, "!" 한/영 key, "|" Enter.
static class E2E
{
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    /// <summary>A real click is the reliable way for a background-launched window to take focus.</summary>
    static void Click(Control target)
    {
        var p = target.PointToScreen(new Point(target.Width / 2, target.Height / 2));
        SetCursorPos(p.X, p.Y);
        foreach (var flag in new[] { 2u, 4u })
            SendInput(1, new[] { new INPUT { type = 0, u = new U { mi = new MOUSEINPUT { flags = flag } } } }, Marshal.SizeOf<INPUT>());
    }

    static void Unicode(char c)
    {
        foreach (var up in new[] { false, true })
        {
            var i = new INPUT { type = 1, u = new U { ki = new KEYBDINPUT { scan = c, flags = 4u | (up ? 2u : 0u) } } };
            SendInput(1, new[] { i }, Marshal.SizeOf<INPUT>());
        }
    }

    static void Key(ushort vk, bool up)
    {
        uint extended = vk == 0xA3 ? 1u : 0u;   // right Ctrl is an extended key
        var i = new INPUT { type = 1, u = new U { ki = new KEYBDINPUT { vk = vk, scan = (ushort)MapVirtualKey(vk, 0), flags = (up ? 2u : 0u) | extended } } };
        SendInput(1, new[] { i }, Marshal.SizeOf<INPUT>());
    }

    static IntPtr expected;

    static void Press(ushort vk, bool shift)
    {
        // Stop the whole run rather than type into a window the user is working in.
        if (expected != IntPtr.Zero && GetForegroundWindow() != expected) Environment.Exit(2);
        if (shift) Key(0x10, false);
        Key(vk, false); Key(vk, true);
        if (shift) Key(0x10, true);
    }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static async Task Type(string keys, int delay)
    {
        foreach (var c in keys)
        {
            switch (c)
            {
                case '_': Press(0x20, false); break;
                case '<': Press(0x08, false); break;
                case '!': Press(0xA3, false); break;
                case '^': Press(0x15, false); break;
                case '|': Press(0x0D, false); break;
                case '.': Press(0xBE, false); break;
                default: Press((ushort)char.ToUpperInvariant(c), char.IsUpper(c)); break;
            }
            await Task.Delay(delay);
        }
    }

    /// <summary>Types each case plus Enter into a classic console window running ConsoleEcho.</summary>
    static void Console(List<string> cases, int delay, string echo)
    {
        var output = Path.Combine(Path.GetTempPath(), "hnw-console.txt");
        File.Delete(output);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("conhost.exe", $"\"{echo}\" \"{output}\"") { UseShellExecute = false });
        Thread.Sleep(2500);
        // The console window is reported on the hosted process, not on conhost.
        var window = System.Diagnostics.Process.GetProcessesByName("ConsoleEcho")
            .Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
        if (window == IntPtr.Zero || !GetWindowRect(window, out var r))
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), "ABORT: console window not found");
            try { process?.Kill(true); } catch { }
            return;
        }
        SetCursorPos((r.L + r.R) / 2, (r.T + r.B) / 2);
        foreach (var flag in new[] { 2u, 4u })
            SendInput(1, new[] { new INPUT { type = 0, u = new U { mi = new MOUSEINPUT { flags = flag } } } }, Marshal.SizeOf<INPUT>());
        Thread.Sleep(500);
        expected = window;
        // Never type into whatever else happens to be in front.
        if (GetForegroundWindow() != window)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), "ABORT: console window is not in front");
            try { process?.Kill(true); } catch { }
            return;
        }
        foreach (var line in cases) { Type(line.Split('	')[0] + "|", delay).Wait(); Thread.Sleep(400); }
        Thread.Sleep(500);
        var got = File.Exists(output) ? File.ReadAllLines(output) : Array.Empty<string>();
        var results = cases.Select((c, i) =>
        {
            var want = c.Split('	')[1].TrimEnd('|');
            var line = i < got.Length ? got[i] : "(none)";
            return $"{(line == want ? "ok  " : "FAIL")}	console	{c.Split('	')[0]}	{line}	(want {want})";
        });
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "result.txt"), results);
        try { process?.Kill(true); } catch { }
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 2 && args[2] == "--console")
        {
            Console(File.ReadAllLines(args[0]).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList(),
                int.Parse(args[1]), args[3]);
            return;
        }
        var cases = File.ReadAllLines(args[0]).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        int delay = args.Length > 1 ? int.Parse(args[1]) : 40;
        var results = new List<string>();
        Application.EnableVisualStyles();
        var form = new Form { Text = "Hanglish E2E", Width = 600, Height = 160, TopMost = true, StartPosition = FormStartPosition.CenterScreen };
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, Font = new Font("Malgun Gothic", 14) };
        if (Environment.GetEnvironmentVariable("E2E_NO_IME") == "1") box.ImeMode = ImeMode.Disable;
        // E2E_LABEL names the box for accessibility, e.g. "인증번호 입력" to test code-field detection.
        if (Environment.GetEnvironmentVariable("E2E_LABEL") is { Length: > 0 } label) box.AccessibleName = label;
        form.Controls.Add(box);
        form.Shown += async (_, _) =>
        {
            await Task.Delay(500);
            Click(box);
            await Task.Delay(300);
            expected = form.Handle;
            foreach (var line in cases)
            {
                var parts = line.Split('\t');
                form.Activate(); box.Focus(); box.Clear();
                Click(box);   // a click is how a real user starts over; it resets Hanglish too
                await Task.Delay(150);
                if (GetForegroundWindow() != form.Handle) { results.Add("ABORT: test window is not in front"); break; }
                if (parts[0].StartsWith("?shot "))
                {
                    // Type without finishing the word, then photograph the area around the box.
                    await Type(parts[0][6..], delay);
                    await Task.Delay(400);
                    var area = form.RectangleToScreen(form.ClientRectangle);
                    area.Inflate(40, 90);
                    using var shot = new Bitmap(area.Width, area.Height);
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    shot.Save(Path.Combine(AppContext.BaseDirectory, parts[1]));
                    results.Add($"shot {parts[1]} text=[{box.Text}]");
                    continue;
                }
                if (parts[0] == "?probe")
                {
                    bool fg = GetForegroundWindow() == form.Handle;
                    Unicode('U'); await Task.Delay(100);
                    Press((ushort)'V', false); await Task.Delay(100);
                    Press(0x08, false); await Task.Delay(300);
                    results.Add($"probe foreground={fg} text=[{box.Text}] (U=unicode ok, V then backspace => U)");
                    continue;
                }
                foreach (var c in parts[0])
                {
                    switch (c)
                    {
                        case '_': Press(0x20, false); break;
                        case '<': Press(0x08, false); break;
                        case '!': Press(0xA3, false); break;
                case '^': Press(0x15, false); break;
                        case '|': Press(0x0D, false); break;
                        case '.': Press(0xBE, false); break;
                        default: Press((ushort)char.ToUpperInvariant(c), char.IsUpper(c)); break;
                    }
                    await Task.Delay(delay);
                }
                await Task.Delay(300);
                var got = box.Text.Replace("\r\n", "|");
                results.Add($"{(got == parts[1] ? "ok  " : "FAIL")}\t{parts[0]}\t{got}\t(want {parts[1]})");
            }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "result.txt"), results);
            form.Close();
        };
        Application.Run(form);
    }
}
