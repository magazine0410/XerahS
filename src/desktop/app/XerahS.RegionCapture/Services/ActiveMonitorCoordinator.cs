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

using XerahS.RegionCapture.Models;
using XerahS.RegionCapture.UI;

namespace XerahS.RegionCapture.Services;

/// <summary>
/// Chooses the active overlay in active monitor mode. When the cursor position is unknown
/// (Wayland), every monitor gets an overlay and the first one that receives pointer input
/// becomes active. The others stay open as inactive overlays so clicks on those monitors
/// do not reach other applications.
/// </summary>
internal sealed class ActiveMonitorCoordinator : IDisposable
{
    private readonly IReadOnlyList<OverlayWindow> _overlays;

    public ActiveMonitorCoordinator(IReadOnlyList<OverlayWindow> overlays, OverlayWindow? initialActive)
    {
        _overlays = overlays;

        foreach (var overlay in _overlays)
        {
            overlay.PointerActivity += OnPointerActivity;
            overlay.ActiveOverlayRequested += OnActiveOverlayRequested;
        }

        if (initialActive != null)
        {
            SetActive(initialActive);
        }
        else
        {
            foreach (var overlay in _overlays)
                overlay.SetMonitorState(OverlayMonitorState.Pending);
        }
    }

    public OverlayWindow? ActiveOverlay { get; private set; }

    /// <summary>
    /// Returns the monitor containing the cursor, or null when the cursor position is unknown
    /// or outside every monitor. A single monitor is always the active one.
    /// </summary>
    internal static MonitorInfo? ResolveInitialActiveMonitor(IReadOnlyList<MonitorInfo> monitors, PixelPoint? cursorPosition)
    {
        if (monitors.Count == 1)
            return monitors[0];

        if (cursorPosition is not { } cursor)
            return null;

        return monitors.FirstOrDefault(monitor => monitor.PhysicalBounds.Contains(cursor));
    }

    private void SetActive(OverlayWindow active)
    {
        ActiveOverlay = active;
        foreach (var overlay in _overlays)
        {
            overlay.SetMonitorState(ReferenceEquals(overlay, active)
                ? OverlayMonitorState.Active
                : OverlayMonitorState.Inactive);
        }
    }

    private void OnPointerActivity(OverlayWindow overlay)
    {
        if (ActiveOverlay != null)
            return;

        SetActive(overlay);
        overlay.FocusOverlay();
    }

    private void OnActiveOverlayRequested(OverlayWindow overlay)
    {
        ActiveOverlay?.FocusOverlay();
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays)
        {
            overlay.PointerActivity -= OnPointerActivity;
            overlay.ActiveOverlayRequested -= OnActiveOverlayRequested;
        }
    }
}
