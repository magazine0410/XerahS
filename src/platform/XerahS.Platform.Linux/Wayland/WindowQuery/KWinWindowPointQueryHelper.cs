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

namespace XerahS.Platform.Linux.Wayland.WindowQuery;

/// <summary>KDE Plasma: finds the window under a point from KWin's window stack.</summary>
internal sealed class KWinWindowPointQueryHelper : IWaylandWindowPointQueryHelper
{
    private readonly Lazy<WindowPointQueryCapability> _capability = new(() => KWinWindowManager.Shared != null
        ? new WindowPointQueryCapability(WindowPointQuerySupportLevel.Full, null)
        : new WindowPointQueryCapability(WindowPointQuerySupportLevel.Unsupported, "Wayland session: KWin scripting is unavailable, so KDE windows cannot be snapped."));

    public WindowPointQueryCapability Capability => _capability.Value;

    public WindowInfo? GetWindowAtPoint(Point logicalPoint)
    {
        if (KWinWindowManager.Shared?.GetWindowAt(logicalPoint) is not { } window)
            return null;

        // The point and the result are both in KWin's logical coordinates.
        return new WindowInfo
        {
            Handle = KWinWindowManager.GetHandle(window.Id),
            Title = window.Caption,
            ClassName = window.ResourceClass,
            Bounds = window.FrameGeometry,
            ProcessId = window.ProcessId,
            IsVisible = true,
            IsMaximized = window.Maximized,
            IsMinimized = window.Minimized
        };
    }
}
