namespace WhoStoleMyFocus.Core.Tests;

/// <summary>Apps and input times used across the tests. Every app here is made up, except the parts of Windows.</summary>
static class Fixture
{
    public static readonly DateTime Start = DateTime.UnixEpoch.AddSeconds(1_800_000_000);

    public static readonly AppInfo Editor = new("Editor", @"C:\Program Files\Editor\Editor.exe", "Editor");
    public static readonly AppInfo Notes = new("Notebook", @"C:\Program Files\Notebook\Notebook.exe");
    public static readonly AppInfo Updater = new("SyncHelper", @"C:\Program Files\CloudSync\bin\SyncHelper.exe", "CloudSync");
    public static readonly AppInfo Meetings = new("Meetings", @"C:\Users\sam\AppData\Local\Meetings\app-2.4.1\Meetings.exe");
    public static readonly AppInfo Launcher = new("Launcher", @"C:\Program Files\Launcher\Launcher.exe");
    public static readonly AppInfo PasswordPrompt = new("CredentialUIBroker", @"C:\Windows\System32\CredentialUIBroker.exe", "Microsoft® Windows® Operating System");
    public static readonly AppInfo LockScreen = new("LockApp", @"C:\Windows\SystemApps\Microsoft.LockApp_cw5n1h2txyewy\LockApp.exe", "Microsoft® Windows® Operating System");
    public static readonly AppInfo Own = new("Who Stole My Focus", @"C:\Users\sam\AppData\Local\Programs\WhoStoleMyFocus\WhoStoleMyFocus.exe");

    static readonly Dictionary<string, int> Pids = new()
    {
        ["Editor"] = 101, ["Notebook"] = 102, ["SyncHelper"] = 103, ["Meetings"] = 104, ["Launcher"] = 105,
        ["CredentialUIBroker"] = 106, ["LockApp"] = 107, ["Who Stole My Focus"] = 999,
    };

    /// <summary>A fixed ID per app name. Apps not listed get one made from their name.</summary>
    public static int Pid(AppInfo app) =>
        Pids.TryGetValue(app.Name, out var pid) ? pid : 2_000 + app.Name.Aggregate(0, (h, c) => (h * 31 + c) % 10_000);

    /// <summary>Nothing for a minute.</summary>
    public static readonly InputSnapshot Idle = new(60, 60, 60, 60);

    public static InputSnapshot Clicked(double seconds) => Idle with { Click = seconds };
    public static InputSnapshot Typed(double seconds) => Idle with { KeyPress = seconds };
    public static InputSnapshot TouchedTrackpad(double seconds) => Idle with { Gesture = seconds };

    /// <summary>Alt+Tab: Tab pressed, then Alt let go just before the switch.</summary>
    public static readonly InputSnapshot AltTab = new(60, 0.3, 0.05, 60);

    public static FocusEvent Event(AppInfo app, double at, AppInfo? from = null, Verdict verdict = Verdict.Stole, Cause cause = Cause.None, double? heldFor = null) =>
        new()
        {
            Date = Start.AddSeconds(at), App = app, Pid = Pids.GetValueOrDefault(app.Name, 1), Previous = from,
            Input = Idle, Cause = cause, Verdict = verdict, HeldFor = heldFor,
        };
}

/// <summary>Drives a tracker through a sequence of switches, one second apart unless told otherwise.</summary>
sealed class Scenario
{
    public FocusTracker Tracker;
    public DateTime Clock;

    public Scenario(AppInfo? front = null, CauseRules? rules = null, int limit = FocusLog.DefaultLimit)
    {
        front ??= Fixture.Editor;
        Tracker = new FocusTracker(new FocusLog(limit: limit), rules ?? new CauseRules(), Fixture.Pid(Fixture.Own));
        Clock = Fixture.Start;
        // Started an hour earlier, so the input times in the fixtures come after tracking began.
        Tracker.Start(Fixture.Pid(front), front, Clock.AddSeconds(-3600));
    }

    public FocusEvent? Activate(
        AppInfo app, double after = 1, InputSnapshot? input = null, bool launcher = false, bool previousHasLeft = false, double? launchedAgo = null)
    {
        Clock = Clock.AddSeconds(after);
        return Tracker.AppActivated(new Activation(
            Fixture.Pid(app), app, Clock, input ?? Fixture.Idle,
            launchedAgo is { } ago ? Clock.AddSeconds(-ago) : null, launcher, previousHasLeft));
    }

    public void Launch(AppInfo app, double after = 1, InputSnapshot? input = null)
    {
        Clock = Clock.AddSeconds(after);
        Tracker.AppLaunched(Fixture.Pid(app), Clock, input ?? Fixture.Idle);
    }

    public void Leave(AppInfo app, double after = 0, bool quitting = false)
    {
        Clock = Clock.AddSeconds(after);
        Tracker.AppLeft(Fixture.Pid(app), Clock, quitting ? app : null);
    }

    public FocusEvent? FinishFollowUp(Guid id, bool keptTyping = false, bool spaceChanged = false, params AppInfo[] left)
    {
        Clock = Clock.AddSeconds(Tracker.FollowUpDelay);
        var pids = left.Select(Fixture.Pid).ToHashSet();
        return Tracker.FinishFollowUp(id, keptTyping, spaceChanged, Clock, pids.Contains);
    }

    public bool IsPending(Guid id) => Tracker.IsPending(id);

    public IReadOnlyList<FocusEvent> Events => Tracker.Log.Events;
}
