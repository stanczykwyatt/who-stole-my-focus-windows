using System.Globalization;

namespace WhoStoleMyFocus.Core;

/// <summary>The words the tray menu, the log window, notifications and the CSV use for events.</summary>
public static class Describe
{
    public static string VerdictName(Verdict verdict) => verdict switch
    {
        Verdict.Stole => "Stole focus",
        Verdict.Switched => "You switched",
        Verdict.Opened => "You opened it",
        Verdict.Returned => "Came back",
        _ => "Not sure",
    };

    /// <summary>The app's name, plus the product it belongs to when that's different, so helpers make sense.</summary>
    public static string Name(AppInfo app) => app.ContainingAppName is { } product ? $"{app.Name} ({product})" : app.Name;

    /// <summary>One line saying what happened, like "Zoom stole focus from Visual Studio Code".</summary>
    public static string Headline(FocusEvent e)
    {
        var app = Name(e.App);
        return (e.Verdict, e.Cause) switch
        {
            (Verdict.Stole, _) => e.Previous is { } previous ? $"{app} stole focus from {Name(previous)}" : $"{app} stole focus",
            (Verdict.Switched, _) => $"You switched to {app}",
            (Verdict.Opened, Cause.Relaunch) => $"{app} came back after relaunching",
            (Verdict.Opened, _) => $"You opened {app}",
            (Verdict.Returned, _) => $"{app} came back to the front",
            (Verdict.Unsure, Cause.Keypress) => $"{app} came forward right after a keypress",
            (Verdict.Unsure, Cause.Startup) => $"{app} came forward before you'd clicked or typed",
            _ => $"{app} came forward",
        };
    }

    /// <summary>Why it was judged that way.</summary>
    public static string Reason(FocusEvent e)
    {
        switch (e.Verdict, e.Cause)
        {
            case (Verdict.Opened, Cause.Relaunch):
                return "Started again soon after it quit, as apps do when they update themselves";
            case (Verdict.Opened, _):
                var wait = e.SecondsSinceLaunch is { } launched ? $" {Duration(launched)} after" : " after";
                return $"Came to the front{wait} you opened it";
            case (Verdict.Returned, _):
                return e.Previous is { } previous ? $"Focus went back to it after {Name(previous)}" : "Focus went back to it";
        }
        switch (e.Cause)
        {
            case Cause.Click: return $"{Duration(e.Input.Click)} after a click";
            case Cause.Shortcut: return "Right after a keyboard shortcut";
            case Cause.Gesture: return "Right after a touchpad swipe to another desktop";
            case Cause.Launcher: return e.Previous is { } launcher ? $"After you typed in {Name(launcher)}" : "After you typed in a launcher";
            case Cause.SystemUI: return "Part of Windows that opens when you ask for it, so it's never counted";
            case Cause.Keypress: return $"{Duration(e.Input.KeyPress)} after a keypress, then you stopped typing. If you pressed Enter to open it, that was you.";
            case Cause.Typing: return "While you were typing: it came forward between your keystrokes";
            case Cause.Startup: return "Before your first click or keypress since Who Stole My Focus started, like at sign-in";
            case Cause.Relaunch: return "Started again soon after it quit";
            default:
                var line = $"No click or keypress for {Duration(e.Input.Latest)}";
                if (e.SecondsSinceLaunch is { } age && age <= 60) line += $", {Duration(age)} after it launched by itself";
                return line;
        }
    }

    /// <summary>A few words of the reason, for a table cell: "No input for 41 s", "Between your keystrokes".</summary>
    public static string ShortReason(FocusEvent e)
    {
        switch (e.Verdict, e.Cause)
        {
            case (Verdict.Opened, Cause.Relaunch): return "Relaunched after quitting";
            case (Verdict.Opened, _): return e.SecondsSinceLaunch is { } launched ? $"{Duration(launched)} after you opened it" : "After you opened it";
            case (Verdict.Returned, _): return e.Previous is { } previous ? $"Back after {Name(previous)}" : "Focus went back to it";
        }
        return e.Cause switch
        {
            Cause.Click => $"{Duration(e.Input.Click)} after a click",
            Cause.Shortcut => "After a shortcut",
            Cause.Gesture => "After a swipe",
            Cause.Launcher => e.Previous is { } launcher ? $"After typing in {Name(launcher)}" : "After typing in a launcher",
            Cause.SystemUI => "Opened from Windows",
            Cause.Keypress => $"{Duration(e.Input.KeyPress)} after a keypress",
            Cause.Typing => "Between your keystrokes",
            Cause.Startup => "Before your first input",
            Cause.Relaunch => "Relaunched after quitting",
            _ => $"No input for {Duration(e.Input.Latest)}",
        };
    }

