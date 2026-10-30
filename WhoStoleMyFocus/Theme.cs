using Microsoft.Win32;

namespace WhoStoleMyFocus;

/// <summary>Colors that follow Windows' light or dark app mode, with the same ice-blue accent as the Mac app.</summary>
internal sealed class Theme
{
    Theme(bool isDark)
    {
        IsDark = isDark;
        Background = isDark ? Color.FromArgb(0x20, 0x22, 0x26) : Color.FromArgb(0xF7, 0xF8, 0xFA);
        Raised = isDark ? Color.FromArgb(0x2A, 0x2D, 0x33) : Color.White;
        Border = isDark ? Color.FromArgb(0x3C, 0x40, 0x47) : Color.FromArgb(0xDC, 0xDF, 0xE4);
        Text = isDark ? Color.FromArgb(0xF2, 0xF4, 0xF7) : Color.FromArgb(0x1A, 0x1C, 0x20);
        SecondaryText = isDark ? Color.FromArgb(0xA8, 0xAE, 0xB8) : Color.FromArgb(0x5C, 0x62, 0x6B);
        Accent = isDark ? Color.FromArgb(0x9E, 0xD1, 0xFF) : Color.FromArgb(0x1F, 0x5F, 0xA8);
        Steal = isDark ? Color.FromArgb(0xFF, 0x9F, 0x8A) : Color.FromArgb(0xB4, 0x2B, 0x12);
        Selection = isDark ? Color.FromArgb(0x34, 0x3A, 0x44) : Color.FromArgb(0xE2, 0xEC, 0xF8);
    }

    public bool IsDark { get; }
    public Color Background { get; }
    public Color Raised { get; }
    public Color Border { get; }
    public Color Text { get; }
    public Color SecondaryText { get; }
    public Color Accent { get; }
    /// <summary>For "Stole focus", so steals stand out in the full log.</summary>
    public Color Steal { get; }
    public Color Selection { get; }

    /// <summary>The theme apps are using right now. Read fresh each time, so a change takes effect at once.</summary>
    public static Theme Current => new(AppsUseDarkMode());

    static bool AppsUseDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
