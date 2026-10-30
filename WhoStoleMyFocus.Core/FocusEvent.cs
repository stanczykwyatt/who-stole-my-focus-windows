using System.Text.Json.Serialization;

namespace WhoStoleMyFocus.Core;

/// <summary>What happened when an app came to the front.</summary>
public enum Verdict
{
    /// <summary>It came forward on its own: no click, shortcut or launcher just before, or you kept typing straight through it.</summary>
    Stole,
    /// <summary>Right after a click, shortcut or launcher.</summary>
    Switched,
    /// <summary>Its first trip to the front after you opened it.</summary>
    Opened,
    /// <summary>Focus went back to it after the app in front quit, was minimized or was a system prompt.</summary>
    Returned,
    /// <summary>
    /// Right after a plain keypress, and you stopped typing. Maybe you pressed Enter on something that
    /// opens it, maybe not, so it isn't counted as a steal.
    /// </summary>
    Unsure,
}

/// <summary>One app coming to the front.</summary>
public sealed record FocusEvent
{
    [JsonRequired]
    public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired]
    public DateTime Date { get; init; }
    public required AppInfo App { get; init; }
    public int Pid { get; init; }
    /// <summary>The app that was in front before.</summary>
    public AppInfo? Previous { get; init; }
    public InputSnapshot Input { get; init; }
    [JsonRequired]
    public Cause Cause { get; init; }
    [JsonRequired]
    public Verdict Verdict { get; init; }
    /// <summary>How long ago the app launched, when that was in the last ten minutes.</summary>
    public double? SecondsSinceLaunch { get; init; }
    /// <summary>How long it stayed in front. Null while it's still there.</summary>
    public double? HeldFor { get; init; }
    /// <summary>What came to the front after it.</summary>
    public AppInfo? Next { get; init; }
    public Verdict? NextVerdict { get; init; }

    /// <summary>Whether it stayed in front only until you went straight back to the app it took focus from.</summary>
    [JsonIgnore]
    public bool YouWentBack => Previous is not null && Next is not null && Next.Key == Previous.Key && NextVerdict == Verdict.Switched;
}
