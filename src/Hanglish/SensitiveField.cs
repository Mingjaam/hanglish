using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace Hanglish;

/// <summary>
/// Tracks whether the focused text field takes a password or a verification code, where
/// Hanglish should leave keys alone. UI Automation exposes password inputs in browsers too,
/// which the Win32 ES_PASSWORD style does not; code fields are recognized by their label.
/// </summary>
static class SensitiveField
{
    // Labels of one-time-code and password-like fields, in Korean and English.
    static readonly Regex Label = new(
        @"비밀번호|암호|패스워드|인증|보안\s*문자|보안\s*코드|확인\s*코드|otp|one[-\s]?time|verif|passcode|password|passwd|\bpin\b|captcha|security\s*code|auth(entication)?\s*code",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    const int ReuseMs = 1500, BudgetMs = 150;
    static readonly object gate = new();
    static bool cached, stale = true;
    static long checkedAt;

    /// <summary>
    /// Whether the focused field is a password or code field. Checked when a word starts, since
    /// UI Automation reports a new focus only a moment after the focus event; a recent answer is
    /// reused while typing goes on in the same field.
    /// </summary>
    public static bool Active
    {
        get
        {
            lock (gate)
            {
                if (!stale && Environment.TickCount64 - checkedAt < ReuseMs) return cached;
            }
            var check = Task.Run(Check);
            bool result = check.Wait(BudgetMs) && check.Result;
            lock (gate)
            {
                cached = result;
                stale = false;
                checkedAt = Environment.TickCount64;
            }
            return result;
        }
    }

    /// <summary>Focus moved: the next word start checks the field again.</summary>
    public static void Refresh()
    {
        lock (gate) stale = true;
    }

    static bool Check()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return false;
            var current = element.Current;
            if (current.IsPassword) return true;
            if (current.ControlType != ControlType.Edit && current.ControlType != ControlType.Document) return false;
            var labeledBy = current.LabeledBy?.Current.Name ?? "";
            return new[] { current.Name, current.AutomationId, current.HelpText, labeledBy }
                .Any(text => !string.IsNullOrEmpty(text) && Label.IsMatch(text));
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException
                                      or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
