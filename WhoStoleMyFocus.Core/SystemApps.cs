namespace WhoStoleMyFocus.Core;

/// <summary>Parts of Windows that come to the front by themselves, described in words people recognize.</summary>
public static class SystemApps
{
    /// <summary>
    /// File names checked against Windows 11 24H2 and Windows 10 22H2, for executables in the Windows
    /// folder. Most of them have names like "CredentialUIBroker" that nobody would recognize.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["consent.exe"] = "Windows asking whether to let an app make changes",
        ["credentialuibroker.exe"] = "Windows asking for your password or PIN",
        ["lockapp.exe"] = "the lock screen",
        ["logonui.exe"] = "the sign-in screen",
        ["smartscreen.exe"] = "a SmartScreen warning about something downloaded from the internet",
        ["werfault.exe"] = "a crash report",
        ["msiexec.exe"] = "an installer finishing up",
        ["pickerhost.exe"] = "a file or app picker",
        ["musnotificationux.exe"] = "Windows Update asking to restart",
        ["securityhealthhost.exe"] = "a Windows Security alert",
        ["startmenuexperiencehost.exe"] = "the Start menu",
        ["searchhost.exe"] = "Windows Search",
        ["searchapp.exe"] = "Windows Search",
        ["shellexperiencehost.exe"] = "the notification center, calendar or quick settings",
        ["shellhost.exe"] = "quick settings",
        ["textinputhost.exe"] = "the emoji panel or touch keyboard",
        ["tabtip.exe"] = "the touch keyboard",
        ["screenclippinghost.exe"] = "the screenshot toolbar",
        ["openwith.exe"] = "the Open With dialog",
    };

    /// <summary>
    /// Parts of Windows that come forward because you asked for them, from a key, a click on the
    /// taskbar or a gesture, so they're never counted as stealing focus.
    /// </summary>
    public static readonly IReadOnlySet<string> OnRequest = new HashSet<string>
    {
        "startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe", "shellexperiencehost.exe", "shellhost.exe",
        "textinputhost.exe", "tabtip.exe", "screenclippinghost.exe", "openwith.exe",
    };

    /// <summary>
    /// Where typing a few letters and pressing Enter opens another app: Start and Search, and launchers
    /// people install. Matched by file name anywhere, since launchers live wherever they're installed.
    /// </summary>
    public static readonly IReadOnlySet<string> Launchers = new HashSet<string>
    {
        "startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe",
        "powertoys.powerlauncher.exe", "flow.launcher.exe", "wox.exe", "keypirinha-x64.exe", "keypirinha.exe", "listary.exe",
        "fluentsearch.exe", "everything.exe", "ueli.exe",
    };

    /// <summary>Whether an app lives in the Windows folder, like "C:\Windows\System32\consent.exe".</summary>
    public static bool IsWindowsPart(AppInfo app)
    {
        var path = app.Path?.Replace('/', '\\');
        return path is { Length: > 11 } && char.IsAsciiLetter(path[0]) && path[1..].StartsWith(@":\Windows\", StringComparison.OrdinalIgnoreCase)
            && !path.Contains(@"\..", StringComparison.Ordinal);
    }

    public static string? Description(AppInfo app) =>
        IsWindowsPart(app) && Descriptions.TryGetValue(app.FileName, out var description) ? description : null;

    public static bool OpensOnRequest(AppInfo app) => IsWindowsPart(app) && OnRequest.Contains(app.FileName);

    public static bool IsLauncher(AppInfo app) => Launchers.Contains(app.FileName) && (IsWindowsPart(app) || !OnRequest.Contains(app.FileName));

    /// <summary>System screens that interrupt whatever you're doing and then hand focus back to it.</summary>
    public static bool IsInterruption(AppInfo app) => Description(app) is not null;

    /// <summary>Locking the screen isn't an app misbehaving, so these aren't counted unless you choose to.</summary>
    public static readonly IReadOnlyDictionary<string, string> IgnoredDefaultNames = new Dictionary<string, string>
    {
        ["system:lockapp.exe"] = "Lock screen (LockApp)",
        ["system:logonui.exe"] = "Sign-in screen (LogonUI)",
    };

    public static IReadOnlySet<string> IgnoredByDefault => IgnoredDefaultNames.Keys.ToHashSet();
}
