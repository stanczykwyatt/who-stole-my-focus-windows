namespace WhoStoleMyFocus.Core;

/// <summary>An app coming to the front, as Windows reported it.</summary>
/// <param name="IsLauncher">Start, Search and launchers like PowerToys Run, where typing a few letters and pressing Enter opens other apps.</param>
/// <param name="PreviousHasLeft">Whether the app that was in front has quit or been minimized by now. Windows doesn't always say so before it brings the next app forward.</param>
public sealed record Activation(
    int Pid, AppInfo App, DateTime Date, InputSnapshot Input, DateTime? LaunchDate = null,
    bool IsLauncher = false, bool PreviousHasLeft = false);

/// <summary>
/// Decides why each app came to the front and keeps the log. A port of the Mac app's FocusTracker,
/// with the same rules, names and order, tested with the same scenarios.
/// </summary>
/// <remarks>
/// It remembers the last few apps that were in front, so focus returning to the app you were using
/// after an interruption isn't blamed on that app. Anything that could still turn out differently
/// waits for a follow-up a moment later before anyone is told about it.
/// </remarks>
public sealed class FocusTracker
{
    public CauseRules Rules { get; set; }
    /// <summary>How long after you open an app its first trip to the front still counts as you opening it. Big apps can take a while to start.</summary>
    public double OpeningTime { get; set; } = 60;
    /// <summary>How close to an app's arrival the app in front being minimized or quitting counts as Windows handing focus over.</summary>
    public double HandoffTime { get; set; } = 2;
    /// <summary>How long to wait before the follow-up. Short enough that a key pressed by then was almost certainly typed before you'd noticed the switch.</summary>
    public double FollowUpDelay { get; set; } = 0.4;
    /// <summary>This app's own process. Its windows can be in front, but they're never logged.</summary>
    public int? OwnPid { get; }
    public FocusLog Log { get; }

    sealed class Front
    {
        public int Pid;
        public required AppInfo App;
        public DateTime Since;
        public bool IsLauncher;
        /// <summary>Null for the app that was in front when tracking started, and for this app.</summary>
        public Verdict? Verdict;
        public Guid? EventId;
        /// <summary>Whether you clicked or typed while it was in front.</summary>
        public bool Touched;
        /// <summary>For an app that came back: whether it came back to being the app you were using.</summary>
        public bool BackToYours;
    }

    enum LaunchOrigin
    {
        /// <summary>A click, shortcut or launcher just before.</summary>
        You,
        /// <summary>A plain keypress just before: Enter on a command that opens it, or just typing.</summary>
        Keypress,
        Unknown,
    }

    readonly record struct Launch(DateTime Date, LaunchOrigin Origin);

    /// <param name="TypingDecides">Whether typing straight through the switch would make it a steal.</param>
    readonly record struct Pending(int? PreviousPid, bool TypingDecides);

    /// <summary>Most recent first: history[0] is the app in front.</summary>
    readonly List<Front> history = [];
    readonly Dictionary<Guid, Pending> pending = [];
    readonly Dictionary<int, Launch> launches = [];
    readonly Dictionary<int, DateTime> departures = [];
    /// <summary>Apps that quit recently, by key, to recognize them coming back after an update.</summary>
    readonly Dictionary<string, DateTime> quits = [];
    /// <summary>When tracking last started. Until your first click or keypress after that, apps sorting themselves out, like at sign-in, aren't stealing from you.</summary>
    DateTime? trackingSince;
    const int HistoryLimit = 12;

    public FocusTracker(FocusLog? log = null, CauseRules? rules = null, int? ownPid = null)
    {
        Log = log ?? new FocusLog();
        Rules = rules ?? new CauseRules();
        OwnPid = ownPid;
    }

    /// <summary>The app in front right now, if known.</summary>
    public int? FrontPid => history.Count > 0 ? history[0].Pid : null;

    /// <summary>The event for the app in front right now, if it was logged.</summary>
    public Guid? FrontEventId => history.Count > 0 ? history[0].EventId : null;

    /// <summary>Starts over from the app that's in front now, without logging it. Used at startup and when you sign back in.</summary>
    public void Start(int front, AppInfo app, DateTime at, bool isLauncher = false)
    {
        Finish(at);
        history.Clear();
        history.Add(new Front { Pid = front, App = app, Since = at, IsLauncher = isLauncher, Touched = true });
        launches.Clear();
        departures.Clear();
        trackingSince = at;
    }

    /// <summary>Stops tracking: the app in front stops counting as in front. Used when quitting.</summary>
    public void Finish(DateTime at)
    {
        if (history.Count > 0 && history[0].EventId is { } id && Log.Event(id)?.HeldFor is null)
        {
            Log.Close(id, (at - history[0].Since).TotalSeconds, null, null);
        }
        history.Clear();
    }

    /// <summary>Notes an app starting up, and whether you started it.</summary>
    public void AppLaunched(int pid, DateTime at, InputSnapshot input)
    {
        ForgetOld(at);
        var origin = Rules.CauseOf(input.Sanitized, history.Count > 0 && history[0].IsLauncher) switch
        {
            Cause.Click or Cause.Shortcut or Cause.Gesture or Cause.Launcher => LaunchOrigin.You,
            Cause.Keypress => LaunchOrigin.Keypress,
            _ => LaunchOrigin.Unknown,
        };
        launches[pid] = new Launch(at, origin);
    }

