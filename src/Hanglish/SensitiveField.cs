using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Accessibility;

namespace Hanglish;

/// <summary>
/// Tracks whether the focused text field takes a password or a verification code, where
/// Hanglish should leave keys alone. Each focus event names the focused object, which is asked
/// through MSAA: browsers mark password inputs "protected", and code fields are known by label.
/// </summary>
static class SensitiveField
{
    // Labels of one-time-code and password-like fields, in Korean and English.
    static readonly Regex Label = new(
        @"비밀번호|암호|패스워드|인증|보안\s*문자|보안\s*코드|확인\s*코드|otp|one[-\s]?time|verif|passcode|password|passwd|\bpin\b|captcha|security\s*code|auth(entication)?\s*code",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    const int StateProtected = 0x20000000, RoleText = 42, BudgetMs = 150;
    static Task<bool> latest = Task.FromResult(false);

    /// <summary>Whether the focused field is a password or code field; waits briefly for a pending check.</summary>
    public static bool Active
    {
        get
        {
            var check = latest;
            return check.Wait(BudgetMs) && check.Result;
        }
    }

    /// <summary>Checks the object a focus event names, off the hook thread (MSAA calls cross processes).</summary>
    public static void Focused(IntPtr hwnd, int idObject, int idChild) =>
        latest = Task.Run(() => Check(hwnd, idObject, idChild));

    static bool Check(IntPtr hwnd, int idObject, int idChild)
    {
        try
        {
            if (Native.AccessibleObjectFromEvent(hwnd, idObject, idChild, out var obj, out var child) != 0
                || obj is not IAccessible element) return false;
            if (element.get_accRole(child) is int role && role != RoleText) return false;
            if (element.get_accState(child) is int state && (state & StateProtected) != 0) return true;
            return new[] { element.get_accName(child), element.get_accDescription(child), element.get_accHelp(child) }
                .Any(text => !string.IsNullOrEmpty(text) && Label.IsMatch(text));
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidCastException or NotImplementedException)
        {
            return false;
        }
    }
}
