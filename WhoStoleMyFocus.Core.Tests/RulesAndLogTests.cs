using System.Text;

namespace WhoStoleMyFocus.Core.Tests;

public class CauseRulesTests
{
    readonly CauseRules rules = new();

    [Fact] public void ClickJustBefore() => Assert.Equal(Cause.Click, rules.CauseOf(Fixture.Clicked(0.1), false));
    [Fact] public void SlowLinkOpenStillCountsAsTheClick() => Assert.Equal(Cause.Click, rules.CauseOf(Fixture.Clicked(1.4), false));
    [Fact] public void ClickThenTypingIsTypingNotTheClick() => Assert.Equal(Cause.Keypress, rules.CauseOf(new InputSnapshot(1.0, 0.1, 60, 60), false));
    [Fact] public void AltTab() => Assert.Equal(Cause.Shortcut, rules.CauseOf(Fixture.AltTab, false));
    [Fact] public void HeldShortcutKey() => Assert.Equal(Cause.Shortcut, rules.CauseOf(Fixture.Typed(0.05) with { CommandKeysHeld = true }, false));
    [Fact] public void ModifierLetGoTooLongAgoDoesntCount() => Assert.Equal(Cause.None, rules.CauseOf(new InputSnapshot(60, 2, 0.9, 60), false));
    [Fact] public void ShiftPressedBeforeTheLastKeyIsJustTyping() => Assert.Equal(Cause.Keypress, rules.CauseOf(new InputSnapshot(60, 0.1, 0.3, 60), false));
    [Fact] public void EnterInALauncher() => Assert.Equal(Cause.Launcher, rules.CauseOf(Fixture.Typed(0.2), true));
    [Fact] public void TouchpadTouchAloneIsntACause() => Assert.Equal(Cause.None, rules.CauseOf(new InputSnapshot(60, 3, 60, 0.1), false));

    [Fact]
    public void NothingRecent()
    {
        Assert.Equal(Cause.None, rules.CauseOf(Fixture.Idle, false));
        Assert.Equal(Cause.None, rules.CauseOf(InputSnapshot.None, true));
    }

    [Fact]
    public void ThresholdIsInclusive()
    {
        Assert.Equal(Cause.Click, rules.CauseOf(Fixture.Clicked(1.5), false));
        Assert.Equal(Cause.None, rules.CauseOf(Fixture.Clicked(1.51), false));
    }

    [Fact]
    public void ThresholdChoicesIncludeTheDefault()
    {
        Assert.Contains(CauseRules.DefaultThreshold, CauseRules.ThresholdChoices);
        Assert.Equal(CauseRules.ThresholdChoices.Order(), CauseRules.ThresholdChoices);
    }

    [Fact]
    public void LatestLeavesOutTouchpadTouches()
    {
        Assert.Equal(2, new InputSnapshot(4, 2, 9, 1).Latest);
        Assert.Equal(3, new InputSnapshot(4, 6, 3, 1).Latest);
    }

    [Fact] public void InteractionIsClicksAndKeys() => Assert.Equal(4, new InputSnapshot(4, 6, 1, 0.5).Interaction);

    [Fact]
    public void SanitizingClampsOddValues() =>
        Assert.Equal(new InputSnapshot(0, InputSnapshot.Longest, 5, InputSnapshot.Longest, true),
            new InputSnapshot(-1, double.NaN, 5, double.PositiveInfinity, true).Sanitized);
}

public class FollowUpTests
{
    [Fact] public void KeyAfterTheSwitchIsTypingOn() => Assert.True(FollowUp.KeptTyping(0.4, 0.4, Fixture.Typed(0.1)));
    [Fact] public void NoKeySinceTheSwitch() => Assert.False(FollowUp.KeptTyping(0.4, 0.4, Fixture.Typed(0.5)));
    [Fact] public void ShortcutAimedAtTheNewAppDoesntCount() => Assert.False(FollowUp.KeptTyping(0.4, 0.4, new InputSnapshot(60, 0.1, 0.05, 60)));
    [Fact] public void HeldCtrlDoesntCount() => Assert.False(FollowUp.KeptTyping(0.4, 0.4, Fixture.Typed(0.1) with { CommandKeysHeld = true }));
    [Fact] public void ALateCheckCantTell() => Assert.False(FollowUp.KeptTyping(1.5, 0.4, Fixture.Typed(0.1)));
}

