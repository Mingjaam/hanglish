using System.Text;
using static Hanglish.Native;

namespace Hanglish;

/// <summary>Which keys can flip a word or toggle the preview, and their names for the settings window.</summary>
static class FlipKeys
{
    /// <summary>
    /// Right Ctrl: habit never presses it alone, unlike 한/영 (pressed before typing Korean). Korean
    /// keyboards that put 한자 on that spot send VK_HANJA instead, so that key counts as the same.
    /// </summary>
    public const int Default = VK_RCONTROL;

    /// <summary>Presets shown in settings; any other allowed key can be recorded.</summary>
    public static readonly int[] Presets = { VK_RCONTROL, VK_HANGUL, VK_RMENU, VK_CAPITAL };

    /// <summary>The preview toggle key: 한/영, the key people already press to change what they see.</summary>
    public const int PreviewDefault = VK_HANGUL;

    /// <summary>No key (the preview toggle can be turned off).</summary>
    public const int None = 0;

    public static readonly int[] PreviewPresets = { VK_HANGUL, VK_CAPITAL, VK_RMENU, None };

    /// <summary>Whether two settings would claim the same physical key.</summary>
    public static bool Same(int a, int b) => a != None && b != None && (Matches(a, b) || Matches(b, a));

    /// <summary>Whether a key press is the configured flip key (한자 stands in for right Ctrl).</summary>
    public static bool Matches(int vk, int flipKey) => vk == flipKey || flipKey == VK_RCONTROL && vk == VK_HANJA;

    /// <summary>Modifiers flip on a lone tap so they keep working in shortcuts; other keys flip on press.</summary>
    public static bool IsModifier(int vk) => vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT or VK_CONTROL or VK_LCONTROL
        or VK_RCONTROL or VK_MENU or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;

    /// <summary>Keys Hanglish needs for typing cannot become the flip key.</summary>
    public static bool Allowed(int vk) =>
        vk is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not (VK_BACK or VK_RETURN or VK_TAB or VK_SPACE or VK_ESCAPE)
        and not (>= 0xBA and <= 0xC0) and not (>= 0xDB and <= 0xDF) and not (>= 0x60 and <= 0x6F);

    public static string Name(int vk) => vk switch
    {
        None => "사용 안 함",
        VK_HANGUL => "한/영 키",
        VK_HANJA => "한자 키",
        VK_RCONTROL => "오른쪽 Ctrl (한자 키)",
        VK_LCONTROL => "왼쪽 Ctrl",
        VK_RMENU => "오른쪽 Alt",
        VK_LMENU => "왼쪽 Alt",
        VK_RSHIFT => "오른쪽 Shift",
        VK_LSHIFT => "왼쪽 Shift",
        VK_CAPITAL => "Caps Lock",
        VK_LWIN => "왼쪽 Windows 키",
        VK_RWIN => "오른쪽 Windows 키",
        0x5D => "메뉴 키",
        0x13 => "Pause",
        0x91 => "Scroll Lock",
        0x2D => "Insert",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        _ => KeyName(vk),
    };

    static string KeyName(int vk)
    {
        uint scan = MapVirtualKey((uint)vk, 0);
        var name = new StringBuilder(64);
        return scan != 0 && GetKeyNameText((int)(scan << 16), name, name.Capacity) > 0 ? name.ToString() : $"키 {vk:X2}";
    }
}
