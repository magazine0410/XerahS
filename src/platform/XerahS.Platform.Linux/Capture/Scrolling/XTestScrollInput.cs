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
using System.Runtime.InteropServices;
using XerahS.Common;
using Point = System.Drawing.Point;

namespace XerahS.Platform.Linux.Capture.Scrolling;

/// <summary>X11: input through the XTEST extension. On Wayland, Xwayland's XTEST only reaches X11 windows.</summary>
internal sealed class XTestScrollInput : IScrollInput
{
    private const uint WheelDownButton = 5;
    private IntPtr _display;

    public static bool IsAvailable()
    {
        IntPtr display = IntPtr.Zero;
        try
        {
            display = NativeMethods.XOpenDisplay(null);
            return display != IntPtr.Zero && XTestQueryExtension(display, out _, out _, out _, out _) != 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        finally
        {
            if (display != IntPtr.Zero) NativeMethods.XCloseDisplay(display);
        }
    }

    public Task<bool> BeginAsync(CancellationToken cancellationToken)
    {
        _display = NativeMethods.XOpenDisplay(null);
        if (_display == IntPtr.Zero || XTestQueryExtension(_display, out _, out _, out _, out _) == 0)
        {
            DebugHelper.WriteLine("XTestScrollInput: XTEST is not available.");
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task MovePointerAsync(Point target)
    {
        if (_display != IntPtr.Zero)
        {
            XTestFakeMotionEvent(_display, -1, target.X, target.Y, 0);
            NativeMethods.XFlush(_display);
        }

        return Task.CompletedTask;
    }

    public Task ScrollWheelAsync(int notches)
    {
        if (_display != IntPtr.Zero)
        {
            for (int i = 0; i < notches; i++)
            {
                XTestFakeButtonEvent(_display, WheelDownButton, 1, 0);
                XTestFakeButtonEvent(_display, WheelDownButton, 0, 0);
            }

            NativeMethods.XFlush(_display);
        }

        return Task.CompletedTask;
    }

    public Task PressKeyAsync(int keysym)
    {
        if (_display != IntPtr.Zero)
        {
            byte keycode = XKeysymToKeycode(_display, (nuint)keysym);
            if (keycode != 0)
            {
                XTestFakeKeyEvent(_display, keycode, 1, 0);
                XTestFakeKeyEvent(_display, keycode, 0, 0);
                NativeMethods.XFlush(_display);
            }
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_display != IntPtr.Zero)
        {
            NativeMethods.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }

        return ValueTask.CompletedTask;
    }

    [DllImport("libXtst.so.6")] private static extern int XTestQueryExtension(IntPtr display, out int eventBase, out int errorBase, out int major, out int minor);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, int isPress, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, int isPress, nuint delay);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);
}
