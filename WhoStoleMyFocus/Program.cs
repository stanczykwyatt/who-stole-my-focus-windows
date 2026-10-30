namespace WhoStoleMyFocus;

static class Program
{
    [STAThread]
    static void Main()
    {
        // One copy at a time, so two never log the same switches.
        using var instance = new Mutex(initiallyOwned: true, @"Local\WhoStoleMyFocus.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "Who Stole My Focus is already running. You'll find it in the system tray, next to the clock.",
                "Who Stole My Focus",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        // A problem in one menu click or timer shouldn't put a stack trace on screen or stop the log.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => System.Diagnostics.Debug.WriteLine(e.Exception);
        using var app = new TrayApp();
        Application.Run(app);
    }
}
