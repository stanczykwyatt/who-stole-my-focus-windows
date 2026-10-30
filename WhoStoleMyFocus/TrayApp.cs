using System.Runtime.InteropServices;
using Microsoft.Win32;
using WhoStoleMyFocus.Core;

namespace WhoStoleMyFocus;

/// <summary>The app itself: a tray icon whose menu lists the latest steals, and the log window.</summary>
internal sealed class TrayApp : ApplicationContext
{
    readonly FocusModel model;
    readonly InputClock clock = new();
    readonly FocusMonitor monitor;
    readonly NotifyIcon trayIcon;
    readonly ContextMenuStrip menu = new();
    readonly System.Windows.Forms.Timer saveDelay = new() { Interval = 3000 };
    readonly Icon plainIcon;
    readonly Icon alertIcon;
    LogWindow? logWindow;

    public TrayApp()
    {
        model = new FocusModel(LogStore.LoadLog(), LogStore.LoadSettings(), Environment.ProcessId);
        monitor = new FocusMonitor(model, clock);
        plainIcon = AppIcon.Load(SystemInformation.SmallIconSize);
        alertIcon = WithDot(plainIcon);

        trayIcon = new NotifyIcon { Icon = plainIcon, Text = "Who Stole My Focus", ContextMenuStrip = menu, Visible = true };
        menu.Opening += (_, _) => BuildMenu();
        // A left click opens the same menu as a right click, like the Mac menu bar icon.
        trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(trayIcon, null);
        };
        trayIcon.BalloonTipClicked += (_, _) => ShowLog();

        model.Changed += () =>
        {
            saveDelay.Stop();
            saveDelay.Start();
            UpdateIcon();
        };
        model.Stole += e =>
        {
            UpdateIcon();
            if (model.Notifies) trayIcon.ShowBalloonTip(5000, Describe.Headline(e), Describe.Reason(e), ToolTipIcon.None);
        };
        model.SettingsChanged += () => LogStore.Save(model.Settings);
        saveDelay.Tick += (_, _) =>
        {
            saveDelay.Stop();
            LogStore.Save(model.Log);
        };
        // Signing back in or reconnecting starts over from whatever is in front then.
        SystemEvents.SessionSwitch += OnSessionSwitch;
        // A tray app is usually closed by shutting down or signing out, not Quit, so save then too.
        SystemEvents.SessionEnding += OnSessionEnding;

        try
        {
            StartupRegistration.RefreshPath();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
        }
        monitor.Start();
        if (!clock.IsListening)
        {
            trayIcon.ShowBalloonTip(8000, "Who Stole My Focus can't see input",
                "Windows didn't allow it to note when you click or type, so every switch will look like it came out of nowhere.", ToolTipIcon.Warning);
        }
    }

    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect:
                monitor.Paused = true;
                break;
            case SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect or SessionSwitchReason.SessionLogon:
                monitor.Paused = false;
                monitor.StartFromForeground();
                break;
        }
    }

    void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Save();

    void Save()
    {
        saveDelay.Stop();
        model.Finish(DateTime.UtcNow);
        LogStore.Save(model.Log);
        LogStore.Save(model.Settings);
    }

    void BuildMenu()
    {
        menu.Items.Clear();
        var now = DateTime.UtcNow;
        var steals = model.Steals(now.AddDays(-1));
        menu.Items.Add(new ToolStripLabel(steals.Count switch
        {
            0 => "No steals in the last 24 hours",
            1 => "1 steal in the last 24 hours",
            _ => $"{steals.Count} steals in the last 24 hours",
        }) { Font = new Font(menu.Font, FontStyle.Bold) });
        foreach (var e in steals.Take(6))
        {
            var item = new ToolStripMenuItem($"{Describe.Name(e.App)} · {Describe.Relative(e.Date, now)}") { ToolTipText = Describe.Reason(e) };
            item.Click += (_, _) => ShowLog();
            menu.Items.Add(item);
        }
        var top = model.Offenders(now.AddDays(-7)).FirstOrDefault();
        if (top is not null)
        {
            menu.Items.Add(new ToolStripLabel($"Top this week: {Describe.Name(top.App)} ({top.Count})") { ForeColor = SystemColors.GrayText });
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Show Log…", null, (_, _) => ShowLog());
        menu.Items.Add(new ToolStripMenuItem("Notify When Something Steals Focus", null, (_, _) =>
        {
            model.Notifies = !model.Notifies;
        }) { Checked = model.Notifies });

        var threshold = new ToolStripMenuItem("Count a Switch as Yours Within");
        foreach (var choice in CauseRules.ThresholdChoices)
        {
            var seconds = choice;
            threshold.DropDownItems.Add(new ToolStripMenuItem($"{Describe.Duration(seconds)} of a Click or Key", null, (_, _) =>
            {
                model.Threshold = seconds;
            }) { Checked = model.Threshold == seconds });
        }
        menu.Items.Add(threshold);

        var ignored = new ToolStripMenuItem("Ignored Apps");
        foreach (var (key, name) in model.IgnoredApps())
        {
            ignored.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) =>
            {
                model.StopIgnoring(key);
            }) { Checked = true, ToolTipText = "Click to stop ignoring it" });
        }
        if (ignored.DropDownItems.Count == 0) ignored.DropDownItems.Add(new ToolStripMenuItem("None") { Enabled = false });
        menu.Items.Add(ignored);

        menu.Items.Add(new ToolStripMenuItem("Start with Windows", null, (_, _) =>
        {
            try
            {
                StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
            }
        }) { Checked = SafeIsStartupEnabled() });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        model.MarkSeen();
        UpdateIcon();
    }

    static bool SafeIsStartupEnabled()
    {
        try
        {
            return StartupRegistration.IsEnabled;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }

    void ShowLog()
    {
        if (logWindow is null || logWindow.IsDisposed)
        {
            logWindow = new LogWindow(model);
            logWindow.FormClosed += (_, _) => UpdateIcon();
        }
        logWindow.Show();
        logWindow.Activate();
        UpdateIcon();
    }

    void UpdateIcon()
    {
        trayIcon.Icon = model.UnseenSteals > 0 ? alertIcon : plainIcon;
        trayIcon.Text = model.UnseenSteals switch
        {
            0 => "Who Stole My Focus",
            1 => "Who Stole My Focus: 1 new steal",
            var n => $"Who Stole My Focus: {n} new steals",
        };
    }

    /// <summary>The icon with an ice-blue dot in the corner, for steals you haven't looked at yet.</summary>
    static Icon WithDot(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var size = bitmap.Width * 0.42f;
            var dot = new RectangleF(bitmap.Width - size, 0, size, size);
            using var ring = new SolidBrush(Color.FromArgb(0x20, 0x22, 0x26));
            using var fill = new SolidBrush(Color.FromArgb(0x9E, 0xD1, 0xFF));
            graphics.FillEllipse(ring, dot);
            dot.Inflate(-size * 0.18f, -size * 0.18f);
            graphics.FillEllipse(fill, dot);
        }
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr handle);

    void Quit()
    {
        Save();
        trayIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.SessionEnding -= OnSessionEnding;
            monitor.Dispose();
            clock.Dispose();
            trayIcon.Dispose();
            menu.Dispose();
            saveDelay.Dispose();
            plainIcon.Dispose();
            alertIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