    /// <summary>What happened next, like "In front for 2 s, then you went back to Word".</summary>
    /// <param name="isCurrent">Whether it's the app in front right now. One that never got a time, because the app stopped unexpectedly, isn't.</param>
    public static string Aftermath(FocusEvent e, bool isCurrent = true)
    {
        if (e.HeldFor is not { } held) return isCurrent ? "Still in front" : "In front until Who Stole My Focus stopped watching";
        var stay = $"In front for {Duration(held)}";
        if (e.YouWentBack && e.Previous is { } previous) return $"{stay}, then you went back to {Name(previous)}";
        if (e.Next is { } next && e.NextVerdict == Verdict.Returned) return $"{stay}, until {Name(next)} came back";
        return stay;
    }

    /// <summary>Plain words for parts of Windows with cryptic names, like "CredentialUIBroker".</summary>
    public static string? SystemNote(AppInfo app) => SystemApps.Description(app) is { } description ? $"{app.Name} is {description}." : null;

    /// <summary>A path with your user folder shortened to "~", so shared details don't include your username.</summary>
    public static string ShortPath(string path, string? home)
    {
        if (string.IsNullOrEmpty(home)) return path;
        var folder = home.TrimEnd('\\', '/');
        if (path.Equals(folder, StringComparison.OrdinalIgnoreCase)) return "~";
        foreach (var separator in new[] { '\\', '/' })
        {
            if (path.StartsWith(folder + separator, StringComparison.OrdinalIgnoreCase)) return "~" + path[folder.Length..];
        }
        return path;
    }

    /// <summary>Everything about one event, for pasting into a bug report to the app's developer.</summary>
    public static string Details(FocusEvent e, string date, string? home = null, bool isCurrent = true)
    {
        var lines = new List<string> { Headline(e), date, Reason(e), Aftermath(e, isCurrent) };
        if (SystemNote(e.App) is { } note) lines.Add(note);
        lines.Add($"App: {e.App.Name}" + (e.App.ContainingAppName is { } product ? $" ({product})" : ""));
        if (e.App.Path is { } path) lines.Add($"Path: {ShortPath(path, home)}");
        return string.Join("\n", lines);
    }

    /// <summary>A short duration: "0.4 s", "41 s", "3 min 5 s", "2 h 10 min", "3 days".</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds)) return "a long time";
        // Clamped to ten thousand days, so no value can overflow.
        seconds = Math.Min(Math.Max(0, seconds), 864_000_000);
        if (seconds < 9.95)
        {
            var tenths = Math.Round(seconds * 10, MidpointRounding.AwayFromZero) / 10;
            return tenths == Math.Round(tenths) ? $"{(int)tenths} s" : tenths.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }
        var whole = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
        if (whole < 60) return $"{whole} s";
        if (whole < 3600)
        {
            var rest = whole % 60;
            return rest == 0 ? $"{whole / 60} min" : $"{whole / 60} min {rest} s";
        }
        if (whole < 86_400)
        {
            var minutes = whole % 3600 / 60;
            return minutes == 0 ? $"{whole / 3600} h" : $"{whole / 3600} h {minutes} min";
        }
        var days = whole / 86_400;
        return days == 1 ? "1 day" : $"{days} days";
    }

    /// <summary>When something happened, relative to now: "just now", "5 min ago", "yesterday at 3:41 PM".</summary>
    public static string Relative(DateTime date, DateTime now, TimeZoneInfo? zone = null, CultureInfo? culture = null)
    {
        if (Recent(date, now) is { } recent) return recent;
        var parts = new DateParts(date, now, zone ?? TimeZoneInfo.Local, culture ?? CultureInfo.CurrentCulture);
        if (parts.IsToday) return $"at {parts.Time}";
        if (parts.IsYesterday) return $"yesterday at {parts.Time}";
        return $"{parts.Day} at {parts.Time}";
    }

    /// <summary>The same, shorter, for a table column: "5 min ago", "3:41 PM", "Yesterday 3:41 PM", "Oct 27, 3:41 PM".</summary>
    public static string CompactTime(DateTime date, DateTime now, TimeZoneInfo? zone = null, CultureInfo? culture = null)
    {
        if (Recent(date, now) is { } recent) return recent;
        var parts = new DateParts(date, now, zone ?? TimeZoneInfo.Local, culture ?? CultureInfo.CurrentCulture);
        if (parts.IsToday) return parts.Time;
        if (parts.IsYesterday) return $"Yesterday {parts.Time}";
        return $"{parts.Day}, {parts.Time}";
    }

    static string? Recent(DateTime date, DateTime now)
    {
        var elapsed = (now - date).TotalSeconds;
        if (elapsed < 10) return "just now";
        if (elapsed < 60) return $"{(int)elapsed} s ago";
        if (elapsed < 3600) return $"{(int)(elapsed / 60)} min ago";
        return null;
    }

    readonly struct DateParts
    {
        public readonly string Time;
        public readonly string Day;
        public readonly bool IsToday;
        public readonly bool IsYesterday;

        public DateParts(DateTime date, DateTime now, TimeZoneInfo zone, CultureInfo culture)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(date, DateTimeKind.Utc), zone);
            var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), zone);
            Time = local.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
            Day = local.ToString(local.Year == today.Year ? "MMM d" : "MMM d, yyyy", culture);
            IsToday = local.Date == today.Date;
            IsYesterday = local.Date == today.Date.AddDays(-1);
        }
    }
}
