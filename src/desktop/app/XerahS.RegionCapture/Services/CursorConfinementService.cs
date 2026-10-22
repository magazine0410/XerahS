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

using Avalonia.Controls;
using Avalonia.Threading;
using XerahS.Common;
using XerahS.RegionCapture.Models;

namespace XerahS.RegionCapture.Services;

/// <summary>
/// Keeps the OS cursor on the active monitor during a region capture, as ShareX's
/// Helpers.LockCursorToWindow does with Cursor.Clip while the capture form is activated.
/// Windows clips the cursor to the monitor's rectangle. On Linux the overlay takes an X pointer
/// grab confined to its own window, on Avalonia's own X connection so the pointer events keep
/// flowing to Avalonia; XWayland turns such a grab into a Wayland pointer constraint on the
/// overlay's surface, so the same grab works on Wayland compositors. Only one confinement
/// exists at a time.
/// </summary>
internal static class CursorConfinementService
{
    private static CursorConfinement? _current;

    /// <summary>A confinement can be attempted on this platform.</summary>
    public static bool IsSupported =>
        ConfineOverride != null ||
        OperatingSystem.IsWindows() ||
        (OperatingSystem.IsLinux() && !MonitorEnumerationService.IsAvaloniaWaylandBackend());

    /// <summary>
    /// True when a successful confinement proves that the cursor cannot leave the monitor, so the other
    /// monitors need no overlay. Windows' ClipCursor does. An X pointer grab does not: the X server
    /// accepts it, but through XWayland the compositor applies the constraint on its own terms and drops
    /// it when another surface takes the pointer, so the other monitors keep their inactive overlays.
    /// </summary>
    public static bool ConfinesReliably => OperatingSystem.IsWindows();

    /// <summary>Replaces the platform confinement in tests.</summary>
    internal static Func<Window, PixelRect, CursorConfinement?>? ConfineOverride { get; set; }

    /// <summary>The confinement currently held, if any.</summary>
    internal static CursorConfinement? Current => _current is { IsReleased: false } current ? current : null;

    /// <summary>
    /// Confines the cursor to <paramref name="physicalBounds"/>, the monitor of <paramref name="overlay"/>.
    /// Any confinement held before is released first. Returns null when the platform cannot confine the
    /// cursor or the attempt failed; the caller may try again later. Must run on the UI thread.
    /// </summary>
    public static CursorConfinement? TryConfine(Window overlay, PixelRect physicalBounds)
    {
        Dispatcher.UIThread.VerifyAccess();
        _current?.Release();
        _current = null;

        CursorConfinement? confinement;
        try
        {
            confinement = ConfineOverride != null
                ? ConfineOverride(overlay, physicalBounds)
                : ConfineOnPlatform(overlay, physicalBounds);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"[CursorConfinement] Confining the cursor failed: {ex.Message}");
            confinement = null;
        }

        _current = confinement;
        return confinement;
    }

    /// <summary>Releases the confinement currently held, if any.</summary>
    public static void ReleaseCurrent()
    {
        _current?.Release();
        _current = null;
    }

    private static CursorConfinement? ConfineOnPlatform(Window overlay, PixelRect physicalBounds)
    {
#if WINDOWS
        var (x, y, width, height) = physicalBounds.ToIntegerBounds();
        return Platform.Windows.NativeCursorClip.Clip(x, y, x + width, y + height)
            ? new CursorConfinement(Platform.Windows.NativeCursorClip.Release)
            : null;
#else
        if (OperatingSystem.IsLinux() && !MonitorEnumerationService.IsAvaloniaWaylandBackend())
            return Platform.Linux.X11PointerGrab.TryConfine(overlay);

        return null;
#endif
    }
}

/// <summary>
/// A cursor confinement held by one overlay. <see cref="Release"/> ends it; <see cref="Reassert"/>
/// re-applies it after the pointer has returned to the overlay.
/// </summary>
internal sealed class CursorConfinement
{
    private readonly Action _release;
    private readonly Action? _reassert;

    public CursorConfinement(Action release, Action? reassert = null)
    {
        _release = release;
        _reassert = reassert;
    }

    public bool IsReleased { get; private set; }

    /// <summary>
    /// Re-applies the confinement. Through XWayland the compositor drops its constraint when another
    /// surface takes the pointer, and only a new grab brings it back.
    /// </summary>
    public void Reassert()
    {
        if (!IsReleased)
            _reassert?.Invoke();
    }

    public void Release()
    {
        if (IsReleased)
            return;

        IsReleased = true;
        try
        {
            _release();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"[CursorConfinement] Releasing the cursor failed: {ex.Message}");
        }
    }
}
