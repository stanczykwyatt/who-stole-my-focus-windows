namespace WhoStoleMyFocus.Core;

/// <summary>What one raw input event counts as. Never which key: only what kind of input it was.</summary>
public enum InputKind { Ignored, Click, Key, Modifier, Gesture }

/// <summary>Sorts raw input events into kinds, from the few fields Windows reports with them.</summary>
public static class InputKinds
{
    const ushort KeyBreak = 0x01;
    /// <summary>Shift, Ctrl, Alt, the Windows keys, and their left and right versions.</summary>
    static readonly HashSet<ushort> Modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];

    /// <summary>
    /// A keyboard event. Modifiers count going down and coming up, since letting go of Alt is what
    /// finishes Alt+Tab. Other keys count only going down, like the Mac app's key presses.
    /// </summary>
    /// <param name="virtualKey">The key's virtual-key code, only compared with the modifiers.</param>
    /// <param name="flags">The raw keyboard flags; only "key up" is looked at.</param>
    public static InputKind Keyboard(ushort virtualKey, ushort flags)
    {
        // 0xFF is sent for parts of some key sequences that aren't keys of their own.
        if (virtualKey is 0 or 0xFF) return InputKind.Ignored;
        if (Modifiers.Contains(virtualKey)) return InputKind.Modifier;
        return (flags & KeyBreak) == 0 ? InputKind.Key : InputKind.Ignored;
    }

    /// <summary>A mouse event. Any button going down or up is a click; moving and scrolling aren't.</summary>
    public static InputKind Mouse(ushort buttonFlags) => (buttonFlags & 0x03FF) != 0 ? InputKind.Click : InputKind.Ignored;

    /// <summary>
    /// A touch device, by its HID usage. A touchscreen or pen touch is like a click; a touchpad report
    /// is a gesture, since touching it to move the pointer sends them too, so it only counts with a
    /// desktop change, like the Mac app's trackpad.
    /// </summary>
    public static InputKind Digitizer(ushort usage) => usage switch
    {
        0x02 or 0x04 => InputKind.Click,
        0x05 => InputKind.Gesture,
        _ => InputKind.Ignored,
    };
}

/// <summary>
/// When each kind of input last happened, in milliseconds on one clock, so the input times for any
/// moment can be told: now, or when an app launched up to a minute ago. Keeps the last minute of
/// times per kind and nothing else.
/// </summary>
public sealed class InputTimes
{
    /// <summary>As long as an app can take to open and still count as opened by you.</summary>
    public const long KeptFor = 61_000;
    const int MostKept = 4_096;
    readonly Dictionary<InputKind, Queue<long>> times = new()
    {
        [InputKind.Click] = new(), [InputKind.Key] = new(), [InputKind.Modifier] = new(), [InputKind.Gesture] = new(),
    };

    public void Record(InputKind kind, long milliseconds)
    {
        if (!times.TryGetValue(kind, out var queue)) return;
        queue.Enqueue(milliseconds);
        // Older than a minute, or past the cap, isn't needed, but the newest is always kept.
        while (queue.Count > 1 && (queue.Count > MostKept || queue.Peek() < milliseconds - KeptFor)) queue.Dequeue();
    }

    /// <summary>Input times as of <paramref name="milliseconds"/>, counting only input up to then.</summary>
    public InputSnapshot At(long milliseconds, bool commandKeysHeld = false)
    {
        double Since(InputKind kind)
        {
            long? latest = null;
            foreach (var t in times[kind]) if (t <= milliseconds && (latest is null || t > latest)) latest = t;
            return latest is { } last ? (milliseconds - last) / 1000.0 : InputSnapshot.Longest;
        }
        return new InputSnapshot(Since(InputKind.Click), Since(InputKind.Key), Since(InputKind.Modifier), Since(InputKind.Gesture), commandKeysHeld).Sanitized;
    }
}

/// <summary>Names for apps, from what their executables say about themselves.</summary>
public static class AppNames
{
    /// <summary>
    /// The name to show: the file description if it has one ("Microsoft Teams"), else the executable's
    /// name without ".exe". Text from other apps is cleaned before it's shown.
    /// </summary>
    public static AppInfo From(string path, string? description, string? product)
    {
        var file = path.Replace('/', '\\');
        file = file[(file.LastIndexOf('\\') + 1)..];
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file = file[..^4];
        var name = Clean(description);
        return new AppInfo(string.IsNullOrEmpty(name) ? Clean(file) ?? file : name, path, Clean(product));
    }

    /// <summary>No control characters or text-direction marks, at most 80 characters, trimmed.</summary>
    public static string? Clean(string? text)
    {
        if (text is null) return null;
        var kept = new string(text.Where(c => !char.IsControl(c) && !IsFormatting(c)).ToArray()).Trim();
        if (kept.Length > 80) kept = kept[..80].TrimEnd();
        return kept.Length == 0 ? null : kept;
    }

    static bool IsFormatting(char c) => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format;
}