public class FocusLogTests
{
    [Fact]
    public void DropsTheOldestOnceFull()
    {
        var log = new FocusLog(limit: 2);
        var events = Enumerable.Range(0, 3).Select(i => Fixture.Event(Fixture.Updater, i)).ToList();
        events.ForEach(log.Append);
        Assert.Equal(events.Skip(1), log.Events);
    }

    [Fact]
    public void StartingFullKeepsTheNewest()
    {
        var events = Enumerable.Range(0, 5).Select(i => Fixture.Event(Fixture.Updater, i)).ToList();
        Assert.Equal(events.Skip(2), new FocusLog(events, 3).Events);
        Assert.Equal(1, new FocusLog(events, 0).Limit);
    }

    [Fact]
    public void UpdatesInPlace()
    {
        var log = new FocusLog();
        var e = Fixture.Event(Fixture.Updater, 0);
        log.Append(e);
        var changed = log.Update(e.Id, old => old with { Verdict = Verdict.Returned })!;
        Assert.Equal(Verdict.Returned, changed.Verdict);
        Assert.Equal(Verdict.Returned, log.Events[0].Verdict);
        Assert.Null(log.Update(Guid.NewGuid(), old => old with { Verdict = Verdict.Stole }));
        // An update can't change which event it is.
        log.Update(e.Id, old => old with { Id = Guid.NewGuid() });
        Assert.NotNull(log.Event(e.Id));
    }

    [Fact]
    public void ClosingRecordsWhatCameNext()
    {
        var log = new FocusLog();
        var e = Fixture.Event(Fixture.Updater, 0, from: Fixture.Editor);
        log.Append(e);
        log.Close(e.Id, -1, Fixture.Editor, Verdict.Switched);
        var closed = log.Event(e.Id)!;
        Assert.Equal(0, closed.HeldFor);
        Assert.True(closed.YouWentBack);
    }

    [Fact]
    public void StealsAreNewestFirstWithoutIgnoredApps()
    {
        var log = new FocusLog([
            Fixture.Event(Fixture.Updater, 0),
            Fixture.Event(Fixture.Notes, 1, verdict: Verdict.Switched, cause: Cause.Click),
            Fixture.Event(Fixture.Meetings, 2),
            Fixture.Event(Fixture.LockScreen, 3),
        ]);
        Assert.Equal([Fixture.LockScreen, Fixture.Meetings, Fixture.Updater], log.Steals().Select(e => e.App));
        Assert.Equal([Fixture.Meetings, Fixture.Updater], log.Steals(SystemApps.IgnoredByDefault.ToHashSet()).Select(e => e.App));
        Assert.Equal([Fixture.LockScreen, Fixture.Meetings], log.Steals(since: Fixture.Start.AddSeconds(2)).Select(e => e.App));
    }

    [Fact]
    public void OffendersAreRankedByCountThenRecency()
    {
        var log = new FocusLog([
            Fixture.Event(Fixture.Meetings, 0, from: Fixture.Notes),
            Fixture.Event(Fixture.Updater, 1, from: Fixture.Editor),
            Fixture.Event(Fixture.Updater, 2, from: Fixture.Editor),
            Fixture.Event(Fixture.Updater, 3, from: Fixture.Notes),
            Fixture.Event(Fixture.PasswordPrompt, 4, from: Fixture.Editor),
            Fixture.Event(Fixture.Notes, 5, verdict: Verdict.Switched, cause: Cause.Click),
        ]);
        var offenders = log.Offenders();
        Assert.Equal([Fixture.Updater, Fixture.PasswordPrompt, Fixture.Meetings], offenders.Select(o => o.App));
        Assert.Equal(3, offenders[0].Count);
        Assert.Equal(Fixture.Start.AddSeconds(3), offenders[0].LastDate);
        Assert.Equal(Fixture.Editor, offenders[0].UsualVictim);
    }

