using System.Buffers.Binary;
using System.Runtime.InteropServices;
using WhoStoleMyFocus.Core;
using static WhoStoleMyFocus.Native;

namespace WhoStoleMyFocus;

/// <summary>
/// Notes when you last clicked, pressed a key or a modifier, touched the screen or the touchpad, through
/// Windows' raw input, which any app may ask for without a permission. From each event it reads what
/// kind of device sent it, and for keys only whether the key is a modifier (Shift, Ctrl, Alt, Windows)
/// and whether it went up. It never reads which key it was beyond that, never sees text, can't block or
/// change input, and keeps only the times, for a minute.
/// </summary>
/// <remarks>
/// Windows doesn't send raw input to an ordinary app while an app running as administrator is in front,
/// so input in those apps isn't seen.
/// </remarks>
internal sealed class InputClock : NativeWindow, IDisposable
{
    static readonly IntPtr MessageOnly = new(-3);
    readonly InputTimes times = new();
    readonly int headerSize = 8 + 2 * IntPtr.Size;
    /// <summary>The HID usage of each touch device seen, by its handle: touchscreen, pen or touchpad.</summary>
    readonly Dictionary<IntPtr, ushort> digitizers = [];

    public InputClock()
    {
        CreateHandle(new CreateParams { Caption = "Who Stole My Focus input clock", Parent = MessageOnly });
        RAWINPUTDEVICE[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = Handle }, // keyboards
            new() { UsagePage = 0x01, Usage = 0x02, Flags = RIDEV_INPUTSINK, Target = Handle }, // mice
        ];
        IsListening = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        // Touchscreens, pens and touchpads, where there are any. Not having them is fine.
        RAWINPUTDEVICE[] touch =
        [
            new() { UsagePage = 0x0D, Usage = 0x04, Flags = RIDEV_INPUTSINK, Target = Handle },
            new() { UsagePage = 0x0D, Usage = 0x02, Flags = RIDEV_INPUTSINK, Target = Handle },
            new() { UsagePage = 0x0D, Usage = 0x05, Flags = RIDEV_INPUTSINK, Target = Handle },
        ];
        RegisterRawInputDevices(touch, (uint)touch.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// <summary>False if Windows refused, in which case every switch looks like it had no input before it.</summary>
    public bool IsListening { get; }

    /// <summary>
    /// Message times are the 32-bit millisecond tick, which wraps every 49 days; they're turned into
    /// the 64-bit tick by how far behind now they are.
    /// </summary>
    static long Tick(uint messageTime)
    {
        var now = Environment.TickCount64;
        return now - (uint)((uint)now - messageTime);
    }

    /// <summary>
    /// Handles input still waiting in the queue. Windows can announce a new foreground window before the
    /// key or click that caused it has been delivered here, like the Alt let go at the end of Alt+Tab.
    /// </summary>
    public void Drain()
    {
        for (var i = 0; i < 256 && PeekMessage(out var message, Handle, WM_INPUT, WM_INPUT, PM_REMOVE); i++)
        {
            Read(message.LParam, message.Time);
        }
    }

    /// <summary>Input times as of a moment given as a message time, like the foreground change's own.</summary>
    public InputSnapshot SnapshotAt(uint messageTime) => times.At(Tick(messageTime), CommandKeysHeld());

    /// <summary>Input times now.</summary>
    public InputSnapshot Snapshot() => times.At(Environment.TickCount64, CommandKeysHeld());

    /// <summary>Input times as they were at <paramref name="date"/>, for an app that launched then.</summary>
    public InputSnapshot SnapshotAt(DateTime date, DateTime now, long nowTick)
    {
        var ago = (long)(now - date).TotalMilliseconds;
        return times.At(nowTick - Math.Max(0, ago));
    }

    /// <summary>Whether Ctrl, Alt or a Windows key is held right now. Asks about those four keys only.</summary>
    static bool CommandKeysHeld() =>
        new[] { VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT) Read(m.LParam, (uint)GetMessageTime());
        base.WndProc(ref m);
    }

    void Read(IntPtr handle, uint time)
    {
        try
        {
            uint size = 0;
            GetRawInputData(handle, RID_INPUT, IntPtr.Zero, ref size, (uint)headerSize);
            if (size == 0 || size > 4096) return;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(handle, RID_INPUT, buffer, ref size, (uint)headerSize) != size) return;
                var bytes = new byte[Math.Min((int)size, headerSize + 8)];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                var data = bytes.AsSpan(headerSize);
                var kind = type switch
                {
                    // RAWKEYBOARD: MakeCode, Flags, Reserved, VKey. Only Flags and VKey are read.
                    RIM_TYPEKEYBOARD when data.Length >= 8 => InputKinds.Keyboard(
                        virtualKey: BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), flags: BinaryPrimitives.ReadUInt16LittleEndian(data[2..])),
                    // RAWMOUSE: usFlags, padding, usButtonFlags. Only the button flags are read.
                    RIM_TYPEMOUSE when data.Length >= 6 => InputKinds.Mouse(BinaryPrimitives.ReadUInt16LittleEndian(data[4..])),
                    // A touch device's report is never read: only which kind of device sent it.
                    RIM_TYPEHID => InputKinds.Digitizer(UsageOf(Marshal.ReadIntPtr(buffer, 8))),
                    _ => InputKind.Ignored,
                };
                times.Record(kind, Tick(time));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or OutOfMemoryException)
        {
        }
    }

    /// <summary>The HID usage of a device, from its RID_DEVICE_INFO: dwSize, dwType, then for HID the vendor, product, version, usage page and usage.</summary>
    ushort UsageOf(IntPtr device)
    {
        if (digitizers.TryGetValue(device, out var known)) return known;
        uint size = 32;
        var info = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.WriteInt32(info, 0, 32);
            ushort usage = 0;
            if (GetRawInputDeviceInfo(device, RIDI_DEVICEINFO, info, ref size) != unchecked((uint)-1)
                && (uint)Marshal.ReadInt32(info, 4) == RIM_TYPEHID && (ushort)Marshal.ReadInt16(info, 20) == 0x0D)
            {
                usage = (ushort)Marshal.ReadInt16(info, 22);
            }
            if (digitizers.Count > 64) digitizers.Clear();
            digitizers[device] = usage;
            return usage;
        }
        finally
        {
            Marshal.FreeHGlobal(info);
        }
    }

    public void Dispose()
    {
        RAWINPUTDEVICE[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_REMOVE, Target = IntPtr.Zero },
            new() { UsagePage = 0x01, Usage = 0x02, Flags = RIDEV_REMOVE, Target = IntPtr.Zero },
            new() { UsagePage = 0x0D, Usage = 0x04, Flags = RIDEV_REMOVE, Target = IntPtr.Zero },
            new() { UsagePage = 0x0D, Usage = 0x02, Flags = RIDEV_REMOVE, Target = IntPtr.Zero },
            new() { UsagePage = 0x0D, Usage = 0x05, Flags = RIDEV_REMOVE, Target = IntPtr.Zero },
        ];
        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        DestroyHandle();
    }
}