    /// <summary>Notes an app being minimized or quitting. Windows then brings another app forward on its own.</summary>
    /// <param name="quit">The app, if it quit rather than being minimized, so it can be recognized relaunching.</param>
    public void AppLeft(int pid, DateTime at, AppInfo? quit = null)
    {
        ForgetOld(at);
        departures[pid] = at;
        launches.Remove(pid);
        if (quit is not null) quits[quit.Key] = at;
    }

    /// <summary>
    /// Logs an app coming to the front and returns the event. Returns null for this app's own windows,
    /// for an app that was already in front, and when there's nothing to compare it with.
    /// </summary>
    public FocusEvent? AppActivated(Activation activation)
    {
        ForgetOld(activation.Date);
        // Without knowing what was in front before, there's nothing to judge it against.
        if (history.Count == 0)
        {
            Start(activation.Pid, activation.App, activation.Date, activation.IsLauncher);
            return null;
        }
        if (history[0].Pid == activation.Pid) return null;
        var input = activation.Input.Sanitized;

        var front = history[0];
        if (!front.Touched && input.Interaction < (activation.Date - front.Since).TotalSeconds) front.Touched = true;

        FocusEvent? logged = null;
        var backToYours = false;
        if (activation.Pid != OwnPid)
        {
            var judged = Judge(activation, input);
            backToYours = judged.BackToYours;
            double? launchAge = activation.LaunchDate is { } launched ? Math.Max(0, (activation.Date - launched).TotalSeconds) : null;
            logged = new FocusEvent
            {
                Date = activation.Date, App = activation.App, Pid = activation.Pid, Previous = front.App,
                Input = input, Cause = judged.Cause, Verdict = judged.Verdict,
                SecondsSinceLaunch = launchAge <= 600 ? launchAge : null,
            };
            if (judged.TypingDecides || logged.Verdict is Verdict.Stole or Verdict.Unsure)
            {
                pending[logged.Id] = new Pending(front.Pid, judged.TypingDecides);
            }
        }

        if (front.EventId is { } frontId)
        {
            Log.Close(frontId, (activation.Date - front.Since).TotalSeconds, activation.App, logged?.Verdict);
        }
        if (logged is not null) Log.Append(logged);
        launches.Remove(activation.Pid);
        history.Insert(0, new Front
        {
            Pid = activation.Pid, App = activation.App, Since = activation.Date, IsLauncher = activation.IsLauncher,
            Verdict = logged?.Verdict, EventId = logged?.Id, Touched = false, BackToYours = backToYours,
        });
        if (history.Count > HistoryLimit) history.RemoveRange(HistoryLimit, history.Count - HistoryLimit);
        return logged;
    }

    /// <summary>Whether an event is waiting for <see cref="FinishFollowUp"/>. Until then it shouldn't be announced.</summary>
    public bool IsPending(Guid id) => pending.ContainsKey(id);

    /// <summary>Takes a second look at an event a moment after it happened, and returns it as it now stands. Returns null if it wasn't waiting.</summary>
    /// <param name="keptTyping">Whether a key was pressed after the app came forward. If you were typing before and after, the keys were meant for the app you were in.</param>
    /// <param name="spaceChanged">Whether Windows switched desktops around the same moment.</param>
    /// <param name="at">Now, to tell which departures have been reported by the time of the second look.</param>
    /// <param name="hasLeft">Whether an app has quit or been minimized by now.</param>
    public FocusEvent? FinishFollowUp(Guid id, bool keptTyping, bool spaceChanged, DateTime at, Func<int, bool>? hasLeft = null)
    {
        if (!pending.Remove(id, out var info) || Log.Event(id) is not { } e) return null;
        hasLeft ??= _ => false;
        var verdict = e.Verdict;
        var cause = e.Cause;
        var backToYours = false;
        var blamed = verdict is Verdict.Stole or Verdict.Unsure;
        if (blamed && info.PreviousPid is { } previous && (hasLeft(previous) || Departed(previous, e.Date, at)))
        {
            verdict = Verdict.Returned;
            backToYours = true;
        }
        else if (blamed && spaceChanged && e.Input.Gesture <= Rules.GestureWindow)
        {
            verdict = Verdict.Switched;
            cause = Cause.Gesture;
        }
        else if (info.TypingDecides && keptTyping)
        {
            verdict = Verdict.Stole;
            cause = Cause.Typing;
        }
        if (verdict == e.Verdict && cause == e.Cause) return e;
        var entry = history.Find(f => f.EventId == id);
        if (entry is not null)
        {
            entry.Verdict = verdict;
            entry.BackToYours = backToYours;
        }
        return Log.Update(id, old => old with { Verdict = verdict, Cause = cause });
    }

    public void ClearLog()
    {
        Log.RemoveAll();
        pending.Clear();
        foreach (var entry in history) entry.EventId = null;
    }

    // Deciding