    [Fact]
    public void OffendersLeaveOutIgnoredAppsAndOldSteals()
    {
        var log = new FocusLog([Fixture.Event(Fixture.Updater, 0), Fixture.Event(Fixture.Meetings, 10)]);
        Assert.Equal([Fixture.Updater], log.Offenders(new HashSet<string> { Fixture.Meetings.Key }).Select(o => o.App));
        Assert.Equal([Fixture.Meetings], log.Offenders(since: Fixture.Start.AddSeconds(5)).Select(o => o.App));
    }

    [Fact]
    public void OffendersWithEqualCountsAndTimesSortByName()
    {
        var log = new FocusLog([Fixture.Event(new AppInfo("beta", @"C:\b.exe"), 0), Fixture.Event(new AppInfo("Alpha", @"C:\a.exe"), 0)]);
        Assert.Equal(["Alpha", "beta"], log.Offenders().Select(o => o.App.Name));
    }

    [Fact]
    public void OffenderUsesItsNewestName()
    {
        var renamed = Fixture.Updater with { Name = "SyncHelper 2" };
        var log = new FocusLog([Fixture.Event(Fixture.Updater, 0), Fixture.Event(renamed, 1)]);
        Assert.Equal("SyncHelper 2", log.Offenders()[0].App.Name);
        Assert.Equal(2, log.Offenders()[0].Count);
    }
}

public class LogFileTests
{
    [Fact]
    public void RoundTrips()
    {
        var e = Fixture.Event(Fixture.Updater, 0.25, from: Fixture.Editor, heldFor: 1.5) with
        {
            Input = new InputSnapshot(1, 2, 3, 4, true), Next = Fixture.Editor, NextVerdict = Verdict.Switched, SecondsSinceLaunch = 3,
            Cause = Cause.SystemUI,
        };
        var log = new FocusLog([e, Fixture.Event(Fixture.Notes, 1, verdict: Verdict.Opened)]);
        Assert.Equal(log, LogFile.Decode(LogFile.Encode(log)));
        Assert.Contains("\"systemUI\"", Encoding.UTF8.GetString(LogFile.Encode(log)));
    }

    [Fact]
    public void RefusesAFileFromANewerVersion()
    {
        var error = Assert.Throws<LogFile.NewerVersionException>(() => LogFile.Decode("""{"version": 99, "events": []}"""u8.ToArray()));
        Assert.Equal(99, error.FoundVersion);
    }

    [Fact]
    public void RefusesGarbage()
    {
        Assert.ThrowsAny<Exception>(() => LogFile.Decode("not json"u8.ToArray()));
        Assert.ThrowsAny<Exception>(() => LogFile.Decode("""{"events": []}"""u8.ToArray()));
        Assert.ThrowsAny<Exception>(() => LogFile.Decode("""{"version": 1, "events": [{"verdict": "plotted"}]}"""u8.ToArray()));
    }

    [Fact]
    public void AppliesTheLimitWhenReading()
    {
        var log = new FocusLog(Enumerable.Range(0, 10).Select(i => Fixture.Event(Fixture.Updater, i)));
        Assert.Equal(4, LogFile.Decode(LogFile.Encode(log), 4).Events.Count);
    }

