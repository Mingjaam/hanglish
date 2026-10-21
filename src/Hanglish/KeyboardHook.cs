using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hanglish.Core;
using static Hanglish.Native;

namespace Hanglish;

public enum FlipKey { HangulKey, RightCtrl }

/// <summary>
/// Feeds global keystrokes to the <see cref="Engine"/> and types its output with SendInput.
/// Keys the engine owns are swallowed and re-typed, so their order relative to our own
/// injected text is preserved.
/// </summary>
sealed class KeyboardHook : IDisposable
{
    // Marks our own injected input so the hook lets it through untouched.
    static readonly UIntPtr Tag = (UIntPtr)0x484E5957;
    const ushort BackspaceScan = 0x0E;

    readonly Engine engine;
    readonly HookProc keyboardProc, mouseProc;
    readonly WinEventProc focusProc;
    readonly IntPtr keyboardHook, mouseHook, foregroundHook, focusHook;
    bool flipArmed;
    long flipDownAt;
    string? currentApp = ForegroundApp();

    public KeyboardHook(Engine engine)
    {
        this.engine = engine;
        keyboardProc = OnKey;
        mouseProc = OnMouse;
        focusProc = OnFocus;
        var module = GetModuleHandle(null);
        keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
        if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            throw new InvalidOperationException($"키보드 후킹 실패 (오류 {Marshal.GetLastWin32Error()})");
        foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, focusProc,
            0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        focusHook = SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, IntPtr.Zero, focusProc,
            0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public bool Enabled { get; set; } = true;
    public FlipKey FlipKey { get; set; } = FlipKey.RightCtrl;
    public HashSet<string> ExcludedApps { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process name of the last foreground app other than this one.</summary>
    public string? LastApp { get; private set; }

    public event Action? ForegroundChanged;

    /// <summary>Raised after every key the hook looked at, so the caret preview can refresh.</summary>
    public event Action? KeyHandled;

    /// <summary>The 한/영 key was pressed while it is not the flip key (it is swallowed).</summary>
    public event Action? HangulKeyPressed;

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(keyboardHook, code, wParam, lParam);
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        bool swallow = false;
        if (info.dwExtraInfo != Tag && Enabled)
        {
            try { swallow = Handle((int)wParam, info); }
            catch (Exception e) { Debug.WriteLine(e); engine.Reset(); }
            KeyHandled?.Invoke();
        }
        return swallow ? 1 : CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    bool Handle(int message, KBDLLHOOKSTRUCT info)
    {
        int vk = (int)info.vkCode;
        bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;

        if (vk == VK_HANGUL)
        {
            // The 한/영 key no longer switches the IME: Korean and English both come out of English mode.
            if (down && FlipKey == FlipKey.HangulKey) Apply(engine.Flip(), info);
            else if (down) HangulKeyPressed?.Invoke();
            return true;
        }
        if (FlipKey == FlipKey.RightCtrl && vk == VK_RCONTROL)
        {
            if (down && !flipArmed) { flipArmed = true; flipDownAt = Environment.TickCount64; }
            else if (!down && flipArmed)
            {
                flipArmed = false;
                if (Environment.TickCount64 - flipDownAt < 500) Apply(engine.Flip(), info);
            }
            return false;
        }
        if (!down) return false;
        flipArmed = false;

        if (vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT or VK_CAPITAL or VK_CONTROL or VK_LCONTROL or VK_RCONTROL
            or VK_MENU or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN)
            return false;
        bool shortcut = Pressed(VK_CONTROL) || Pressed(VK_MENU) || Pressed(VK_LWIN) || Pressed(VK_RWIN);
        if (shortcut || Paused()) { engine.Reset(); return false; }

        if (vk is >= 'A' and <= 'Z')
        {
            if (engine.Composing.Length == 0 && PasswordField()) { engine.Reset(); return false; }
            if (engine.Composing.Length == 0) ForceLatinIme();
            bool upper = Pressed(VK_SHIFT) ^ ((GetKeyState(VK_CAPITAL) & 1) != 0);
            return Apply(engine.Letter(upper ? (char)vk : char.ToLowerInvariant((char)vk)), info);
        }
        switch (vk)
        {
            case VK_BACK: return Apply(engine.Backspace(), info);
            case VK_ESCAPE: return Apply(engine.Escape(), info);
            case VK_RETURN or VK_TAB: return Apply(engine.Boundary(null), info);
        }
        var text = Typed(info);
        if (text is not null) return Apply(engine.Boundary(text), info);
        engine.Reset();   // arrows, Home, Delete, F-keys...: the caret may have moved
        return false;
    }

    /// <summary>Performs the engine's action; returns whether the original key is swallowed.</summary>
    bool Apply(KeyAction action, KBDLLHOOKSTRUCT info)
    {
        if (!action.Injects && !action.Replay) return action.Swallow;
        var inputs = new List<INPUT>();
        for (int i = 0; i < action.Backspaces; i++)
        {
            // Terminals drop key events without a scan code, so give Backspace its real one.
            inputs.Add(Key(VK_BACK, BackspaceScan, 0));
            inputs.Add(Key(VK_BACK, BackspaceScan, KEYEVENTF_KEYUP));
        }
        foreach (var c in action.Text)
        {
            inputs.Add(Key(0, c, KEYEVENTF_UNICODE));
            inputs.Add(Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }
        if (action.Replay)
        {
            // The physical key-up arrives later and passes through on its own.
            uint flags = (info.flags & LLKHF_EXTENDED) != 0 ? KEYEVENTF_EXTENDEDKEY : 0;
            inputs.Add(Key((ushort)info.vkCode, (ushort)info.scanCode, flags));
        }
        var array = inputs.ToArray();
        SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        return action.Swallow;
    }

    static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = 1,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = Tag } },
    };

