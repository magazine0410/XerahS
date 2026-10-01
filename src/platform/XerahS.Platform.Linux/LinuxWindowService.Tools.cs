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
using SkiaSharp;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux;

public partial class LinuxWindowService
{
    private readonly Dictionary<IntPtr, BorderlessSnapshot> _borderlessWindows = [];
    // Click-through is set on XerahS's own windows, which are X11 windows also on Wayland (through
    // XWayland), so it does not need the inspection support that other applications' windows need.
    public bool SupportsClickThrough
    {
        get
        {
            if (_display == IntPtr.Zero) return false;
            try { return XShapeQueryVersion(_display, out int major, out int minor) != 0 && (major > 1 || major == 1 && minor >= 1); }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }
    [DllImport("libXext.so.6")] private static extern int XShapeQueryVersion(IntPtr display, out int major, out int minor);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int x, int y, IntPtr rectangles, int count, int operation, int ordering);

    // XerahS's windows are X11 windows, also on Wayland through XWayland, where KWin and other
    // compositors honour their positions. Only another application's native Wayland windows cannot be moved.
    public bool SupportsWindowPositioning => _display != IntPtr.Zero;
    public bool SupportsWindowInspection => _display != IntPtr.Zero && !LinuxScreenCaptureService.IsWayland;
    public bool SupportsWindowOpacity => SupportsWindowInspection;
    public bool SupportsBorderless => KWin != null || SupportsWindowInspection && HasAnyPropertyAtom(_rootWindow, "_NET_SUPPORTED", ["_NET_MOVERESIZE_WINDOW"]);

    public WindowDetails? GetWindowDetails(IntPtr handle)
    {
        if (!SupportsWindowInspection || !TryGetWindowAttributes(handle, out var attributes)) return null;
        uint pid = GetWindowProcessId(handle);
        string processName = string.Empty, processPath = string.Empty;
        // A PID from a remote X client does not identify a local process.
        bool local = !TryGetUtf8StringProperty(handle, "WM_CLIENT_MACHINE", out string machine) ||
            machine.Split('.')[0].Equals(Environment.MachineName.Split('.')[0], StringComparison.OrdinalIgnoreCase);
        if (pid > 0 && local)
        {
            try
            {
                using var process = Process.GetProcessById(checked((int)pid));
                processName = process.ProcessName;
                processPath = process.MainModule?.FileName ?? string.Empty;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException) { }
        }
        byte opacity = 255;
        if (TryGetWindowHandleArrayProperty(handle, "_NET_WM_WINDOW_OPACITY", out var values) && values.Length > 0)
            opacity = (byte)Math.Round(unchecked((uint)values[0].ToInt64()) / (double)uint.MaxValue * 255);
        return new WindowDetails
        {
            Handle = handle, Title = GetWindowText(handle), ClassName = GetWindowClassName(handle),
            ProcessId = pid, ProcessName = processName, ProcessFileName = processPath,
            Bounds = GetWindowBoundsCore(handle, false), ClientBounds = GetClientScreenBounds(handle, attributes),
            IsTopmost = HasAnyPropertyAtom(handle, "_NET_WM_STATE", ["_NET_WM_STATE_ABOVE"]), Opacity = opacity
        };
    }

    // As in ShareX, show the client area at its position on screen rather than relative to the window.
    private Rectangle GetClientScreenBounds(IntPtr handle, XWindowAttributes attributes) =>
        NativeMethods.XTranslateCoordinates(_display, handle, _rootWindow, 0, 0, out int x, out int y, out _) != 0
            ? new Rectangle(x, y, attributes.width, attributes.height)
            : new Rectangle(0, 0, attributes.width, attributes.height);

    public byte[]? GetWindowIcon(IntPtr handle)
    {
        if (!SupportsWindowInspection || !TryGetProperty(handle, "_NET_WM_ICON", out var property, 1024 * 1024)) return null;
        using (property)
        {
            if (property.Format != 32) return null;
            return DecodeWindowIcon(ReadIntPtrArray(property.Data, property.ItemCount));
        }
    }

