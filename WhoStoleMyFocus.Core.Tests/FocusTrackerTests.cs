namespace WhoStoleMyFocus.Core.Tests;

// The same scenarios as the Mac app's FocusTrackerTests, with Windows names for the same situations.

public class StealTests
{
    [Fact]
    public void AppComingForwardWhileYouAreIdleStealsFocus()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: Fixture.Idle)!;
        Assert.Equal(Verdict.Stole, e.Verdict);
        Assert.Equal(Cause.None, e.Cause);
        Assert.Equal(Fixture.Editor, e.Previous);
        Assert.Equal([e], scenario.Events);
    }

    [Fact]
    public void TypingStraightThroughASwitchIsASteal()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: Fixture.Typed(0.15))!;
        Assert.Equal(Verdict.Unsure, e.Verdict);
        Assert.True(scenario.IsPending(e.Id));
        var settled = scenario.FinishFollowUp(e.Id, keptTyping: true)!;
        Assert.Equal(Verdict.Stole, settled.Verdict);
        Assert.Equal(Cause.Typing, settled.Cause);
        Assert.Equal(Verdict.Stole, scenario.Events[^1].Verdict);
        Assert.False(scenario.IsPending(e.Id));
    }

    [Fact]
    public void StoppingAfterAKeypressStaysUnsure()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: Fixture.Typed(0.4))!;
        Assert.Equal(Verdict.Unsure, scenario.FinishFollowUp(e.Id, keptTyping: false)?.Verdict);
        Assert.False(scenario.IsPending(e.Id));
    }

    [Fact]
    public void ClearSwitchesNeedNoFollowUp()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1))!;
        Assert.False(scenario.IsPending(e.Id));
        Assert.Null(scenario.FinishFollowUp(e.Id, keptTyping: true));
        Assert.Equal(Verdict.Switched, scenario.Events[^1].Verdict);
    }

    [Fact]
    public void FollowUpOnlyHappensOnce()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: Fixture.Typed(0.1))!;
        Assert.NotNull(scenario.FinishFollowUp(e.Id));
        Assert.Null(scenario.FinishFollowUp(e.Id, keptTyping: true));
        Assert.Equal(Verdict.Unsure, scenario.Events[^1].Verdict);
    }

    [Fact]
    public void FollowUpForAnEventThatWasClearedDoesNothing()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: Fixture.Typed(0.1))!;
        scenario.Tracker.ClearLog();
        Assert.Null(scenario.FinishFollowUp(e.Id, keptTyping: true));
        Assert.Empty(scenario.Events);
    }

    [Fact]
    public void SwipingToAnotherDesktopIsYours()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Notes, input: Fixture.TouchedTrackpad(0.3))!;
        Assert.True(scenario.IsPending(e.Id));
        var settled = scenario.FinishFollowUp(e.Id, spaceChanged: true)!;
        Assert.Equal(Verdict.Switched, settled.Verdict);
        Assert.Equal(Cause.Gesture, settled.Cause);
    }

    [Fact]
    public void TouchingTheTouchpadWithoutADesktopChangeIsStillASteal()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: Fixture.TouchedTrackpad(0.2))!;
        Assert.Equal(Verdict.Stole, e.Verdict);
        Assert.True(scenario.IsPending(e.Id));
        Assert.Equal(Verdict.Stole, scenario.FinishFollowUp(e.Id)?.Verdict);
    }

    [Fact]
    public void DesktopChangeWithoutATouchIsntASwipe()
    {
        // An app on another desktop jumping forward makes Windows switch desktops too.
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: Fixture.Typed(0.1))!;
        Assert.Equal(Verdict.Stole, scenario.FinishFollowUp(e.Id, keptTyping: true, spaceChanged: true)?.Verdict);
    }

    [Fact]
    public void EveryStealGetsASecondLookBeforeItsAnnounced()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: Fixture.TouchedTrackpad(5))!;
        Assert.True(scenario.IsPending(e.Id));
        Assert.Equal(Verdict.Stole, scenario.FinishFollowUp(e.Id, spaceChanged: true)?.Verdict);
    }

    [Fact]
    public void QuitReportedJustAfterTheSwitchIsAHandoff()
    {
        // Windows can bring the next app forward before the last one has finished quitting.
        var scenario = new Scenario();
        scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1));
        var e = scenario.Activate(Fixture.Meetings, after: 30)!;
        Assert.Equal(Verdict.Stole, e.Verdict);
        scenario.Leave(Fixture.Notes, after: 0.1);
        Assert.Equal(Verdict.Returned, scenario.FinishFollowUp(e.Id)?.Verdict);
    }

    [Fact]
    public void AppThatHasLeftByTheSecondLookHandedFocusOver()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1));
        var e = scenario.Activate(Fixture.Meetings, after: 30)!;
        Assert.Equal(Verdict.Returned, scenario.FinishFollowUp(e.Id, left: Fixture.Notes)?.Verdict);
    }

    [Fact]
    public void ModifierLetGoRightAfterAKeyCouldBeShift()
    {
        // Shift released between capitals looks like the end of Alt+Tab, so typing on decides.
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Updater, input: new InputSnapshot(60, 0.25, 0.2, 60))!;
        Assert.Equal(Verdict.Switched, e.Verdict);
        Assert.True(scenario.IsPending(e.Id));
        Assert.Equal(Verdict.Stole, scenario.FinishFollowUp(e.Id, keptTyping: true)?.Verdict);
    }

    [Fact]
    public void ModifierLetGoAtTheSwitchIsAltTab()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Notes, input: Fixture.AltTab)!;
        Assert.False(scenario.IsPending(e.Id));
    }

    [Fact]
    public void MovingThePointerOverAThiefDoesntMakeItYours()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Updater);
        var input = Fixture.Idle with { Gesture = 5, ModifierChange = 60 };
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Editor, after: 30, input: input)?.Verdict);
    }

    [Fact]
    public void AppThatLaunchedItselfAndJumpedForwardStealsFocus()
    {
        var scenario = new Scenario();
        scenario.Launch(Fixture.Updater, input: Fixture.Idle);
        var e = scenario.Activate(Fixture.Updater, after: 2, launchedAgo: 2)!;
        Assert.Equal(Verdict.Stole, e.Verdict);
        Assert.Equal(2, e.SecondsSinceLaunch);
    }

    [Fact]
    public void LaunchesLongAgoArentMentioned()
    {
        var scenario = new Scenario();
        Assert.Null(scenario.Activate(Fixture.Updater, launchedAgo: 3600)!.SecondsSinceLaunch);
    }
}

