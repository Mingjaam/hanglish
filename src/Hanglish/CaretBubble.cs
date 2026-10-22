using System.Runtime.InteropServices;
using Hanglish.Core;
using static Hanglish.Native;

namespace Hanglish;

/// <summary>
/// A click-through layered window above the text caret showing what the word being typed will
/// become. Per-pixel alpha keeps the rounded corners and shadow smooth.
/// </summary>
sealed class CaretBubble : Form
{
    public CaretBubble()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
            var p = base.CreateParams;
            p.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            return p;
        }
    }

    /// <summary>
    /// Shows the guess above the caret. Without a caret the bubble hides, or with <paramref name="anywhere"/>
    /// (short notices) sits near the bottom of the screen instead.
    /// </summary>
    public void Present(Guess? guess, BubbleStyle style, bool anywhere = false)
    {
        if (guess is null) { Hide(); return; }
        var found = Caret();
        if (found is null && !anywhere) { Hide(); return; }
        var screen = found is { } c ? Screen.FromRectangle(c) : Screen.FromHandle(GetForegroundWindow());
        float scale = Dpi(screen) / 96f;
        using var bitmap = BubbleRenderer.Render(guess, style, scale);
        int margin = BubbleRenderer.Margin(scale);
        var area = screen.WorkingArea;
        if (found is not { } caret)
        {
            if (!Visible) Show();
            Draw(bitmap, new Point(area.Left + (area.Width - bitmap.Width) / 2, area.Bottom - bitmap.Height - (int)(48 * scale)));
            return;
        }
        int x = Math.Clamp(caret.Left - margin - (int)(10 * scale), area.Left - margin, area.Right - bitmap.Width + margin);
        int y = caret.Top - bitmap.Height + margin - (int)(4 * scale);
        if (y + margin < area.Top) y = caret.Bottom - margin + (int)(6 * scale);
        if (!Visible) Show();
        Draw(bitmap, new Point(x, y));
    }

    void Draw(Bitmap bitmap, Point at)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screenDc);
        IntPtr hbitmap = bitmap.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memory, hbitmap);
        try
        {
            var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
            var source = new POINT();
            var position = new POINT { X = at.X, Y = at.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref position, ref size, memory, ref source, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(hbitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    static uint Dpi(Screen screen)
    {
        var monitor = MonitorFromPoint(new POINT { X = screen.Bounds.Left + 1, Y = screen.Bounds.Top + 1 }, 2);
        return GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi : 96;
    }

    /// <summary>
    /// The caret in screen coordinates: the Win32 caret first (Notepad, classic apps),
    /// then the accessibility caret object (Chrome, Edge, Electron apps).
    /// </summary>
    static Rectangle? Caret()
    {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        if (!GetGUIThreadInfo(thread, ref info)) return null;
        if (info.hwndCaret != IntPtr.Zero && info.rcCaret.Bottom > info.rcCaret.Top)
        {
            var origin = new POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
            ClientToScreen(info.hwndCaret, ref origin);
            return new Rectangle(origin.X, origin.Y, Math.Max(1, info.rcCaret.Right - info.rcCaret.Left),
                info.rcCaret.Bottom - info.rcCaret.Top);
        }
        var focus = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : GetForegroundWindow();
        var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");   // IAccessible
        try
        {
            if (AccessibleObjectFromWindow(focus, OBJID_CARET, ref iid, out var obj) != 0
                || obj is not Accessibility.IAccessible caret) return null;
            caret.accLocation(out int left, out int top, out int width, out int height, 0);
            return height > 0 ? new Rectangle(left, top, Math.Max(1, width), height) : null;
        }
        catch (COMException) { return null; }
    }
}
