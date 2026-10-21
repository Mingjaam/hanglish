using System.Text;
using static Hanglish.Native;

namespace Hanglish;

/// <summary>Which keys can flip a word, and their names for the settings window.</summary>
static class FlipKeys
{
    public const int Default = VK_HANGUL;

    /// <summary>Presets shown in settings; any other allowed key can be recorded.</summary>
    public static readonly int[] Presets = { VK_HANGUL, VK_RCONTROL, VK_RMENU, VK_CAPITAL };

    /// <summary>Modifiers flip on a lone tap so they keep working in shortcuts; other keys flip on press.</summary>
    public static bool IsModifier(int vk) => vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT or VK_CONTROL or VK_LCONTROL
        or VK_RCONTROL or VK_MENU or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;

    /// <summary>Keys Hanglish needs for typing cannot become the flip key.</summary>
    public static bool Allowed(int vk) =>
        vk is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not (VK_BACK or VK_RETURN or VK_TAB or VK_SPACE or VK_ESCAPE)
        and not (>= 0xBA and <= 0xC0) and not (>= 0xDB and <= 0xDF) and not (>= 0x60 and <= 0x6F);

    public static string Name(int vk) => vk switch
    {
        VK_HANGUL => "한/영 키",
        VK_HANJA => "한자 키",
        VK_RCONTROL => "오른쪽 Ctrl",
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