public class SwitchTests
{
    [Fact]
    public void ClickJustBeforeIsYours()
    {
        var e = new Scenario().Activate(Fixture.Notes, input: Fixture.Clicked(0.08))!;
        Assert.Equal(Verdict.Switched, e.Verdict);
        Assert.Equal(Cause.Click, e.Cause);
    }

    [Fact]
    public void AltTabIsYours()
    {
        var e = new Scenario().Activate(Fixture.Notes, input: Fixture.AltTab)!;
        Assert.Equal(Verdict.Switched, e.Verdict);
        Assert.Equal(Cause.Shortcut, e.Cause);
    }

    [Fact]
    public void EnterInALauncherIsYours()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Launcher, input: Fixture.AltTab, launcher: true);
        var e = scenario.Activate(Fixture.Notes, input: Fixture.Typed(0.2))!;
        Assert.Equal(Verdict.Switched, e.Verdict);
        Assert.Equal(Cause.Launcher, e.Cause);
    }

    [Fact]
    public void ThresholdChangesWhatCountsAsYours()
    {
        Assert.Equal(Verdict.Switched, new Scenario(rules: new CauseRules { Threshold = 3 }).Activate(Fixture.Notes, input: Fixture.Clicked(2.5))?.Verdict);
        Assert.Equal(Verdict.Stole, new Scenario(rules: new CauseRules { Threshold = 0.5 }).Activate(Fixture.Notes, input: Fixture.Clicked(1))?.Verdict);
    }

    [Fact]
    public void ThresholdCanChangeWhileRunning()
    {
        var scenario = new Scenario();
        scenario.Tracker.Rules.Threshold = 0.5;
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Notes, input: Fixture.Clicked(1))?.Verdict);
    }
}

