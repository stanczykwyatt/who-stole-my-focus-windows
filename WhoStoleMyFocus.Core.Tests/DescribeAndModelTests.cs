using System.Globalization;
using System.Text;

namespace WhoStoleMyFocus.Core.Tests;

public class DescribeTests
{
    [Theory]
    [InlineData(0.0, "0 s")]
    [InlineData(0.04, "0 s")]
    [InlineData(0.43, "0.4 s")]
    [InlineData(2.0, "2 s")]
    [InlineData(2.96, "3 s")]
    [InlineData(9.94, "9.9 s")]
    [InlineData(9.96, "10 s")]
    [InlineData(41.4, "41 s")]
    [InlineData(59.4, "59 s")]
    [InlineData(59.6, "1 min")]
    [InlineData(65.0, "1 min 5 s")]
    [InlineData(3599.6, "1 h")]
    [InlineData(3600.0, "1 h")]
    [InlineData(7380.0, "2 h 3 min")]
    [InlineData(86_400.0, "1 day")]
    [InlineData(3 * 86_400.0, "3 days")]
    [InlineData(-1.0, "0 s")]
    [InlineData(double.PositiveInfinity, "a long time")]
    public void Durations(double seconds, string expected) => Assert.Equal(expected, Describe.Duration(seconds));

    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    // .NET may put a narrow no-break space before AM and PM.
    static string Plain(string s) => s.Replace('\u202F', ' ').Replace('\u00A0', ' ');

    [Fact]
    public void RelativeTimes()
    {
        var now = Fixture.Start; // Friday, Jan 15 2027, 08:00 UTC
        string Relative(double offset) => Plain(Describe.Relative(now.AddSeconds(-offset), now, TimeZoneInfo.Utc, English));
        Assert.Equal("just now", Relative(3));
        Assert.Equal("42 s ago", Relative(42));
        Assert.Equal("10 min ago", Relative(600));
        Assert.Equal("at 5:00 AM", Relative(3 * 3600));
        Assert.Equal("yesterday at 11:00 PM", Relative(9 * 3600));
        Assert.Equal("Jan 12 at 8:00 AM", Relative(3 * 86_400));
        Assert.Equal("Dec 16, 2026 at 8:00 AM", Relative(30 * 86_400));
    }

    [Fact]
    public void CompactTimes()
    {
        var now = Fixture.Start;
        string Compact(double offset) => Plain(Describe.CompactTime(now.AddSeconds(-offset), now, TimeZoneInfo.Utc, English));
        Assert.Equal("just now", Compact(5));
        Assert.Equal("10 min ago", Compact(600));
        Assert.Equal("5:00 AM", Compact(3 * 3600));
        Assert.Equal("Yesterday 11:00 PM", Compact(9 * 3600));
        Assert.Equal("Jan 12, 8:00 AM", Compact(3 * 86_400));
        Assert.Equal("Dec 16, 2026, 8:00 AM", Compact(30 * 86_400));
    }

    [Fact]
    public void ShortReasons()
    {
        var e = Fixture.Event(Fixture.Updater, 0, from: Fixture.Editor) with { Input = new InputSnapshot(120, 41, 300, 300) };
        Assert.Equal("No input for 41 s", Describe.ShortReason(e));
        e = e with { Cause = Cause.Typing };
        Assert.Equal("Between your keystrokes", Describe.ShortReason(e));
        e = e with { Verdict = Verdict.Unsure, Cause = Cause.Keypress, Input = e.Input with { KeyPress = 0.6 } };
        Assert.Equal("0.6 s after a keypress", Describe.ShortReason(e));
        e = e with { Verdict = Verdict.Returned, Previous = Fixture.PasswordPrompt };
        Assert.Equal("Back after CredentialUIBroker", Describe.ShortReason(e));
        foreach (var cause in Enum.GetValues<Cause>())
        {
            Assert.True(Describe.ShortReason(e with { Verdict = Verdict.Switched, Cause = cause }).Length < 40);
        }
    }

    [Fact]
    public void Headlines()
    {
        Assert.Equal("SyncHelper (CloudSync) stole focus from Editor", Describe.Headline(Fixture.Event(Fixture.Updater, 0, from: Fixture.Editor)));
        Assert.Equal("SyncHelper (CloudSync) stole focus", Describe.Headline(Fixture.Event(Fixture.Updater, 0)));
        Assert.Equal("You switched to Notebook", Describe.Headline(Fixture.Event(Fixture.Notes, 0, verdict: Verdict.Switched)));
        Assert.Equal("You opened Notebook", Describe.Headline(Fixture.Event(Fixture.Notes, 0, verdict: Verdict.Opened)));
        Assert.Equal("Editor came back to the front", Describe.Headline(Fixture.Event(Fixture.Editor, 0, verdict: Verdict.Returned)));
        Assert.Equal("Notebook came forward right after a keypress", Describe.Headline(Fixture.Event(Fixture.Notes, 0, verdict: Verdict.Unsure, cause: Cause.Keypress)));
        Assert.Equal("Notebook came forward before you'd clicked or typed", Describe.Headline(Fixture.Event(Fixture.Notes, 0, verdict: Verdict.Unsure, cause: Cause.Startup)));
        Assert.Equal("Notebook came back after relaunching", Describe.Headline(Fixture.Event(Fixture.Notes, 0, verdict: Verdict.Opened, cause: Cause.Relaunch)));
    }

