using System.Diagnostics;
using WhoStoleMyFocus.Core;
using static WhoStoleMyFocus.Native;

namespace WhoStoleMyFocus;

/// <summary>
/// Watches which app is in front and tells the model. Windows announces each new foreground window to
/// any app that asks; the window is only used to find its process and its class, never read.
/// </summary>
internal sealed class FocusMonitor : IDisposable
{
    /// <summary>
    /// Parts of the Windows shell that pass through the front on the way somewhere else: the Alt+Tab
    /// switcher, Task View, the taskbar and the desktop. They're skipped, like the Mac's ⌘Tab switcher,
    /// so they're never logged or blamed as what focus was taken from.
    /// </summary>
    static readonly HashSet<string> PassThrough = new(StringComparer.Ordinal)
    {
        "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "TaskSwitcherWnd", "ForegroundStaging",
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
    };

    sealed record Known(AppInfo App, DateTime? Launched, IntPtr Wait, bool LaunchNoted);

    readonly FocusModel model;
    readonly InputClock clock;
    readonly WinEventProc callback;
    readonly List<IntPtr> hooks = [];
    readonly System.Windows.Forms.Timer exitCheck = new() { Interval = 1000 };
    /// <summary>Processes seen in front lately, by ID and start time, each with a handle that can only wait for it to quit.</summary>
    readonly RecentProcesses<Known> known = new(24);
    readonly Native.IVirtualDesktopManager? desktops;
    IntPtr frontWindow;
    public bool Paused { get; set; }

    public FocusMonitor(FocusModel model, InputClock clock)
    {
        this.model = model;
        this.clock = clock;
        callback = OnWinEvent;
        exitCheck.Tick += (_, _) => Safely(CheckExits);
        try
        {
            desktops = (Native.IVirtualDesktopManager)new VirtualDesktopManager();
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            desktops = null;
        }
    }

    static DateTime Now => DateTime.UtcNow;