public class OpenTests
{
    [Fact]
    public void AppLaunchedWhileYouTypeIsStillCheckedForTyping()
    {
        var scenario = new Scenario();
        scenario.Launch(Fixture.Updater, input: Fixture.Typed(0.1));
        var e = scenario.Activate(Fixture.Updater, after: 3, input: Fixture.Typed(0.1))!;
        Assert.Equal(Verdict.Opened, e.Verdict);
        Assert.True(scenario.IsPending(e.Id));
        Assert.Equal(Verdict.Stole, scenario.FinishFollowUp(e.Id, keptTyping: true)?.Verdict);
    }

    [Fact]
    public void CommandThatOpensAnAppIsYours()
    {
        // Enter on `code .` in a terminal, then waiting for the editor to come up.
        var scenario = new Scenario();
        scenario.Launch(Fixture.Meetings, input: Fixture.Typed(0.1));
        var e = scenario.Activate(Fixture.Meetings, after: 3)!;
        Assert.Equal(Verdict.Opened, scenario.FinishFollowUp(e.Id, keptTyping: false)?.Verdict);
    }

    [Fact]
    public void AppRelaunchingAfterAnUpdateIsntAThief()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Meetings, input: Fixture.Clicked(0.1));
        scenario.Activate(Fixture.Editor, input: Fixture.Clicked(0.1));
        scenario.Leave(Fixture.Meetings, after: 5, quitting: true);
        // The update moved it to a new version folder, which is still the same app.
        var updated = Fixture.Meetings with { Path = @"C:\Users\sam\AppData\Local\Meetings\app-2.5.0\Meetings.exe" };
        var e = scenario.Activate(updated, after: 12, launchedAgo: 3)!;
        Assert.Equal(Verdict.Opened, e.Verdict);
        Assert.Equal(Cause.Relaunch, e.Cause);
    }

    [Fact]
    public void RelaunchLongAfterQuittingIsJudgedNormally()
    {
        var scenario = new Scenario();
        scenario.Leave(Fixture.Meetings, after: 5, quitting: true);
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Meetings, after: 120, launchedAgo: 3)?.Verdict);
    }

    [Fact]
    public void AppYouOpenedIsntBlamedForTakingAWhile()
    {
        var scenario = new Scenario();
        scenario.Launch(Fixture.Meetings, input: Fixture.Clicked(0.1));
        Assert.Equal(Verdict.Opened, scenario.Activate(Fixture.Meetings, after: 12, launchedAgo: 12)?.Verdict);
    }

    [Fact]
    public void OpeningFromALauncherCounts()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Launcher, input: Fixture.AltTab, launcher: true);
        scenario.Launch(Fixture.Meetings, input: Fixture.Typed(0.1));
        Assert.Equal(Verdict.Opened, scenario.Activate(Fixture.Meetings, after: 5)?.Verdict);
    }

    [Fact]
    public void OnlyTheFirstTripToTheFrontCounts()
    {
        var scenario = new Scenario();
        scenario.Launch(Fixture.Meetings, input: Fixture.Clicked(0.1));
        scenario.Activate(Fixture.Meetings, after: 3);
        scenario.Activate(Fixture.Editor, input: Fixture.Clicked(0.1));
        scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1));
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Meetings)?.Verdict);
    }

    [Fact]
    public void OpeningTimeRunsOut()
    {
        var scenario = new Scenario();
        scenario.Launch(Fixture.Meetings, input: Fixture.Clicked(0.1));
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Meetings, after: 61)?.Verdict);
    }

    [Fact]
    public void AppThatQuitIsForgotten()
    {
        var scenario = new Scenario();
        scenario.Launch(Fixture.Meetings, input: Fixture.Clicked(0.1));
        scenario.Leave(Fixture.Meetings, after: 1);
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Meetings, after: 5)?.Verdict);
    }
}

