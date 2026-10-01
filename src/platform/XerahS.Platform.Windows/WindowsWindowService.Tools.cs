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

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using Point = System.Drawing.Point;

namespace XerahS.Platform.Windows;

public partial class WindowsWindowService
{
    private readonly Dictionary<IntPtr, BorderlessSnapshot> _borderlessWindows = [];
    public bool SupportsWindowInspection => true;
    public bool SupportsChildWindowPicking => true;
    public bool SupportsWindowOpacity => true;
    public bool SupportsBorderless => true;
    public bool SupportsClickThrough => true;

    public WindowDetails? GetWindowDetails(IntPtr handle)
    {
        if (!IsWindow(handle)) return null;
        WindowInfo window = new(handle);
        return new WindowDetails
        {
            Handle = handle, Title = window.Text, ClassName = window.ClassName,
            ProcessId = GetWindowProcessId(handle), ProcessName = ReadSafely(() => window.ProcessName),
            ProcessFileName = ReadSafely(() => window.ProcessFilePath), Bounds = window.Rectangle,
            ClientBounds = GetClientScreenBounds(handle, window.ClientRectangle), Styles = window.Style.ToString().Replace(", ", Environment.NewLine),
            ExtendedStyles = window.ExStyle.ToString().Replace(", ", Environment.NewLine),
            IsTopmost = window.TopMost, Opacity = window.Opacity
        };
    }

    // As in ShareX, show the client area at its position on screen rather than relative to the window.
    private static Rectangle GetClientScreenBounds(IntPtr handle, Rectangle client)
    {
        POINT origin = new() { X = client.X, Y = client.Y };
        return NativeMethods.ClientToScreen(handle, ref origin) ? new Rectangle(origin.X, origin.Y, client.Width, client.Height) : client;
    }

    private static string ReadSafely(Func<string> value)
    {
        try { return value(); }
        catch { return string.Empty; }
    }

    public byte[]? GetWindowIcon(IntPtr handle)
    {
        try
        {
            using var icon = new WindowInfo(handle).Icon;
            if (icon == null) return null;
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Unable to read window icon"); return null; }
    }

    public IntPtr GetWindowAtPoint(Point point, bool topLevelOnly = true)
    {
        IntPtr handle = NativeMethods.WindowFromPoint(new POINT { X = point.X, Y = point.Y });
        return topLevelOnly && handle != IntPtr.Zero ? GetAncestor(handle, 2) : handle;
    }

    public bool SetWindowTopmost(IntPtr handle, bool topmost) => IsWindow(handle) &&
        NativeMethods.SetWindowPos(handle, (IntPtr)(topmost ? NativeConstants.HWND_TOPMOST : NativeConstants.HWND_NOTOPMOST),
            0, 0, 0, 0, SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE);

    public bool SetWindowOpacity(IntPtr handle, byte opacity)
    {
        if (!IsWindow(handle)) return false;
        WindowInfo window = new(handle);
        window.Layered = opacity < 255;
        return opacity < 255
            ? window.Layered && NativeMethods.SetLayeredWindowAttributes(handle, 0, opacity, 0x00000002)
            : !window.Layered;
    }

    public bool ToggleBorderlessWindow(IntPtr handle, bool useWorkingArea = false)
    {
        lock (_borderlessWindows)
        {
            if (!IsWindow(handle)) { _borderlessWindows.Remove(handle); return false; }
            WindowInfo window = new(handle);
            if (window.IsMinimized) window.Restore();
            uint processId = GetWindowProcessId(handle);
            const SetWindowPosFlags flags = SetWindowPosFlags.SWP_FRAMECHANGED | SetWindowPosFlags.SWP_NOOWNERZORDER | SetWindowPosFlags.SWP_NOZORDER;
            if (_borderlessWindows.TryGetValue(handle, out var saved) && saved.ProcessId == processId && saved.ClassName == window.ClassName)
            {
                window.Style = saved.Style;
                window.ExStyle = saved.ExStyle;
                if (window.Style != saved.Style || window.ExStyle != saved.ExStyle) return false;
                Rectangle rect = saved.Bounds;
                bool restored = NativeMethods.SetWindowPos(handle, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height, flags);
                if (restored) _borderlessWindows.Remove(handle);
                return restored;
            }
            saved = new BorderlessSnapshot(processId, window.ClassName, window.Style, window.ExStyle, window.Rectangle);
            var screen = new WindowsScreenService().GetScreenFromRectangle(window.Rectangle);
            Rectangle bounds = useWorkingArea ? screen.WorkingArea : screen.Bounds;
            if (bounds.IsEmpty) return false;
            const WindowStyles removedStyles = WindowStyles.WS_CAPTION | WindowStyles.WS_MAXIMIZEBOX | WindowStyles.WS_SYSMENU | WindowStyles.WS_THICKFRAME;
            const WindowStyles removedExtendedStyles = WindowStyles.WS_EX_CLIENTEDGE | WindowStyles.WS_EX_DLGMODALFRAME | WindowStyles.WS_EX_STATICEDGE;
            window.Style &= ~removedStyles;
            window.ExStyle &= ~removedExtendedStyles;
            if ((window.Style & removedStyles) != 0 || (window.ExStyle & removedExtendedStyles) != 0 ||
                !NativeMethods.SetWindowPos(handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags))
            {
                window.Style = saved.Style;
                window.ExStyle = saved.ExStyle;
                return false;
            }
            _borderlessWindows[handle] = saved;
            return true;
        }
    }

    private sealed record BorderlessSnapshot(uint ProcessId, string ClassName, WindowStyles Style, WindowStyles ExStyle, Rectangle Bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
}