    [Fact]
    public void EveryVerdictHasWords()
    {
        var words = Enum.GetValues<Verdict>().Select(Describe.VerdictName).ToList();
        Assert.Equal(words.Count, words.Distinct().Count());
        Assert.Equal("Stole focus", Describe.VerdictName(Verdict.Stole));
    }

    [Fact]
    public void Reasons()
    {
        var e = Fixture.Event(Fixture.Updater, 0, from: Fixture.Editor) with { Input = new InputSnapshot(120, 41, 300, 300) };
        Assert.Equal("No click or keypress for 41 s", Describe.Reason(e));
        e = e with { SecondsSinceLaunch = 2 };
        Assert.Equal("No click or keypress for 41 s, 2 s after it launched by itself", Describe.Reason(e));
        e = e with { Cause = Cause.Typing };
        Assert.Equal("While you were typing: it came forward between your keystrokes", Describe.Reason(e));
        e = e with { Verdict = Verdict.Switched, Cause = Cause.Click, Input = e.Input with { Click = 0.2 } };
        Assert.Equal("0.2 s after a click", Describe.Reason(e));
        e = e with { Cause = Cause.Launcher, Previous = Fixture.Launcher };
        Assert.Equal("After you typed in Launcher", Describe.Reason(e));
        e = e with { Verdict = Verdict.Returned, Previous = Fixture.PasswordPrompt };
        Assert.Equal("Focus went back to it after CredentialUIBroker", Describe.Reason(e));
        e = e with { Verdict = Verdict.Opened, SecondsSinceLaunch = 12 };
        Assert.Equal("Came to the front 12 s after you opened it", Describe.Reason(e));
    }