public class ReturnTests
{
    [Fact]
    public void FocusGoingBackAfterAStealIsntASecondSteal()
    {
        var scenario = new Scenario();
        var theft = scenario.Activate(Fixture.Updater)!;
        var back = scenario.Activate(Fixture.Editor, after: 0.6)!;
        Assert.Equal(Verdict.Returned, back.Verdict);
        var closed = scenario.Tracker.Log.Event(theft.Id)!;
        Assert.Equal(0.6, closed.HeldFor!.Value, 3);
        Assert.Equal(Fixture.Editor, closed.Next);
        Assert.Equal(Verdict.Returned, closed.NextVerdict);
    }

    [Fact]
    public void GoingBackYourselfIsNoted()
    {
        var scenario = new Scenario();
        var theft = scenario.Activate(Fixture.Updater)!;
        scenario.Activate(Fixture.Editor, after: 2, input: Fixture.Clicked(0.1));
        Assert.True(scenario.Tracker.Log.Event(theft.Id)!.YouWentBack);
    }

    [Fact]
    public void ThiefThatKeepsComingBackStealsEachTime()
    {
        var scenario = new Scenario();
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Updater)?.Verdict);
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Editor)?.Verdict);
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Updater)?.Verdict);
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Editor)?.Verdict);
        Assert.Equal(2, scenario.Tracker.Log.Steals().Count);
    }

    [Fact]
    public void BackPastSeveralInterruptions()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Updater);
        scenario.Activate(Fixture.Meetings);
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Editor)?.Verdict);
    }

    [Fact]
    public void BackToYourAppAfterItAlreadyCameBackOnce()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Updater);
        scenario.Activate(Fixture.Editor);
        scenario.Activate(Fixture.Meetings);
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Editor)?.Verdict);
    }

    [Fact]
    public void AnAppYouStartedUsingIsYoursEvenIfItStoleFocus()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Updater);
        // You typed into it for a while, so the next app is interrupting it, not you.
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Editor, after: 30, input: Fixture.Typed(5))?.Verdict);
    }

    [Fact]
    public void AppYouSwitchedAwayFromDoesntGetToComeBackOnItsOwn()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1));
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Editor, after: 20)?.Verdict);
    }

    [Fact]
    public void AfterAPasswordPromptFocusGoesBackWithoutBlame()
    {
        var scenario = new Scenario();
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Updater)?.Verdict);
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.PasswordPrompt)?.Verdict);
        // You typed your password, then waited for it to be checked.
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Updater, after: 8, input: Fixture.Typed(3))?.Verdict);
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Editor)?.Verdict);
    }

    [Fact]
    public void UnlockingGoesBackToYourApp()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.LockScreen, after: 300);
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Editor, after: 600)?.Verdict);
    }

    [Fact]
    public void StartingWhileTheScreenIsLockedDoesntBlameTheAppThatComesBack()
    {
        var scenario = new Scenario(front: Fixture.LockScreen);
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Editor, after: 400)?.Verdict);
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Updater)?.Verdict);
    }

    [Fact]
    public void SomethingElseJumpingInAfterUnlockingStillSteals()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.LockScreen, after: 300);
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Updater, after: 600)?.Verdict);
    }

    [Fact]
    public void AppThatQuitHandsFocusToTheNextApp()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1));
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Meetings, after: 40, previousHasLeft: true)?.Verdict);
    }

    [Fact]
    public void MinimizeNoticedJustBeforeCounts()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1));
        scenario.Leave(Fixture.Notes, after: 10);
        Assert.Equal(Verdict.Returned, scenario.Activate(Fixture.Meetings, after: 0.2)?.Verdict);
    }

    [Fact]
    public void MinimizeLongBeforeDoesntCount()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1));
        scenario.Leave(Fixture.Notes, after: 10);
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Meetings, after: 5)?.Verdict);
    }

    [Fact]
    public void StartingOverForgetsWhoWasInFront()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Updater);
        scenario.Tracker.Start(Fixture.Pid(Fixture.Notes), Fixture.Notes, scenario.Clock.AddSeconds(-3600));
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Editor)?.Verdict);
    }
}