    public void Start()
    {
        hooks.Add(SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0, WINEVENT_OUTOFCONTEXT));
        hooks.Add(SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, callback, 0, 0, WINEVENT_OUTOFCONTEXT));
        exitCheck.Start();
        StartFromForeground();
    }

    /// <summary>Starts over from whatever is in front now, without logging it.</summary>
    public void StartFromForeground() => Safely(() =>
    {
        frontWindow = GetForegroundWindow();
        if (OwnerOf(frontWindow) is not { } pid || Identify(pid) is not { } found) return;
        model.Start(pid, found.App, Now, SystemApps.IsLauncher(found.App));
    });

    /// <summary>Nothing that goes wrong while looking at one switch may take the app down.</summary>
    static void Safely(Action work)
    {
        try
        {
            work();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Debug.WriteLine(error);
        }
    }

    void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) => Safely(() =>
    {
        if (Paused || idObject != 0 || hwnd == IntPtr.Zero) return;
        if (eventType == EVENT_SYSTEM_MINIMIZESTART)
        {
            if (OwnerOf(hwnd) is { } minimized) model.AppLeft(minimized, Now);
            return;
        }
        Activated(hwnd, time);
    });

    void Activated(IntPtr hwnd, uint time)
    {
        if (PassThrough.Contains(ClassOf(hwnd))) return;
        if (OwnerOf(hwnd) is not { } pid || Identify(pid) is not { } found) return;
        // Input that caused the switch may still be queued behind the announcement of it.
        clock.Drain();
        var date = Now;
        var previousWindow = frontWindow;
        var previousHasLeft = model.FrontPid is { } front && front != pid
            && (HasLeft(front) || (previousWindow != IntPtr.Zero && IsIconic(previousWindow)));
        frontWindow = hwnd;
        // An app that started a moment ago: note whether you started it, from the input before it did.
        if (found.Launched is { } launched && (date - launched).TotalSeconds <= 60 && !found.LaunchNoted)
        {
            model.AppLaunched(pid, launched, clock.SnapshotAt(launched, date, Environment.TickCount64));
            known.Add(pid, launched.Ticks, found with { LaunchNoted = true });
        }
        var activation = new Activation(pid, found.App, date, clock.SnapshotAt(time), found.Launched, SystemApps.IsLauncher(found.App), previousHasLeft);
        if (model.AppActivated(activation) is not { } e || !model.IsPending(e.Id)) return;
        var timer = new System.Windows.Forms.Timer { Interval = (int)(model.FollowUpDelay * 1000) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            Safely(() => FollowUpOn(e, previousWindow));
        };
        timer.Start();
    }

    void FollowUpOn(FocusEvent e, IntPtr previousWindow)
    {
        clock.Drain();
        var date = Now;
        var elapsed = (date - e.Date).TotalSeconds;
        var keptTyping = FollowUp.KeptTyping(elapsed, model.FollowUpDelay, clock.Snapshot());
        model.FinishFollowUp(e.Id, keptTyping, DesktopChanged(previousWindow), date, pid =>
            HasLeft(pid) || (previousWindow != IntPtr.Zero && e.Previous is not null && IsIconic(previousWindow)));
    }

    /// <summary>Whether the window that was in front is now on another virtual desktop than the one showing.</summary>
    bool DesktopChanged(IntPtr previousWindow)
    {
        if (desktops is null || previousWindow == IntPtr.Zero) return false;
        try
        {
            return desktops.IsWindowOnCurrentVirtualDesktop(previousWindow, out var onCurrent) == 0 && !onCurrent;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    static string ClassOf(IntPtr hwnd)
    {
        var name = new char[64];
        var length = GetClassName(hwnd, name, name.Length);
        return length > 0 ? new string(name, 0, length) : "";
    }

    /// <summary>The process that owns a window. For Store apps, the app inside the frame Windows draws around it.</summary>
    static int? OwnerOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        if (!PathOf((int)pid)?.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ?? true) return (int)pid;
        var inner = pid;
        EnumChildWindows(hwnd, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var childPid);
            if (childPid != 0 && childPid != pid)
            {
                inner = childPid;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return (int)inner;
    }

    Known? Identify(int pid)
    {
        var launched = LaunchedAt(pid);
        var started = launched?.Ticks ?? 0;
        if (known.TryGet(pid, started, out var found)) return found;
        var path = PathOf(pid);
        AppInfo app;
        if (path is null)
        {
            // Windows won't give the path of another user's or the system's processes, like a User
            // Account Control prompt. Their name is still public.
            app = new AppInfo(ProcessName(pid) is { } name ? AppNames.Clean(name) ?? name : "An app Windows won't name");
        }
        else
        {
            string? description = null, product = null;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                description = info.FileDescription;
                product = info.ProductName;
            }
            catch (FileNotFoundException)
            {
            }
            app = AppNames.From(path, description, product);
        }
        var wait = OpenProcess(SYNCHRONIZE, false, (uint)pid);
        found = new Known(app, launched, wait, false);
        foreach (var old in known.Add(pid, started, found)) Close(old);
        return found;
    }

    static string? ProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    static string? PathOf(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static DateTime? LaunchedAt(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            return GetProcessTimes(handle, out var created, out _, out _, out _) ? DateTime.FromFileTimeUtc(created) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Whether a process has quit. One that can't be found at all counts as gone, like on the Mac.</summary>
    bool HasLeft(int pid)
    {
        foreach (var (seen, entry) in known.All)
        {
            if (seen == pid && entry.Wait != IntPtr.Zero) return WaitForSingleObject(entry.Wait, 0) == WAIT_OBJECT_0;
        }
        var handle = OpenProcess(SYNCHRONIZE, false, (uint)pid);
        if (handle == IntPtr.Zero) return true;
        try
        {
            return WaitForSingleObject(handle, 0) == WAIT_OBJECT_0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    void CheckExits()
    {
        foreach (var (pid, entry) in known.All.ToList())
        {
            if (entry.Wait == IntPtr.Zero || WaitForSingleObject(entry.Wait, 0) != WAIT_OBJECT_0) continue;
            model.AppLeft(pid, Now, entry.App);
            if (known.Remove(pid, out var gone)) Close(gone);
        }
    }

    static void Close(Known entry)
    {
        if (entry.Wait != IntPtr.Zero) CloseHandle(entry.Wait);
    }

    public void Dispose()
    {
        foreach (var hook in hooks) UnhookWinEvent(hook);
        hooks.Clear();
        exitCheck.Dispose();
        foreach (var (_, entry) in known.All.ToList()) Close(entry);
    }
}