    [Fact]
    public void EveryCauseHasAReason()
    {
        foreach (var cause in Enum.GetValues<Cause>())
        {
            var e = Fixture.Event(Fixture.Notes, 0, from: Fixture.Editor) with
            {
                Cause = cause, Verdict = cause is Cause.Typing or Cause.None ? Verdict.Stole : Verdict.Switched,
            };
            Assert.NotEmpty(Describe.Reason(e));
            Assert.DoesNotContain("mac", Describe.Reason(e), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Aftermath()
    {
        var e = Fixture.Event(Fixture.Updater, 0, from: Fixture.Editor);
        Assert.Equal("Still in front", Describe.Aftermath(e));
        e = e with { HeldFor = 2, Next = Fixture.Editor, NextVerdict = Verdict.Switched };
        Assert.Equal("In front for 2 s, then you went back to Editor", Describe.Aftermath(e));
        e = e with { NextVerdict = Verdict.Returned };
        Assert.Equal("In front for 2 s, until Editor came back", Describe.Aftermath(e));
        e = e with { Next = Fixture.Notes, NextVerdict = Verdict.Switched };
        Assert.Equal("In front for 2 s", Describe.Aftermath(e));
    }

    [Fact]
    public void DetailsHaveEverythingForABugReport()
    {
        var e = Fixture.Event(Fixture.PasswordPrompt, 0, from: Fixture.Editor, heldFor: 9) with { Input = new InputSnapshot(75, 75, 75, 75) };
        Assert.Equal("""
            CredentialUIBroker stole focus from Editor
            Friday 8:00
            No click or keypress for 1 min 15 s
            In front for 9 s
            CredentialUIBroker is Windows asking for your password or PIN.
            App: CredentialUIBroker
            Path: C:\Windows\System32\CredentialUIBroker.exe
            """.ReplaceLineEndings("\n"), Describe.Details(e, "Friday 8:00"));
        var helper = Fixture.Event(Fixture.Updater, 0) with { HeldFor = 1 };
        Assert.Contains("App: SyncHelper (CloudSync)", Describe.Details(helper, "now"));
    }

    [Fact]
    public void UserFolderIsShortened()
    {
        Assert.Equal(@"~\AppData\Tool.exe", Describe.ShortPath(@"C:\Users\sam\AppData\Tool.exe", @"C:\Users\sam"));
        Assert.Equal("~", Describe.ShortPath(@"C:\Users\sam", @"C:\Users\sam\"));
        Assert.Equal(@"C:\Users\samantha\Tool.exe", Describe.ShortPath(@"C:\Users\samantha\Tool.exe", @"C:\Users\sam"));
        Assert.Equal(@"C:\Tool.exe", Describe.ShortPath(@"C:\Tool.exe", null));
    }

    [Fact]
    public void EventLeftOpenByACrashIsntStillInFront()
    {
        var e = Fixture.Event(Fixture.Updater, 0);
        Assert.Equal("Still in front", Describe.Aftermath(e, true));
        Assert.Equal("In front until Who Stole My Focus stopped watching", Describe.Aftermath(e, false));
    }

    [Fact]
    public void SystemNotes()
    {
        Assert.Equal("CredentialUIBroker is Windows asking for your password or PIN.", Describe.SystemNote(Fixture.PasswordPrompt));
        Assert.Null(Describe.SystemNote(Fixture.Editor));
    }
}

public class FocusModelTests
{
    static FocusModel Model(AppSettings? settings = null)
    {
        var model = new FocusModel(null, settings ?? new AppSettings(), Fixture.Pid(Fixture.Own));
        model.Start(Fixture.Pid(Fixture.Editor), Fixture.Editor, Fixture.Start.AddSeconds(-3600), false);
        return model;
    }

    static Activation At(AppInfo app, double seconds, InputSnapshot? input = null) =>
        new(Fixture.Pid(app), app, Fixture.Start.AddSeconds(seconds), input ?? Fixture.Idle);

    [Fact]
    public void AStealIsAnnouncedOnlyAfterItsSecondLook()
    {
        var model = Model();
        var announced = new List<FocusEvent>();
        model.Stole += announced.Add;
        var e = model.AppActivated(At(Fixture.Updater, 1))!;
        Assert.Empty(announced);
        model.FinishFollowUp(e.Id, false, false, Fixture.Start.AddSeconds(1.4));
        Assert.Single(announced);
        Assert.Equal(1, model.UnseenSteals);
        model.MarkSeen();
        Assert.Equal(0, model.UnseenSteals);
    }

    [Fact]
    public void IgnoredAppsAreNeverAnnounced()
    {
        var model = Model();
        var announced = 0;
        model.Stole += _ => announced++;
        model.Ignore(Fixture.Updater);
        var e = model.AppActivated(At(Fixture.Updater, 1))!;
        model.FinishFollowUp(e.Id, false, false, Fixture.Start.AddSeconds(1.4));
        Assert.Equal(0, announced);
        Assert.Empty(model.Steals());
        Assert.Contains((Fixture.Updater.Key, "SyncHelper (CloudSync)"), model.IgnoredApps());
        model.StopIgnoring(Fixture.Updater.Key);
        Assert.Single(model.Steals());
    }

    [Fact]
    public void TheLockScreenIsIgnoredUntilYouSayOtherwise()
    {
        var model = Model();
        Assert.True(model.IsIgnored(Fixture.LockScreen));
        Assert.Contains(model.IgnoredApps(), p => p.Name == "Lock screen (LockApp)");
        Assert.False(Model(new AppSettings { Ignored = [] }).IsIgnored(Fixture.LockScreen));
    }

    [Fact]
    public void ChangesAreReported()
    {
        var model = Model();
        var changes = 0;
        model.Changed += () => changes++;
        model.AppActivated(At(Fixture.Notes, 1, Fixture.Clicked(0.1)));
        model.ClearLog();
        Assert.Equal(2, changes);
        Assert.Empty(model.Log.Events);
    }

    [Fact]
    public void ThresholdAndSettingsRoundTrip()
    {
        var model = Model();
        model.Threshold = 3;
        model.Notifies = true;
        model.Ignore(Fixture.Meetings);
        var read = AppSettings.Load(model.Settings.Save());
        Assert.Equal(3, read.Threshold);
        Assert.True(read.Notifies);
        Assert.Contains(Fixture.Meetings.Key, read.Ignored);
        Assert.Contains("system:lockapp.exe", read.Ignored);
    }

    [Fact]
    public void SavedSettingsAreReadAsUntrusted()
    {
        Assert.Equal(new AppSettings().Threshold, AppSettings.Load(null).Threshold);
        Assert.Equal(new AppSettings().Ignored, AppSettings.Load("garbage"u8.ToArray()).Ignored);
        var odd = AppSettings.Load(Encoding.UTF8.GetBytes("""{"threshold": 7, "notifies": "yes", "ignored": ["a", 4, "", "b\u0007", "a"]}"""));
        Assert.Equal(CauseRules.DefaultThreshold, odd.Threshold);
        Assert.False(odd.Notifies);
        Assert.Equal(["a"], odd.Ignored);
        Assert.Equal(1, AppSettings.Load("""{"threshold": 1}"""u8.ToArray()).Threshold);
    }
}