    [Fact]
    public void ReadsAFileShapedLikeTheMacApps()
    {
        // The same format as the Mac app, so a log can be read on either.
        var json = """
        {"version":1,"events":[{"id":"6F9619FF-8B86-D011-B42D-00C04FC964FF","date":1800000000,
        "app":{"name":"SyncHelper","path":"C:\\Program Files\\CloudSync\\bin\\SyncHelper.exe"},"pid":103,
        "previous":{"name":"Editor","path":"C:\\Program Files\\Editor\\Editor.exe"},
        "input":{"click":60,"keyPress":0.1,"modifierChange":60,"gesture":60,"commandKeysHeld":false},
        "cause":"typing","verdict":"stole","heldFor":2}]}
        """;
        var e = LogFile.Decode(Encoding.UTF8.GetBytes(json)).Events[0];
        Assert.Equal("SyncHelper", e.App.Name);
        Assert.Equal(Cause.Typing, e.Cause);
        Assert.Equal(Verdict.Stole, e.Verdict);
        Assert.Equal(2, e.HeldFor);
        Assert.Equal(Fixture.Start, e.Date);
        Assert.Null(e.Next);
    }
}

public class CsvExportTests
{
    static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void HeaderAndOneRow()
    {
        var e = Fixture.Event(Fixture.Updater, 0, from: Fixture.Editor, heldFor: 2.04) with { Input = Fixture.Typed(0.15), Cause = Cause.Typing };
        var lines = CsvExport.Csv([e], Utc).Split('\n');
        Assert.Equal("Time,App,Part Of,Path,Took Focus From,What Happened,Why,Seconds Since Input,Seconds In Front", lines[0]);
        Assert.Equal(@"2027-01-15T08:00:00Z,SyncHelper,CloudSync,C:\Program Files\CloudSync\bin\SyncHelper.exe,Editor,Stole focus,typing,0.1,2.0", lines[1]);
        Assert.Equal(3, lines.Length);
        Assert.Equal("", lines[2]);
    }

    [Fact]
    public void TimesCarryTheirOffset()
    {
        var central = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Central Standard Time" : "America/Chicago");
        Assert.Contains("2027-01-15T02:00:00-06:00", CsvExport.Csv([Fixture.Event(Fixture.Updater, 0)], central));
    }

    [Fact]
    public void MissingValuesAreEmpty()
    {
        var e = new FocusEvent { Date = Fixture.Start, App = new AppInfo("Bare"), Pid = 1, Input = Fixture.Idle, Cause = Cause.None, Verdict = Verdict.Stole };
        Assert.Equal("2027-01-15T08:00:00Z,Bare,,,,Stole focus,none,60.0,", CsvExport.Csv([e], Utc).Split('\n')[1]);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("windows\r\nline", "\"windows\r\nline\"")]
    [InlineData(" padded ", "\" padded \"")]
    [InlineData("", "")]
    public void QuotesFieldsThatNeedIt(string value, string expected) => Assert.Equal(expected, CsvExport.Field(value));

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@sum", "'@sum")]
    [InlineData("\tTab", "'\tTab")]
    [InlineData("Safe=", "Safe=")]
    public void NamesThatLookLikeFormulasOpenAsText(string value, string expected) => Assert.Equal(expected, CsvExport.Field(value));