public class SystemTests
{
    public static IEnumerable<object[]> OnRequest() => SystemApps.OnRequest.Order().Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(OnRequest))]
    public void PartsOfWindowsYouOpenNeverSteal(string fileName)
    {
        var scenario = new Scenario();
        var app = new AppInfo(fileName, @"C:\Windows\SystemApps\Shell\" + fileName);
        var e = scenario.Activate(app)!;
        Assert.Equal(Verdict.Switched, e.Verdict);
        Assert.Equal(Cause.SystemUI, e.Cause);
        Assert.False(scenario.IsPending(e.Id));
    }

    [Fact]
    public void LeavingStartForAnotherAppIsYours()
    {
        var scenario = new Scenario();
        scenario.Activate(new AppInfo("Start", @"C:\Windows\SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe"));
        Assert.Equal(Verdict.Switched, scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1))?.Verdict);
    }

    [Fact]
    public void AnAppNamedLikeAPartOfWindowsElsewhereGetsNoPass()
    {
        // Something calling itself SearchHost.exe outside the Windows folder is judged like any app.
        var impostor = new AppInfo("SearchHost", @"C:\Users\sam\AppData\Roaming\SearchHost.exe");
        Assert.Equal(Verdict.Stole, new Scenario().Activate(impostor)?.Verdict);
        Assert.False(SystemApps.IsInterruption(new AppInfo("consent", @"C:\Temp\consent.exe")));
        Assert.False(SystemApps.IsInterruption(new AppInfo("consent", @"C:\Windows\..\Temp\consent.exe")));
    }
}

public class StartupTests
{
    [Fact]
    public void AppsSortingThemselvesOutAtSignInArentSteals()
    {
        var scenario = new Scenario(front: Fixture.Notes);
        scenario.Tracker.Start(Fixture.Pid(Fixture.Notes), Fixture.Notes, scenario.Clock);
        var e = scenario.Activate(Fixture.Updater, after: 4, input: Fixture.Idle)!;
        Assert.Equal(Verdict.Unsure, e.Verdict);
        Assert.Equal(Cause.Startup, e.Cause);
        // Once you've clicked, steals count again.
        Assert.Equal(Verdict.Stole, scenario.Activate(Fixture.Meetings, after: 20, input: Fixture.Idle with { Click = 10 })?.Verdict);
    }

    [Fact]
    public void FirstAppWithNothingInFrontJustStartsTracking()
    {
        var tracker = new FocusTracker();
        Assert.Null(tracker.AppActivated(new Activation(1, Fixture.Editor, Fixture.Start, Fixture.Idle)));
        Assert.Equal(1, tracker.FrontPid);
        Assert.Empty(tracker.Log.Events);
    }

    [Fact]
    public void QuittingClosesTheEventInFront()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1))!;
        scenario.Tracker.Finish(scenario.Clock.AddSeconds(30));
        Assert.Equal(30, scenario.Tracker.Log.Event(e.Id)?.HeldFor);
        Assert.Null(scenario.Tracker.FrontEventId);
    }

    [Fact]
    public void StartingOverClosesTheEventInFront()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1))!;
        Assert.Equal(e.Id, scenario.Tracker.FrontEventId);
        scenario.Tracker.Start(1, Fixture.Editor, scenario.Clock.AddSeconds(10));
        Assert.Equal(10, scenario.Tracker.Log.Event(e.Id)?.HeldFor);
    }
}

