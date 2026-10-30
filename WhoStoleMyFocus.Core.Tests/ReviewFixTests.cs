using System.Text;

namespace WhoStoleMyFocus.Core.Tests;

public class RecentProcessesTests
{
    [Fact]
    public void DropsTheLeastRecentlySeenNeverTheNewest()
    {
        var cache = new RecentProcesses<string>(16);
        var dropped = new List<string>();
        for (var pid = 1; pid <= 30; pid++) dropped.AddRange(cache.Add(pid, 100, $"p{pid}"));
        Assert.Equal(16, cache.Count);
        Assert.Equal(Enumerable.Range(1, 14).Select(p => $"p{p}"), dropped);
        Assert.True(cache.TryGet(30, 100, out var newest) && newest == "p30");
        Assert.True(cache.TryGet(15, 100, out _));
    }

    [Fact]
    public void SeeingAnEntryAgainKeepsIt()
    {
        var cache = new RecentProcesses<string>(2);
        cache.Add(1, 0, "a");
        cache.Add(2, 0, "b");
        Assert.True(cache.TryGet(1, 0, out _));
        Assert.Equal(["b"], cache.Add(3, 0, "c"));
    }

    [Fact]
    public void AReusedProcessIdIsANewProcess()
    {
        var cache = new RecentProcesses<string>(8);
        cache.Add(7, 1000, "old app");
        Assert.False(cache.TryGet(7, 2000, out _));
        Assert.Equal(["old app"], cache.Add(7, 2000, "new app"));
        Assert.True(cache.TryGet(7, 2000, out var now) && now == "new app");
        Assert.Empty(cache.Add(7, 2000, "same process, refreshed"));
    }
}

public class HardenedLogTests
{
    [Theory]
    [InlineData("""{"version": 1.5, "events": []}""")]
    [InlineData("""{"version": 3000000000, "events": []}""")]
    [InlineData("""{"version": 1}""")]
    [InlineData("""{"version": 1, "events": [null]}""")]
    [InlineData("""{"version": 1, "events": [{"app": {"name": "x"}}]}""")]
    [InlineData("""{"version": 1, "events": [{"id": "6F9619FF-8B86-D011-B42D-00C04FC964FF", "date": 1e15, "app": {"name": "x"}, "cause": "none", "verdict": "stole"}]}""")]
    [InlineData("[1, 2]")]
    public void DamagedFilesAreRefusedAsJson(string json) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => LogFile.Decode(Encoding.UTF8.GetBytes(json)));
}

public class SettingsEventTests
{
    [Fact]
    public void EveryChangeOfMindIsReported()
    {
        var model = new FocusModel(null, new AppSettings(), null);
        var changes = 0;
        model.SettingsChanged += () => changes++;
        model.Ignore(Fixture.Updater);
        model.Ignore(Fixture.Updater);
        model.StopIgnoring(Fixture.Updater.Key);
        model.Threshold = 2;
        model.Notifies = true;
        Assert.Equal(4, changes);
    }
}

public class GestureInputTests
{
    [Theory]
    [InlineData(0x04, InputKind.Click)]   // touchscreen
    [InlineData(0x02, InputKind.Click)]   // pen
    [InlineData(0x05, InputKind.Gesture)] // touchpad
    [InlineData(0x01, InputKind.Ignored)]
    public void Digitizers(int usage, InputKind expected) => Assert.Equal(expected, InputKinds.Digitizer((ushort)usage));

    [Fact]
    public void TouchpadTimesReachTheSnapshot()
    {
        var times = new InputTimes();
        times.Record(InputKind.Gesture, 1_000);
        Assert.Equal(0.5, times.At(1_500).Gesture);
    }

    [Fact]
    public void AMinuteOfTypingIsKeptForLaunches()
    {
        // Enter on a command that opens an app, then typing for a while before it comes up.
        var times = new InputTimes();
        times.Record(InputKind.Key, 10_000);
        for (var i = 1; i <= 300; i++) times.Record(InputKind.Key, 10_000 + i * 100);
        Assert.Equal(0, times.At(10_000).KeyPress);
        times.Record(InputKind.Key, 200_000);
        Assert.Equal(InputSnapshot.Longest, times.At(10_000).KeyPress);
    }
}

public class DurationLimitTests
{
    [Fact]
    public void HugeValuesDontOverflow() => Assert.Equal("10000 days", Describe.Duration(1e18));
}
