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
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Platform.Linux;

/// <summary>
/// KDE Plasma Wayland: window queries and actions go through KWin, which sees native Wayland windows.
/// Handles from <see cref="KWinWindowManager"/> are KWin windows; any other handle is an X11 window.
/// </summary>
public partial class LinuxWindowService
{
    private readonly Dictionary<string, KWinBorderState> _kwinBorderlessWindows = new(StringComparer.Ordinal);

    private static KWinWindowManager? KWin => KWinWindowManager.Shared;

    /// <summary>
    /// Whether the handle came from KWin. Such handles never reach the X11 calls, even after the
    /// window has closed; the methods then return their "not found" values.
    /// </summary>
    private static bool IsKWinHandle(IntPtr handle) => KWinWindowManager.TryGetId(handle, out _);

    private static KWinWindow? GetKWinWindow(IntPtr handle) => KWin?.GetWindow(handle);

    private static WindowInfo ToWindowInfo(KWinWindow window, double scale) => new()
    {
        Handle = KWinWindowManager.GetHandle(window.Id),
        Title = window.Caption,
        ClassName = window.ResourceClass,
        Bounds = KWinWindowManager.ToX11(window.FrameGeometry, scale),
        ProcessId = window.ProcessId,
        IsVisible = true,
        IsMaximized = window.Maximized,
        IsMinimized = window.Minimized
    };

    private static bool TryGetKWinWindows(out WindowInfo[] windows)
    {
        windows = [];
        if (KWin?.GetSnapshot() is not { } snapshot)
            return false;

        double scale = KWinWindowManager.X11Scale;
        // Topmost first, the order the X11 enumeration uses.
        windows = snapshot.Windows.Where(window => window.Listed).Reverse().Select(window => ToWindowInfo(window, scale)).ToArray();
        return true;
    }

    private static bool TryGetKWinForegroundWindow(out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (KWin?.GetSnapshot() is not { } snapshot)
            return false;

        if (snapshot.ActiveWindowId is { Length: > 0 } id)
            handle = KWinWindowManager.GetHandle(id);
        return true;
    }

    private bool ToggleKWinBorderlessWindow(KWinWindowManager kwin, IntPtr handle, KWinWindow window, bool useWorkingArea)
    {
        lock (_kwinBorderlessWindows)
        {
            if (_kwinBorderlessWindows.Remove(window.Id, out var saved))
            {
                Common.DebugHelper.WriteLine($"LinuxWindowService: Restoring the border of '{window.Caption}' ({window.ResourceClass}).");
                return kwin.RestoreBorder(handle, saved.FrameGeometry, saved.NoBorder, saved.Maximized);
            }

            // Windows that draw their own title bar (most browsers and Electron apps) keep it; only
            // KWin's border is removed, as on X11 and Windows.
            Common.DebugHelper.WriteLine($"LinuxWindowService: Making '{window.Caption}' ({window.ResourceClass}) borderless.");
            if (!kwin.MakeBorderless(handle, useWorkingArea))
                return false;

            _kwinBorderlessWindows[window.Id] = new KWinBorderState(window.FrameGeometry, window.NoBorder, window.Maximized);
            return true;
        }
    }

    private sealed record KWinBorderState(Rectangle FrameGeometry, bool NoBorder, bool Maximized);
}