    [Fact]
    public void UserFolderIsShortenedInPaths()
    {
        var e = Fixture.Event(new AppInfo("Tool", @"C:\Users\sam\AppData\Local\Tool\Tool.exe"), 0);
        Assert.Contains(@",~\AppData\Local\Tool\Tool.exe,", CsvExport.Csv([e], Utc, @"C:\Users\sam"));
        Assert.Contains(@",~\AppData\Local\Tool\Tool.exe,", CsvExport.Csv([e], Utc, @"c:\users\SAM\"));
    }

    [Fact]
    public void EmptyLogIsJustTheHeader() => Assert.Equal(string.Join(",", CsvExport.Header) + "\n", CsvExport.Csv([]));
}

public class AppInfoTests
{
    [Fact]
    public void KeyIsThePathThenTheName()
    {
        Assert.Equal(@"c:\program files\a\a.exe", new AppInfo("A", @"C:\Program Files\A\A.exe").Key);
        Assert.Equal("A", new AppInfo("A").Key);
    }

    [Fact]
    public void UpdatesInNewVersionFoldersKeepTheKey()
    {
        var before = new AppInfo("Chat", @"C:\Users\sam\AppData\Local\Chat\app-1.0.9\Chat.exe");
        var after = new AppInfo("Chat", @"C:\Users\sam\AppData\Local\Chat\app-1.1.0\Chat.exe");
        Assert.Equal(before.Key, after.Key);
        var browser = new AppInfo("Browser", @"C:\Program Files\Browser\128.0.6613.120\helper.exe");
        Assert.Equal(new AppInfo("Browser", @"C:\Program Files\Browser\129.0.1.2\helper.exe").Key, browser.Key);
        Assert.NotEqual(new AppInfo("Other", @"C:\Program Files\Other\Chat.exe").Key, before.Key);
    }

    [Fact]
    public void PartsOfWindowsAreKeyedByFileName()
    {
        Assert.Equal("system:lockapp.exe", Fixture.LockScreen.Key);
        Assert.Equal("system:credentialuibroker.exe", Fixture.PasswordPrompt.Key);
    }

    [Fact]
    public void HelperNamesItsProduct()
    {
        Assert.Equal("CloudSync", Fixture.Updater.ContainingAppName);
        Assert.Null(Fixture.Editor.ContainingAppName);
        Assert.Null(new AppInfo("editor", Product: "Editor ").ContainingAppName);
        Assert.Null(Fixture.PasswordPrompt.ContainingAppName);
        Assert.Null(new AppInfo("Nameless").ContainingAppName);
    }

    [Fact]
    public void FileName()
    {
        Assert.Equal("synchelper.exe", Fixture.Updater.FileName);
        Assert.Equal("tool.exe", new AppInfo("Tool", "C:/Tools/Tool.exe").FileName);
    }
}

public class SystemAppsTests
{
    [Fact]
    public void EveryEntryIsAnExecutableWithADescription()
    {
        foreach (var (name, description) in SystemApps.Descriptions)
        {
            Assert.EndsWith(".exe", name);
            Assert.Equal(name, name.ToLowerInvariant());
            // Used mid-sentence, as in "CredentialUIBroker is Windows asking for your password or PIN."
            Assert.NotEmpty(description);
            Assert.False(description.EndsWith('.'));
        }
        Assert.Subset(SystemApps.Descriptions.Keys.ToHashSet(), SystemApps.OnRequest.ToHashSet());
    }

    [Fact]
    public void IgnoredByDefaultAreSystemScreensWithNames()
    {
        foreach (var key in SystemApps.IgnoredByDefault)
        {
            Assert.StartsWith("system:", key);
            Assert.True(SystemApps.Descriptions.ContainsKey(key["system:".Length..]));
        }
        Assert.Contains(Fixture.LockScreen.Key, SystemApps.IgnoredByDefault);
    }

    [Fact]
    public void PasswordPromptIsAnInterruption()
    {
        Assert.True(SystemApps.IsInterruption(Fixture.PasswordPrompt));
        Assert.False(SystemApps.IsInterruption(Fixture.Editor));
        Assert.False(SystemApps.IsInterruption(new AppInfo("No path")));
    }

    [Fact]
    public void Launchers()
    {
        Assert.True(SystemApps.IsLauncher(new AppInfo("Start", @"C:\Windows\SystemApps\Start\StartMenuExperienceHost.exe")));
        Assert.True(SystemApps.IsLauncher(new AppInfo("PowerToys Run", @"C:\Program Files\PowerToys\PowerToys.PowerLauncher.exe")));
        Assert.False(SystemApps.IsLauncher(new AppInfo("Search", @"C:\Users\sam\SearchHost.exe")));
        Assert.False(SystemApps.IsLauncher(Fixture.Editor));
    }
}
