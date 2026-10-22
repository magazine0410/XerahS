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

using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using XerahS.Common;
using XerahS.RegionCapture.Services;

namespace XerahS.RegionCapture.Platform.Linux;

/// <summary>
/// Confines the pointer to an overlay window with an X pointer grab (XGrabPointer with the overlay as
/// confine_to), the X11 counterpart of ShareX's Cursor.Clip. The grab is taken on the connection
/// Avalonia's X11 backend uses, with owner_events set, so Avalonia keeps receiving the pointer events
/// as before; a grab on a second connection would divert them. XWayland maps the confine_to window to a
/// Wayland pointer constraint on the overlay's surface. Avalonia's X11 backend never grabs the pointer
/// itself, so nothing replaces the grab while the overlay is open. The X server releases a grab when
/// its client disconnects, so a crash cannot leave the pointer confined.
/// </summary>
internal static class X11PointerGrab
{
    private const string LibX11 = "libX11.so.6";

    private const int GrabModeAsync = 1;
    private const uint ButtonPressMask = 1 << 2;
    private const uint ButtonReleaseMask = 1 << 3;
    private const uint EnterWindowMask = 1 << 4;
    private const uint LeaveWindowMask = 1 << 5;
    private const uint PointerMotionMask = 1 << 6;
    private const uint ButtonMotionMask = 1 << 13;
    private const uint GrabEventMask = ButtonPressMask | ButtonReleaseMask | EnterWindowMask | LeaveWindowMask |
                                       PointerMotionMask | ButtonMotionMask;

    internal const int GrabSuccess = 0;
    internal const int AlreadyGrabbed = 1;
    internal const int GrabInvalidTime = 2;
    internal const int GrabNotViewable = 3;
    internal const int GrabFrozen = 4;

    /// <summary>The field of Avalonia's X11Window holding its X11Info, whose Display property is the connection.</summary>
    internal const string X11InfoFieldName = "_x11";
    internal const string DisplayPropertyName = "Display";

    private static Type? _resolvedType;
    private static FieldInfo? _x11Field;
    private static PropertyInfo? _displayProperty;

    [DllImport(LibX11)]
    private static extern int XGrabPointer(IntPtr display, IntPtr grabWindow, int ownerEvents, uint eventMask,
        int pointerMode, int keyboardMode, IntPtr confineTo, IntPtr cursor, IntPtr time);

    [DllImport(LibX11)]
    private static extern int XUngrabPointer(IntPtr display, IntPtr time);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

    /// <summary>
    /// Grabs the pointer confined to <paramref name="overlay"/>'s X window. Returns null when the overlay
    /// has no X window yet, Avalonia's connection cannot be found, or the X server refused the grab
    /// (another client holds the pointer, or the window is not viewable yet).
    /// </summary>
    public static CursorConfinement? TryConfine(TopLevel overlay)
    {
        if (overlay.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle || handle.Handle == IntPtr.Zero)
            return null;

        if (overlay.PlatformImpl is not { } platformImpl || !TryGetDisplay(platformImpl, out IntPtr display))
        {
            DebugHelper.WriteLine("[CursorConfinement] Avalonia's X11 connection was not found; the cursor stays free.");
            return null;
        }

        IntPtr window = handle.Handle;
        int status = Grab(display, window);
        if (status != GrabSuccess)
        {
            DebugHelper.WriteLine($"[CursorConfinement] The X server refused the pointer grab: {DescribeStatus(status)}");
            return null;
        }

        return new CursorConfinement(
            release: () => Ungrab(display),
            reassert: () =>
            {
                int again = Grab(display, window);
                if (again != GrabSuccess)
                    DebugHelper.WriteLine($"[CursorConfinement] Re-issuing the pointer grab failed: {DescribeStatus(again)}");
            });
    }

    private static int Grab(IntPtr display, IntPtr window)
    {
        int status = XGrabPointer(display, window, 1, GrabEventMask, GrabModeAsync, GrabModeAsync, window, IntPtr.Zero, IntPtr.Zero);
        XFlush(display);
        return status;
    }

    private static void Ungrab(IntPtr display)
    {
        XUngrabPointer(display, IntPtr.Zero);
        XFlush(display);
    }

    internal static string DescribeStatus(int status) => status switch
    {
        GrabSuccess => "success",
        AlreadyGrabbed => "another client holds the pointer",
        GrabInvalidTime => "invalid time",
        GrabNotViewable => "the overlay window is not viewable yet",
        GrabFrozen => "the pointer is frozen by another grab",
        _ => $"status {status}"
    };

    /// <summary>
    /// Reads the Display connection out of Avalonia's window implementation. Avalonia keeps it in the
    /// internal field <c>X11Window._x11</c> (an <c>X11Info</c> with a public <c>Display</c> property); the
    /// Avalonia version is pinned, and a test guards the names.
    /// </summary>
    internal static bool TryGetDisplay(object platformImpl, out IntPtr display)
    {
        display = IntPtr.Zero;
        Type type = platformImpl.GetType();
        if (_resolvedType != type)
        {
            TryResolve(type, out _x11Field, out _displayProperty);
            _resolvedType = type;
        }

        if (_x11Field == null || _displayProperty == null)
            return false;

        object? info = _x11Field.GetValue(platformImpl);
        if (info == null || _displayProperty.GetValue(info) is not IntPtr value || value == IntPtr.Zero)
            return false;

        display = value;
        return true;
    }

    /// <summary>Finds the field and property on <paramref name="windowImplType"/> or one of its base types.</summary>
    internal static bool TryResolve(Type windowImplType, out FieldInfo? x11Field, out PropertyInfo? displayProperty)
    {
        x11Field = null;
        displayProperty = null;
        for (Type? current = windowImplType; current != null; current = current.BaseType)
        {
            x11Field = current.GetField(X11InfoFieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (x11Field != null)
                break;
        }

        if (x11Field == null)
            return false;

        displayProperty = x11Field.FieldType.GetProperty(DisplayPropertyName, BindingFlags.Instance | BindingFlags.Public);
        if (displayProperty == null || displayProperty.PropertyType != typeof(IntPtr))
        {
            displayProperty = null;
            return false;
        }

        return true;
    }
}