    internal static byte[]? DecodeWindowIcon(IntPtr[] values)
    {
        int selected = -1, bestScore = int.MaxValue, width = 0, height = 0;
        for (int offset = 0; offset + 2 <= values.Length;)
        {
            long w = values[offset].ToInt64(), h = values[offset + 1].ToInt64();
            if (w <= 0 || h <= 0 || w > 1024 || h > 1024 || w * h > values.Length - offset - 2) break;
            int score = (int)(Math.Abs(w - 32) + Math.Abs(h - 32));
            if (score < bestScore) { selected = offset + 2; width = (int)w; height = (int)h; bestScore = score; }
            offset += 2 + (int)(w * h);
        }
        if (selected < 0) return null;
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var pixels = new SKColor[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new SKColor(unchecked((uint)values[selected + i].ToInt64()));
        bitmap.Pixels = pixels;
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }

    public IntPtr GetWindowAtPoint(Point point, bool topLevelOnly = true)
    {
        if (!SupportsWindowInspection) return IntPtr.Zero;
        // Enumeration is in stacking order. X11 controls are often drawn within the client,
        // so only top-level picking is advertised on this platform.
        return GetAllWindows().FirstOrDefault(w => w.Bounds.Contains(point))?.Handle ?? IntPtr.Zero;
    }

    public bool SetWindowTopmost(IntPtr handle, bool topmost) => IsKWinHandle(handle)
        ? KWin?.SetKeepAbove(handle, topmost) == true
        : SupportsTopmost &&
        SendWindowMessage(handle, "_NET_WM_STATE", topmost ? 1 : 0, GetAtom("_NET_WM_STATE_ABOVE"), IntPtr.Zero, new IntPtr(2));

    public bool SetWindowOpacity(IntPtr handle, byte opacity)
    {
        if (!SupportsWindowOpacity || !TryGetWindowAttributes(handle, out _)) return false;
        SetCardinals(handle, "_NET_WM_WINDOW_OPACITY", "CARDINAL", [new IntPtr((long)Math.Round(opacity / 255d * uint.MaxValue))]);
        NativeMethods.XFlush(_display);
        return true;
    }

    public bool ToggleBorderlessWindow(IntPtr handle, bool useWorkingArea = false)
    {
        if (IsKWinHandle(handle))
            return KWin is { } kwin && GetKWinWindow(handle) is { } kwinWindow && ToggleKWinBorderlessWindow(kwin, handle, kwinWindow, useWorkingArea);

        lock (_borderlessWindows)
        {
            if (!SupportsBorderless || !TryGetWindowAttributes(handle, out var attributes)) { _borderlessWindows.Remove(handle); return false; }
            uint pid = GetWindowProcessId(handle);
            string className = GetWindowClassName(handle);
            if (_borderlessWindows.TryGetValue(handle, out var saved) && saved.ProcessId == pid && saved.ClassName == className)
            {
                IntPtr[] restoredHints = saved.Hints is { Length: >= 5 } ? (IntPtr[])saved.Hints.Clone() : new IntPtr[5];
                if ((restoredHints[0].ToInt64() & 2) == 0)
                {
                    // KWin retains the last decoration state when the flag/property is removed.
                    // Explicitly restore the previous default instead of leaving the window borderless.
                    restoredHints[0] = new IntPtr(restoredHints[0].ToInt64() | 2);
                    restoredHints[2] = new IntPtr(saved.HadDecorations ? 1 : 0); // MWM_DECOR_ALL
                }
                SetCardinals(handle, "_MOTIF_WM_HINTS", "_MOTIF_WM_HINTS", restoredHints);
                MoveResizeClient(handle, saved.Bounds);
                SetMaximized(handle, saved.MaximizedHorizontal, saved.MaximizedVertical);
                _borderlessWindows.Remove(handle);
                NativeMethods.XFlush(_display);
                return true;
            }
            Rectangle bounds = GetWindowBoundsCore(handle, false);
            var screen = PlatformServices.Screen.GetScreenFromRectangle(bounds);
            Rectangle target = useWorkingArea ? GetMonitorWorkingArea(screen) : screen.Bounds;
            if (target.IsEmpty) return false;
            IntPtr[]? hints = TryGetWindowHandleArrayProperty(handle, "_MOTIF_WM_HINTS", out var existing) ? existing : null;
            bool hadDecorations = !TryGetFrameExtents(handle, out var extents) ||
                extents.Left + extents.Right + extents.Top + extents.Bottom > 0;
            saved = new BorderlessSnapshot(pid, className, hints, new Rectangle(bounds.Location, new Size(attributes.width, attributes.height)),
                HasAnyPropertyAtom(handle, "_NET_WM_STATE", ["_NET_WM_STATE_MAXIMIZED_HORZ"]),
                HasAnyPropertyAtom(handle, "_NET_WM_STATE", ["_NET_WM_STATE_MAXIMIZED_VERT"]), hadDecorations);
            IntPtr[] borderlessHints = hints is { Length: >= 5 } ? (IntPtr[])hints.Clone() : new IntPtr[5];
            borderlessHints[0] = new IntPtr(borderlessHints[0].ToInt64() | 2); // MWM_HINTS_DECORATIONS
            borderlessHints[2] = IntPtr.Zero;
            SetCardinals(handle, "_MOTIF_WM_HINTS", "_MOTIF_WM_HINTS", borderlessHints);
            SetMaximized(handle, false, false);
            MoveResizeClient(handle, target);
            _borderlessWindows[handle] = saved;
            NativeMethods.XFlush(_display);
            return true;
        }
    }

    private Rectangle GetMonitorWorkingArea(ScreenInfo screen)
    {
        // LinuxScreenService may only know xrandr bounds. EWMH supplies panel reservations.
        Rectangle area = screen.WorkingArea.IsEmpty ? screen.Bounds : screen.WorkingArea;
        if (!TryGetWindowAttributes(_rootWindow, out var root)) return area;
        bool hasStruts = false;
        foreach (IntPtr client in EnumerateCandidateWindows())
        {
            if (!TryGetWindowHandleArrayProperty(client, "_NET_WM_STRUT_PARTIAL", out var values) || values.Length < 12) continue;
            long[] strut = values.Select(v => v.ToInt64()).ToArray();
            if (strut.Any(v => v < 0 || v > int.MaxValue)) continue;
            area = ApplyStrut(area, root.width, root.height, strut);
            hasStruts = true;
        }
        if (hasStruts) return area;
        int desktop = 0;
        if (TryGetWindowHandleArrayProperty(_rootWindow, "_NET_CURRENT_DESKTOP", out var current) && current.Length > 0)
            desktop = (int)Math.Clamp(current[0].ToInt64(), 0, 1024);
        if (TryGetWindowHandleArrayProperty(_rootWindow, "_NET_WORKAREA", out var workAreas) && workAreas.Length >= (desktop + 1) * 4)
        {
            int i = desktop * 4;
            var work = new Rectangle(ToInt32(workAreas[i]), ToInt32(workAreas[i + 1]), ToInt32(workAreas[i + 2]), ToInt32(workAreas[i + 3]));
            Rectangle intersection = Rectangle.Intersect(area, work);
            if (intersection.Width > 0 && intersection.Height > 0) return intersection;
        }
        return area;
    }

    internal static Rectangle ApplyStrut(Rectangle area, int rootWidth, int rootHeight, long[] strut)
    {
        if (strut.Length < 12) return area;
        int left = area.Left, top = area.Top, right = area.Right, bottom = area.Bottom;
        if (strut[0] > 0 && strut[4] < bottom && strut[5] >= top) left = Math.Max(left, (int)strut[0]);
        if (strut[1] > 0 && strut[6] < bottom && strut[7] >= top) right = Math.Min(right, rootWidth - (int)strut[1]);
        if (strut[2] > 0 && strut[8] < right && strut[9] >= left) top = Math.Max(top, (int)strut[2]);
        if (strut[3] > 0 && strut[10] < right && strut[11] >= left) bottom = Math.Min(bottom, rootHeight - (int)strut[3]);
        return right > left && bottom > top ? Rectangle.FromLTRB(left, top, right, bottom) : area;
    }

    private void SetMaximized(IntPtr handle, bool horizontal, bool vertical)
    {
        SendWindowMessage(handle, "_NET_WM_STATE", horizontal ? 1 : 0, GetAtom("_NET_WM_STATE_MAXIMIZED_HORZ"), IntPtr.Zero, new IntPtr(2));
        SendWindowMessage(handle, "_NET_WM_STATE", vertical ? 1 : 0, GetAtom("_NET_WM_STATE_MAXIMIZED_VERT"), IntPtr.Zero, new IntPtr(2));
    }

    private void MoveResizeClient(IntPtr handle, Rectangle bounds) =>
        SendWindowMessage(handle, "_NET_MOVERESIZE_WINDOW", 1 | (15 << 8) | (2 << 12), new IntPtr(bounds.X), new IntPtr(bounds.Y), new IntPtr(bounds.Width), new IntPtr(bounds.Height));

    private bool SendWindowMessage(IntPtr handle, string messageType, long first, IntPtr second, IntPtr third, IntPtr fourth, IntPtr fifth = default)
    {
        if (handle == IntPtr.Zero || handle == _rootWindow) return false;
        var message = new XEvent { clientMessage = new XClientMessageEvent
        {
            type = 33, display = _display, window = handle, message_type = GetAtom(messageType), format = 32,
            data0 = new IntPtr(first), data1 = second, data2 = third, data3 = fourth, data4 = fifth
        }};
        int result = NativeMethods.XSendEvent(_display, _rootWindow, false, (1L << 20) | (1L << 19), ref message);
        NativeMethods.XFlush(_display);
        return result != 0;
    }

    private void SetCardinals(IntPtr handle, string property, string type, IntPtr[] values) =>
        XChangeProperty(_display, handle, GetAtom(property), GetAtom(type), 32, 0, values, values.Length);

    private sealed record BorderlessSnapshot(uint ProcessId, string ClassName, IntPtr[]? Hints, Rectangle Bounds, bool MaximizedHorizontal, bool MaximizedVertical, bool HadDecorations);
    [DllImport("libX11.so.6")] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int count);
}
