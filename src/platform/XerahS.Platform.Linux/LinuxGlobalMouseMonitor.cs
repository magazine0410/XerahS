#region License Information (GPL v3)

/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux;

internal sealed class LinuxGlobalMouseMonitor : IGlobalMouseMonitor
{
    private readonly MouseHighlighterInputBuffer _input;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new();
    private Exception? _failure;
    private int _disposed;
    public Exception? Failure => Volatile.Read(ref _failure);

    public static bool IsSupported
    {
        get
        {
            if (LinuxScreenCaptureService.IsWayland) return false;
            IntPtr display = IntPtr.Zero;
            try { display = NativeMethods.XOpenDisplay(null); return display != IntPtr.Zero && QueryExtension(display, out _); }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            finally { if (display != IntPtr.Zero) NativeMethods.XCloseDisplay(display); }
        }
    }

    public LinuxGlobalMouseMonitor(MouseHighlighterInputBuffer input)
    {
        _input = input;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "XerahS X11 mouse input" };
        _thread.Start();
        try { ready.Task.GetAwaiter().GetResult(); }
        catch { _thread.Join(); _stop.Dispose(); throw; }
    }

    private static bool QueryExtension(IntPtr display, out int opcode)
    {
        if (XQueryExtension(display, "XInputExtension", out opcode, out _, out _) == 0) return false;
        // XI 2.1 continues delivering raw events while another application grabs the pointer.
        int major = 2, minor = 1;
        return XIQueryVersion(display, ref major, ref minor) == 0 && (major > 2 || major == 2 && minor >= 1);
    }

    private unsafe void Run(TaskCompletionSource ready)
    {
        IntPtr display = IntPtr.Zero;
        try
        {
            if (LinuxScreenCaptureService.IsWayland) throw new PlatformNotSupportedException("Mouse highlighting requires an X11 session.");
            display = NativeMethods.XOpenDisplay(null);
            if (display == IntPtr.Zero || !QueryExtension(display, out int opcode))
                throw new PlatformNotSupportedException("Mouse highlighting requires XInput 2.1 or newer.");
            IntPtr root = NativeMethods.XDefaultRootWindow(display);
            byte[] mask = new byte[4];
            foreach (int kind in new[] { 15, 16, 17 }) mask[kind >> 3] |= (byte)(1 << (kind & 7));
            fixed (byte* pointer = mask)
            {
                XIEventMask selection = new() { DeviceId = 1, MaskLength = mask.Length, Mask = (IntPtr)pointer }; // XIAllMasterDevices
                if (XISelectEvents(display, root, ref selection, 1) != 0) throw new InvalidOperationException("Could not subscribe to global mouse events.");
            }
            NativeMethods.XFlush(display);
            if (QueryPosition(display, root, out var initial)) _input.SetPosition(initial);
            ready.SetResult();
            var eventBuffer = stackalloc nint[24]; // XEvent is 24 native longs.
            byte[] mapping = new byte[256];
            while (!_stop.IsSet)
            {
                // Poll only this connection; disposal never closes a display while Xlib is using it.
                for (int count = 0; count < 256 && XPending(display) > 0 && !_stop.IsSet; count++)
                {
                    XNextEvent(display, (IntPtr)eventBuffer);
                    var cookie = Marshal.PtrToStructure<XGenericEventCookie>((IntPtr)eventBuffer);
                    if (cookie.Type != 35 || cookie.Extension != opcode || !XGetEventData(display, ref cookie)) continue;
                    try
                    {
                        var raw = Marshal.PtrToStructure<XIRawEvent>(cookie.Data);
                        if (!QueryPosition(display, root, out var point)) continue;
                        _input.SetPosition(point);
                        if (raw.EventType is not (15 or 16)) continue;
                        int button = raw.Detail;
                        // Raw events carry physical button numbers. Read the source device mapping,
                        // including left-handed/remapped mice, before publishing logical buttons.
                        IntPtr device = XOpenDevice(display, new IntPtr(raw.SourceId));
                        if (device != IntPtr.Zero)
                        {
                            try
                            {
                                int length = XGetDeviceButtonMapping(display, device, mapping, mapping.Length);
                                if (button > 0 && button <= Math.Min(length, mapping.Length)) button = mapping[button - 1];
                            }
                            finally { XCloseDevice(display, device); }
                        }
                        MouseHighlightButton? logical = button switch { 1 => MouseHighlightButton.Primary, 2 => MouseHighlightButton.Middle, 3 => MouseHighlightButton.Secondary, _ => null };
                        if (logical.HasValue) _input.PublishButton(new(logical.Value, raw.EventType == 15, point, Stopwatch.GetTimestamp()));
                    }
                    finally { XFreeEventData(display, ref cookie); }
                }
                if (QueryPosition(display, root, out var current)) _input.SetPosition(current);
                _stop.Wait(8);
            }
        }
        catch (Exception ex) { Volatile.Write(ref _failure, ex); ready.TrySetException(ex); }
        finally { if (display != IntPtr.Zero) NativeMethods.XCloseDisplay(display); }
    }

    internal static bool TryGetCursorPosition(out Point point)
    {
        point = Point.Empty;
        if (LinuxScreenCaptureService.IsWayland) return false;
        IntPtr display = IntPtr.Zero;
        try
        {
            display = NativeMethods.XOpenDisplay(null);
            return display != IntPtr.Zero && QueryPosition(display, NativeMethods.XDefaultRootWindow(display), out point);
        }
        catch (DllNotFoundException) { return false; }
        finally { if (display != IntPtr.Zero) NativeMethods.XCloseDisplay(display); }
    }

    private static bool QueryPosition(IntPtr display, IntPtr root, out Point point)
    {
        bool success = XQueryPointer(display, root, out _, out _, out int x, out int y, out _, out _, out _);
        point = new Point(x, y);
        return success;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Set();
        if (Thread.CurrentThread != _thread) _thread.Join();
        _stop.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct XIEventMask { public int DeviceId, MaskLength; public IntPtr Mask; }
    [StructLayout(LayoutKind.Sequential)] private struct XGenericEventCookie
    {
        public int Type; public nuint Serial; public int SendEvent; public IntPtr Display;
        public int Extension, EventType; public uint Cookie; public IntPtr Data;
    }
    [StructLayout(LayoutKind.Sequential)] private struct XIRawEvent
    {
        public int Type; public nuint Serial; public int SendEvent; public IntPtr Display;
        public int Extension, EventType; public nuint Time; public int DeviceId, SourceId, Detail, Flags;
        // The remaining valuator data is not used; absolute cursor coordinates come from XQueryPointer.
    }
    [DllImport("libX11.so.6")] private static extern int XQueryExtension(IntPtr display, string name, out int opcode, out int firstEvent, out int firstError);
    [DllImport("libXi.so.6")] private static extern int XIQueryVersion(IntPtr display, ref int major, ref int minor);
    [DllImport("libXi.so.6")] private static extern int XISelectEvents(IntPtr display, IntPtr window, ref XIEventMask masks, int count);
    [DllImport("libX11.so.6")] private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display, IntPtr eventBuffer);
    [DllImport("libX11.so.6")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool XGetEventData(IntPtr display, ref XGenericEventCookie cookie);
    [DllImport("libX11.so.6")] private static extern void XFreeEventData(IntPtr display, ref XGenericEventCookie cookie);
    [DllImport("libX11.so.6")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
    [DllImport("libXi.so.6")] private static extern IntPtr XOpenDevice(IntPtr display, IntPtr deviceId);
    [DllImport("libXi.so.6")] private static extern int XCloseDevice(IntPtr display, IntPtr device);
    [DllImport("libXi.so.6")] private static extern int XGetDeviceButtonMapping(IntPtr display, IntPtr device, [Out] byte[] mapping, int count);
}
