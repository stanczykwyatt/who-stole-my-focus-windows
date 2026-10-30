namespace WhoStoleMyFocus.Core.Tests;

public class InputKindTests
{
    [Theory]
    [InlineData(0x41, 0, InputKind.Key)]      // A down
    [InlineData(0x41, 1, InputKind.Ignored)]  // A up
    [InlineData(0x0D, 0, InputKind.Key)]      // Enter down
    [InlineData(0x12, 0, InputKind.Modifier)] // Alt down
    [InlineData(0x12, 1, InputKind.Modifier)] // Alt up: the end of Alt+Tab
    [InlineData(0xA0, 1, InputKind.Modifier)] // left Shift up
    [InlineData(0x5B, 0, InputKind.Modifier)] // Windows key
    [InlineData(0xFF, 0, InputKind.Ignored)]
    [InlineData(0x00, 0, InputKind.Ignored)]
    public void Keyboard(int key, int flags, InputKind expected) => Assert.Equal(expected, InputKinds.Keyboard((ushort)key, (ushort)flags));

    [Theory]
    [InlineData(0x0001, InputKind.Click)] // left down
    [InlineData(0x0002, InputKind.Click)] // left up
    [InlineData(0x0010, InputKind.Click)] // middle down
    [InlineData(0x0200, InputKind.Click)] // fifth button up
    [InlineData(0x0400, InputKind.Ignored)] // wheel
    [InlineData(0x0800, InputKind.Ignored)] // horizontal wheel
    [InlineData(0x0000, InputKind.Ignored)] // a move
    public void Mouse(int flags, InputKind expected) => Assert.Equal(expected, InputKinds.Mouse((ushort)flags));
}

public class InputTimesTests
{
    [Fact]
    public void NothingYet()
    {
        Assert.Equal(InputSnapshot.None, new InputTimes().At(5_000));
    }

    [Fact]
    public void TimesAreSecondsSinceEachKind()
    {
        var times = new InputTimes();
        times.Record(InputKind.Click, 1_000);
        times.Record(InputKind.Key, 4_000);
        times.Record(InputKind.Modifier, 4_500);
        times.Record(InputKind.Ignored, 4_900);
        var now = times.At(5_000, commandKeysHeld: true);
        Assert.Equal(4, now.Click);
        Assert.Equal(1, now.KeyPress);
        Assert.Equal(0.5, now.ModifierChange);
        Assert.Equal(InputSnapshot.Longest, now.Gesture);
        Assert.True(now.CommandKeysHeld);
    }

    [Fact]
    public void AnEarlierMomentOnlyCountsInputUpToThen()
    {
        // An app that launched at 3 s is judged by the input before 3 s, not the typing after.
        var times = new InputTimes();
        times.Record(InputKind.Click, 2_900);
        times.Record(InputKind.Key, 3_200);
        var launch = times.At(3_000);
        Assert.Equal(0.1, launch.Click, 6);
        Assert.Equal(InputSnapshot.Longest, launch.KeyPress);
        Assert.Equal(Cause.Click, new CauseRules().CauseOf(launch, false));
    }

    [Fact]
    public void KeepsOnlyRecentTimes()
    {
        var times = new InputTimes();
        for (var i = 0; i < 1_000; i++) times.Record(InputKind.Key, i * 100);
        Assert.Equal(0, times.At(99_900).KeyPress);
        // The oldest are forgotten, so a moment long ago reads as no input.
        Assert.Equal(InputSnapshot.Longest, times.At(5).KeyPress);
    }
}

public class AppNamesTests
{
    [Fact]
    public void PrefersTheDescription()
    {
        var app = AppNames.From(@"C:\Program Files\CloudSync\bin\SyncHelper.exe", "SyncHelper", "CloudSync");
        Assert.Equal(Fixture.Updater, app);
        Assert.Equal("SyncHelper (CloudSync)", Describe.Name(app));
    }

    [Fact]
    public void FallsBackToTheFileName()
    {
        Assert.Equal("tool", AppNames.From(@"C:\Tools\tool.EXE", "  ", null).Name);
        Assert.Equal("runner", AppNames.From(@"C:\Tools\runner", null, null).Name);
    }

    [Fact]
    public void CleansTextFromOtherApps()
    {
        var app = AppNames.From(@"C:\x\evil.exe", "Trusted\u202E exe.\u0007", new string('x', 200));
        Assert.Equal("Trusted exe.", app.Name);
        Assert.Equal(80, app.Product!.Length);
        Assert.Null(AppNames.Clean("\u200B\u200F"));
    }
}