public class BookkeepingTests
{
    [Fact]
    public void OwnWindowIsNeverLogged()
    {
        var scenario = new Scenario();
        Assert.Null(scenario.Activate(Fixture.Own, input: Fixture.Clicked(0.1)));
        Assert.Empty(scenario.Events);
        var back = scenario.Activate(Fixture.Editor, input: Fixture.Clicked(0.1))!;
        Assert.Equal(Fixture.Own, back.Previous);
        Assert.Equal(Verdict.Switched, back.Verdict);
    }

    [Fact]
    public void OwnWindowClosesTheEventBeforeIt()
    {
        var scenario = new Scenario();
        var theft = scenario.Activate(Fixture.Updater)!;
        scenario.Activate(Fixture.Own, after: 4, input: Fixture.Clicked(0.1));
        var closed = scenario.Tracker.Log.Event(theft.Id)!;
        Assert.Equal(4, closed.HeldFor);
        Assert.Equal(Fixture.Own, closed.Next);
        Assert.Null(closed.NextVerdict);
    }

    [Fact]
    public void AppAlreadyInFrontIsIgnored()
    {
        var scenario = new Scenario();
        Assert.Null(scenario.Activate(Fixture.Editor));
        Assert.Empty(scenario.Events);
    }

    [Fact]
    public void LastEventStaysOpen()
    {
        var scenario = new Scenario();
        var e = scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1))!;
        Assert.Null(scenario.Tracker.Log.Event(e.Id)?.HeldFor);
    }

    [Fact]
    public void ClearingTheLogDoesntBringEventsBack()
    {
        var scenario = new Scenario();
        scenario.Activate(Fixture.Updater);
        scenario.Tracker.ClearLog();
        var next = scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1))!;
        Assert.Equal([next], scenario.Events);
    }

    [Fact]
    public void LogLimitHolds()
    {
        var scenario = new Scenario(limit: 3);
        for (var i = 0; i < 10; i++) scenario.Activate(i % 2 == 0 ? Fixture.Notes : Fixture.Editor, input: Fixture.Clicked(0.1));
        Assert.Equal(3, scenario.Events.Count);
    }

    [Fact]
    public void OddInputTimesAreClamped()
    {
        var e = new Scenario().Activate(Fixture.Updater, input: new InputSnapshot(double.NaN, -4, double.PositiveInfinity, 1e12))!;
        Assert.Equal(InputSnapshot.Longest, e.Input.Click);
        Assert.Equal(0, e.Input.KeyPress);
        Assert.Equal(InputSnapshot.Longest, e.Input.ModifierChange);
        Assert.Equal(InputSnapshot.Longest, e.Input.Gesture);
    }

    [Fact]
    public void FrontIsTracked()
    {
        var scenario = new Scenario();
        Assert.Equal(Fixture.Pid(Fixture.Editor), scenario.Tracker.FrontPid);
        scenario.Activate(Fixture.Notes, input: Fixture.Clicked(0.1));
        Assert.Equal(Fixture.Pid(Fixture.Notes), scenario.Tracker.FrontPid);
    }

    [Fact]
    public void LongSessionsKeepWorking()
    {
        var scenario = new Scenario();
        // Thirty seconds apart, with your last input a minute ago: you never touch the thief.
        for (var round = 0; round < 200; round++) scenario.Activate(round % 2 == 0 ? Fixture.Updater : Fixture.Editor, after: 30);
        Assert.Equal(100, scenario.Tracker.Log.Steals().Count);
        Assert.Equal(100, scenario.Events.Count(e => e.Verdict == Verdict.Returned));
    }
}