    readonly record struct Judgment(Verdict Verdict, Cause Cause, bool BackToYours = false, bool TypingDecides = false);

    Judgment Judge(Activation activation, InputSnapshot input)
    {
        // Start, Search and the notification center open when you ask, sometimes from a touchpad
        // gesture or a corner of the screen that leaves no click or keypress behind.
        if (SystemApps.OpensOnRequest(activation.App)) return new Judgment(Verdict.Switched, Cause.SystemUI);
        var cause = Rules.CauseOf(input, history.Count > 0 && history[0].IsLauncher);
        switch (cause)
        {
            case Cause.Click or Cause.Gesture or Cause.Launcher:
                return new Judgment(Verdict.Switched, cause);
            case Cause.Shortcut:
                // Alt+Tab switches the instant Alt is let go. A modifier let go a little earlier, right
                // after a key, may be Shift between capitals, so whether typing carries on decides.
                var maybeShift = !input.CommandKeysHeld && input.ModifierChange > Rules.InstantModifierWindow
                    && input.KeyPress <= Rules.ModifierWindow;
                return new Judgment(Verdict.Switched, Cause.Shortcut, TypingDecides: maybeShift);
        }
        if (launches.TryGetValue(activation.Pid, out var launch) && (activation.Date - launch.Date).TotalSeconds <= OpeningTime)
        {
            switch (launch.Origin)
            {
                case LaunchOrigin.You: return new Judgment(Verdict.Opened, cause);
                // Enter on a command that opens it, or typing when it happened to launch.
                case LaunchOrigin.Keypress: return new Judgment(Verdict.Opened, cause, TypingDecides: true);
            }
        }
        if (IsRelaunch(activation)) return new Judgment(Verdict.Opened, Cause.Relaunch);
        if (ReturnKind(activation) is { } back) return new Judgment(Verdict.Returned, cause, BackToYours: back);
        if (trackingSince is { } since && input.Interaction >= (activation.Date - since).TotalSeconds)
        {
            return new Judgment(Verdict.Unsure, Cause.Startup);
        }
        if (cause == Cause.Keypress) return new Judgment(Verdict.Unsure, Cause.Keypress, TypingDecides: true);
        return new Judgment(Verdict.Stole, Cause.None);
    }

    /// <summary>The same app starting again shortly after it quit, as apps do when they update themselves.</summary>
    bool IsRelaunch(Activation activation)
    {
        if (!quits.TryGetValue(activation.App.Key, out var quit) || activation.LaunchDate is not { } launched) return false;
        return launched >= quit && (activation.Date - quit).TotalSeconds <= OpeningTime;
    }

    /// <summary>
    /// Null if the app isn't coming back. Otherwise, whether it's coming back to being the app you
    /// were using, as opposed to a thief getting focus back after a system prompt.
    /// </summary>
    bool? ReturnKind(Activation activation)
    {
        if (history.Count == 0) return null;
        var front = history[0];
        // Windows brings the next app forward when the one in front is minimized or quits: the app
        // you were using before it.
        if (activation.PreviousHasLeft || Departed(front.Pid, activation.Date, activation.Date)) return true;
        if (SystemApps.IsInterruption(front.App))
        {
            // Nothing known from before it, like when this app starts while the screen is locked:
            // whatever comes next is focus being handed back.
            if (history.Count <= 1) return true;
            // Straight back after a system prompt, like a password dialog or the lock screen. Even to an
            // app that stole focus itself: it didn't take it a second time.
            if (history[1].Pid == activation.Pid) return IsYours(history[1]);
        }
        // Back to the app you were using, past anything that grabbed focus without you touching it.
        // An app that only ever interrupted you doesn't qualify, or a thief could keep coming back.
        foreach (var entry in history)
        {
            if (!IsYours(entry)) continue;
            if (entry.Pid == activation.Pid) return true;
            if (!SystemApps.IsInterruption(entry.App)) return null;
        }
        return null;
    }

    /// <summary>Whether an app was minimized or quit close to <paramref name="date"/>, as known by <paramref name="now"/>.</summary>
    bool Departed(int pid, DateTime date, DateTime now)
    {
        if (!departures.TryGetValue(pid, out var left) || left > now) return false;
        return Math.Abs((date - left).TotalSeconds) <= HandoffTime;
    }

    /// <summary>Whether you were using this app, as opposed to it interrupting you.</summary>
    static bool IsYours(Front entry) => entry.Verdict switch
    {
        null or Verdict.Switched or Verdict.Opened => true,
        Verdict.Returned => entry.BackToYours || entry.Touched,
        _ => entry.Touched,
    };

    void ForgetOld(DateTime before)
    {
        var cutoff = before.AddSeconds(-Math.Max(OpeningTime, HandoffTime) - 1);
        foreach (var pid in launches.Where(p => p.Value.Date < cutoff).Select(p => p.Key).ToList()) launches.Remove(pid);
        foreach (var pid in departures.Where(p => p.Value < cutoff).Select(p => p.Key).ToList()) departures.Remove(pid);
        foreach (var key in quits.Where(p => p.Value < cutoff).Select(p => p.Key).ToList()) quits.Remove(key);
    }
}
