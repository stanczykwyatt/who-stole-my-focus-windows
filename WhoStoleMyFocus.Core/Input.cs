using System.Text.Json.Serialization;

namespace WhoStoleMyFocus.Core;

/// <summary>
/// How long ago you last did each thing that can bring an app to the front, in seconds. Times only:
/// never which key or where you clicked.
/// </summary>
/// <param name="Click">Any mouse button, pressed or released. Covers windows, the taskbar, links and notifications.</param>
/// <param name="KeyPress">Any key other than a modifier.</param>
/// <param name="ModifierChange">Ctrl, Alt, Shift or the Windows key pressed or released.</param>
/// <param name="Gesture">Touchpad gestures. Windows reports these as the shortcuts they stand for, so this stays at <see cref="Longest"/> and is kept for the same rules as the Mac app.</param>
/// <param name="CommandKeysHeld">Whether Ctrl, Alt or the Windows key is held down right now.</param>
public readonly record struct InputSnapshot(double Click, double KeyPress, double ModifierChange, double Gesture, bool CommandKeysHeld = false)
{
    /// <summary>Ten days. Anything longer is treated as never.</summary>
    public const double Longest = 864_000;

    /// <summary>Nothing at all for as long as can be told.</summary>
    public static readonly InputSnapshot None = new(Longest, Longest, Longest, Longest);

    /// <summary>Seconds since the last click, keypress or modifier key: anything that could switch apps.</summary>
    [JsonIgnore]
    public double Latest => Math.Min(Click, Math.Min(KeyPress, ModifierChange));

    /// <summary>Seconds since you last clicked or typed, the two ways of actually using an app.</summary>
    [JsonIgnore]
    public double Interaction => Math.Min(Click, KeyPress);

    /// <summary>Clamped to 0 to ten days, so odd values can't break the log or its JSON.</summary>
    [JsonIgnore]
    public InputSnapshot Sanitized
    {
        get
        {
            static double Clamp(double value) => double.IsNaN(value) ? Longest : Math.Min(Math.Max(value, 0), Longest);
            return new InputSnapshot(Clamp(Click), Clamp(KeyPress), Clamp(ModifierChange), Clamp(Gesture), CommandKeysHeld);
        }
    }
}

/// <summary>What most likely brought an app to the front.</summary>
public enum Cause
{
    /// <summary>A click a moment before.</summary>
    Click,
    /// <summary>A keyboard shortcut, like Alt+Tab or Win+1.</summary>
    Shortcut,
    /// <summary>A touchpad swipe to another desktop. Only decided after the switch.</summary>
    Gesture,
    /// <summary>A key pressed in a launcher like Start, Search or PowerToys Run, usually Enter.</summary>
    Launcher,
    /// <summary>
    /// A plain keypress a moment before. It could be Enter on something that opens an app, or the app
    /// jumped in while you were typing. Settled by watching whether you keep typing.
    /// </summary>
    Keypress,
    /// <summary>You were typing when it came forward and kept typing after, so you didn't switch on purpose.</summary>
    Typing,
    /// <summary>No click or keypress for longer than the threshold.</summary>
    None,
    /// <summary>Part of Windows you open yourself, like Start, Search or the notification center.</summary>
    SystemUI,
    /// <summary>The same app starting again shortly after it quit, as apps do when they update themselves.</summary>
    Relaunch,
    /// <summary>Before your first click or keypress since tracking started, like at sign-in.</summary>
    Startup,
}

/// <summary>Turns input times into a cause.</summary>
public sealed record CauseRules
{
    /// <summary>How long after a click or keypress a switch still counts as yours. Links and busy apps can take a moment.</summary>
    public double Threshold { get; set; } = DefaultThreshold;
    /// <summary>Alt+Tab switches the moment you let go of Alt, so a modifier only counts if it changed just before.</summary>
    public double ModifierWindow { get; init; } = 0.5;
    /// <summary>
    /// A modifier let go this recently is almost certainly what made the switch. Earlier than this, it
    /// may be Shift released while typing.
    /// </summary>
    public double InstantModifierWindow { get; init; } = 0.15;
    /// <summary>How recent a touchpad touch has to be to explain a switch to another desktop.</summary>
    public double GestureWindow { get; init; } = 1;

    public static readonly double[] ThresholdChoices = [0.5, 1, 1.5, 2, 3];
    public const double DefaultThreshold = 1.5;

    /// <param name="input">The input times.</param>
    /// <param name="frontIsLauncher">Whether the app in front before the switch was a launcher, where typing and pressing Enter opens other apps.</param>
    public Cause CauseOf(InputSnapshot input, bool frontIsLauncher)
    {
        // A click only explains the switch if you haven't typed since. Otherwise you were typing in the window you clicked.
        if (input.Click <= Threshold && input.Click <= input.KeyPress) return Cause.Click;
        // Letting go of a modifier after the last key is how Alt+Tab and most shortcuts finish.
        if (input.ModifierChange <= ModifierWindow && input.ModifierChange <= input.KeyPress) return Cause.Shortcut;
        if (input.KeyPress <= Threshold)
        {
            if (input.CommandKeysHeld) return Cause.Shortcut;
            if (frontIsLauncher) return Cause.Launcher;
            return Cause.Keypress;
        }
        return Cause.None;
    }
}

/// <summary>The second look a moment after a switch: whether you kept typing straight through it.</summary>
public static class FollowUp
{
    /// <summary>
    /// A key pressed after the app came forward means you were still typing, unless it was a shortcut
    /// aimed at the new app, like Ctrl+T. A check that ran late can't tell either way.
    /// </summary>
    /// <param name="elapsed">Seconds since the app came forward.</param>
    /// <param name="delay">How long the follow-up was meant to wait.</param>
    /// <param name="input">Input times now.</param>
    public static bool KeptTyping(double elapsed, double delay, InputSnapshot input)
    {
        var onTime = elapsed <= delay + 0.6;
        var shortcut = input.CommandKeysHeld || input.ModifierChange < input.KeyPress;
        return onTime && input.KeyPress < elapsed && !shortcut;
    }
}