    static bool Pressed(int vk) => GetAsyncKeyState(vk) < 0;

    /// <summary>The printable character a key types under the foreground layout, or null.</summary>
    static string? Typed(KBDLLHOOKSTRUCT info)
    {
        var state = new byte[256];
        if (Pressed(VK_SHIFT)) state[VK_SHIFT] = 0x80;
        if ((GetKeyState(VK_CAPITAL) & 1) != 0) state[VK_CAPITAL] = 0x01;
        var buffer = new char[8];
        uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        // Flag 4: do not change the keyboard state (no dead-key side effects).
        int n = ToUnicodeEx(info.vkCode, info.scanCode, state, buffer, buffer.Length, 4, GetKeyboardLayout(thread));
        if (n != 1 || char.IsControl(buffer[0])) return null;
        return buffer[0].ToString();
    }

    /// <summary>The foreground app, looked up once per switch rather than per key.</summary>
    public string? CurrentApp => currentApp;

    bool Paused() => currentApp is not null && ExcludedApps.Contains(currentApp);

    static string? ForegroundApp()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch (ArgumentException) { return null; }
    }

    static IntPtr FocusedWindow()
    {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        return GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : IntPtr.Zero;
    }

    /// <summary>Classic Win32 password boxes; browsers' password fields are not visible from here.</summary>
    static bool PasswordField()
    {
        var focus = FocusedWindow();
        if (focus == IntPtr.Zero) return false;
        var name = new StringBuilder(64);
        GetClassName(focus, name, name.Capacity);
        return name.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase)
               && (GetWindowLong(focus, GWL_STYLE) & ES_PASSWORD) != 0;
    }

    /// <summary>Turns a Korean IME's Hangul mode off so it does not compose our keys a second time.</summary>
    static void ForceLatinIme()
    {
        var focus = FocusedWindow();
        var ime = ImmGetDefaultIMEWnd(focus == IntPtr.Zero ? GetForegroundWindow() : focus);
        if (ime == IntPtr.Zero) return;
        const uint abortIfHung = 2;
        if (SendMessageTimeout(ime, WM_IME_CONTROL, IMC_GETCONVERSIONMODE, 0, abortIfHung, 30, out var mode) == 0)
            return;
        if (((long)mode & 1) != 0)
            SendMessageTimeout(ime, WM_IME_CONTROL, IMC_SETCONVERSIONMODE, (IntPtr)((long)mode & ~1L), abortIfHung, 30, out _);
    }

    IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
        {
            engine.Reset();
            KeyHandled?.Invoke();
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    void OnFocus(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        engine.Reset();
        KeyHandled?.Invoke();
        if (eventType != EVENT_SYSTEM_FOREGROUND) return;
        currentApp = ForegroundApp();
        if (currentApp is not null && currentApp != Process.GetCurrentProcess().ProcessName) LastApp = currentApp;
        ForegroundChanged?.Invoke();
    }

    public void Dispose()
    {
        UnhookWindowsHookEx(keyboardHook);
        UnhookWindowsHookEx(mouseHook);
        UnhookWinEvent(foregroundHook);
        UnhookWinEvent(focusHook);
    }
}
