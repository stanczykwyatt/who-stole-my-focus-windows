using System.Text.Json;

namespace WhoStoleMyFocus.Core;

/// <summary>What the person chose: how strict to be, whether to notify, and which apps to ignore.</summary>
public sealed record AppSettings
{
    public double Threshold { get; init; } = CauseRules.DefaultThreshold;
    public bool Notifies { get; init; }
    public IReadOnlyList<string> Ignored { get; init; } = SystemApps.IgnoredByDefault.Order(StringComparer.Ordinal).ToList();

    /// <summary>Reads saved settings as untrusted: anything missing or malformed keeps its default.</summary>
    public static AppSettings Load(byte[]? data)
    {
        var settings = new AppSettings();
        if (data is null) return settings;
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return settings;
            if (root.TryGetProperty("threshold", out var threshold) && threshold.ValueKind == JsonValueKind.Number
                && CauseRules.ThresholdChoices.Contains(threshold.GetDouble()))
            {
                settings = settings with { Threshold = threshold.GetDouble() };
            }
            if (root.TryGetProperty("notifies", out var notifies) && notifies.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                settings = settings with { Notifies = notifies.GetBoolean() };
            }
            if (root.TryGetProperty("ignored", out var ignored) && ignored.ValueKind == JsonValueKind.Array)
            {
                var keys = ignored.EnumerateArray()
                    .Where(k => k.ValueKind == JsonValueKind.String)
                    .Select(k => k.GetString()!)
                    .Where(k => k.Length is > 0 and <= 1024 && !k.Any(char.IsControl))
                    .Distinct()
                    .Take(500)
                    .ToList();
                settings = settings with { Ignored = keys };
            }
        }
        catch (JsonException)
        {
        }
        return settings;
    }

    public byte[] Save() => JsonSerializer.SerializeToUtf8Bytes(
        new { threshold = Threshold, notifies = Notifies, ignored = Ignored });
}

/// <summary>
/// Everything the tray menu and the log window show, and the one place events come in. The same as
/// the Mac app's FocusModel, without the user interface.
/// </summary>
public sealed class FocusModel
{
    readonly FocusTracker tracker;
    readonly HashSet<string> ignored;

    public FocusModel(FocusLog? log, AppSettings settings, int? ownPid)
    {
        tracker = new FocusTracker(log, new CauseRules { Threshold = settings.Threshold }, ownPid);
        ignored = [.. settings.Ignored];
        notifies = settings.Notifies;
    }

    /// <summary>Called for each steal by an app that isn't ignored, once it's certain.</summary>
    public event Action<FocusEvent>? Stole;
    /// <summary>Called whenever the log changes, so it can be saved and shown.</summary>
    public event Action? Changed;
    /// <summary>Called whenever a setting changes, so it can be saved right away.</summary>
    public event Action? SettingsChanged;

    public FocusLog Log => tracker.Log;
    /// <summary>Steals since you last opened the menu or the log, for the tray icon.</summary>
    public int UnseenSteals { get; private set; }
    bool notifies;

    public bool Notifies
    {
        get => notifies;
        set
        {
            notifies = value;
            SettingsChanged?.Invoke();
        }
    }
    public double FollowUpDelay => tracker.FollowUpDelay;
    public int? FrontPid => tracker.FrontPid;

    public double Threshold
    {
        get => tracker.Rules.Threshold;
        set
        {
            tracker.Rules.Threshold = value;
            SettingsChanged?.Invoke();
        }
    }

    public AppSettings Settings => new() { Threshold = Threshold, Notifies = Notifies, Ignored = ignored.Order(StringComparer.Ordinal).ToList() };

    /// <summary>Whether this is the app in front right now, so "Still in front" is only said of that one.</summary>
    public bool IsCurrent(FocusEvent e) => tracker.FrontEventId == e.Id;

    // Events from the monitor

    public void Start(int front, AppInfo app, DateTime at, bool isLauncher) => tracker.Start(front, app, at, isLauncher);

    public void AppLaunched(int pid, DateTime at, InputSnapshot input) => tracker.AppLaunched(pid, at, input);

    public void AppLeft(int pid, DateTime at, AppInfo? quit = null) => tracker.AppLeft(pid, at, quit);

    public FocusEvent? AppActivated(Activation activation)
    {
        if (tracker.AppActivated(activation) is not { } e) return null;
        Changed?.Invoke();
        if (e.Verdict == Verdict.Stole && !tracker.IsPending(e.Id)) StoleFocus(e);
        return e;
    }

    public bool IsPending(Guid id) => tracker.IsPending(id);

    public void FinishFollowUp(Guid id, bool keptTyping, bool spaceChanged, DateTime at, Func<int, bool>? hasLeft = null)
    {
        if (tracker.FinishFollowUp(id, keptTyping, spaceChanged, at, hasLeft) is not { } e) return;
        Changed?.Invoke();
        if (e.Verdict == Verdict.Stole) StoleFocus(e);
    }

    public void Finish(DateTime at)
    {
        tracker.Finish(at);
        Changed?.Invoke();
    }

    // What the menu and log show

    public bool IsIgnored(AppInfo app) => ignored.Contains(app.Key);

    public List<FocusEvent> Steals(DateTime? since = null) => Log.Steals(ignored, since);

    public List<Offender> Offenders(DateTime? since = null) => Log.Offenders(ignored, since);

    /// <summary>The newest name seen for each ignored app, for the Ignored Apps menu.</summary>
    public List<(string Key, string Name)> IgnoredApps()
    {
        var names = new Dictionary<string, string>();
        foreach (var e in Log.Events.Where(e => ignored.Contains(e.App.Key))) names[e.App.Key] = Describe.Name(e.App);
        return ignored
            .Select(key => (key, names.GetValueOrDefault(key) ?? SystemApps.IgnoredDefaultNames.GetValueOrDefault(key) ?? key))
            .OrderBy(p => p.Item2, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // Changes from the menu and log

    public void Ignore(AppInfo app)
    {
        if (ignored.Add(app.Key)) SettingsChanged?.Invoke();
    }

    public void StopIgnoring(string key)
    {
        if (ignored.Remove(key)) SettingsChanged?.Invoke();
    }

    public void MarkSeen() => UnseenSteals = 0;

    public void ClearLog()
    {
        tracker.ClearLog();
        UnseenSteals = 0;
        Changed?.Invoke();
    }

    void StoleFocus(FocusEvent e)
    {
        if (IsIgnored(e.App)) return;
        UnseenSteals++;
        Stole?.Invoke(e);
    }
}
