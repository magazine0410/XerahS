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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using XerahS.Common;
using XerahS.RegionCapture.Services;

namespace XerahS.RegionCapture.UI;

/// <summary>
/// Role of an overlay in active monitor mode.
/// </summary>
internal enum OverlayMonitorState
{
    /// <summary>Normal region capture overlay.</summary>
    Active,

    /// <summary>The active monitor is not known yet; the first overlay with pointer input becomes active.</summary>
    Pending,

    /// <summary>Covers a monitor outside the active one and ignores pointer input.</summary>
    Inactive
}

public partial class OverlayWindow
{
    private OverlayMonitorState _monitorState = OverlayMonitorState.Active;
    private CursorConfinement? _cursorConfinement;
    private long _nextConfinementRetryTick;

    /// <summary>How long a failed cursor confinement waits before pointer movement retries it.</summary>
    internal static int ConfinementRetryIntervalMs { get; set; } = 1000;

    /// <summary>The cursor is confined to this overlay's monitor.</summary>
    internal bool IsCursorConfined => _cursorConfinement is { IsReleased: false };

    internal OverlayMonitorState MonitorState => _monitorState;

    internal Models.MonitorInfo Monitor => _monitor;

    /// <summary>Raised on the first pointer input while the overlay is <see cref="OverlayMonitorState.Pending"/>.</summary>
    internal event Action<OverlayWindow>? PointerActivity;

    /// <summary>Raised when an inactive overlay is clicked or receives a key, so focus can return to the active overlay.</summary>
    internal event Action<OverlayWindow>? ActiveOverlayRequested;

    /// <summary>Raised with the physical pointer position after pointer input on this overlay.</summary>
    internal event Action<OverlayWindow, Models.PixelPoint>? PointerLocationChanged;

    /// <summary>
    /// Another monitor's overlay saw the pointer at <paramref name="physicalPoint"/>. This overlay then
    /// shows its crosshair and magnifier only if the pointer is on its monitor.
    /// </summary>
    internal void UpdatePointerFromOtherOverlay(Models.PixelPoint physicalPoint)
    {
        if (_monitorState != OverlayMonitorState.Inactive)
        {
            _captureControl.UpdatePointerFromOtherOverlay(physicalPoint);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        KeepCursorConfined(pointerEntered: true);
        // Compositors send an enter when the overlay appears under a stationary pointer.
        // This must choose the monitor even before the first motion or button event.
        if (!HandleMonitorStatePointerEvent(e))
        {
            _captureControl.UpdateAimFromOverlayPointer(e.GetPosition(_captureControl), e.KeyModifiers);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        // A drag keeps the pointer capture; its moves still report the position.
        if (e.Pointer.Captured == null)
        {
            _captureControl.MarkPointerLeft();
        }
    }

    internal void SetMonitorState(OverlayMonitorState state)
    {
        if (_monitorState == state)
            return;

        _monitorState = state;
        _captureControl.IsInactiveMonitor = state == OverlayMonitorState.Inactive;

        if (state == OverlayMonitorState.Inactive)
        {
            HideAnnotationToolbar();
            ApplyInactiveCursorPolicy();
        }

        if (UpdateAnnotationCanvasHitTesting())
        {
            _captureControl.InvalidateVisual();
        }

        UpdateCursorConfinement();
    }

    /// <summary>
    /// Gives keyboard focus to this overlay.
    /// </summary>
    internal void FocusOverlay()
    {
        if (_windowClosed)
            return;

        Activate();
        Focus();
        _captureControl.Focus();
    }

    private void OnMonitorStatePointerEvent(object? sender, PointerEventArgs e) => HandleMonitorStatePointerEvent(e);

    /// <summary>
    /// Called first by every window-level pointer handler. Reports the first pointer input of a
    /// pending overlay and consumes all pointer input on an inactive one.
    /// Returns true when the event was consumed.
    /// </summary>
    private bool HandleMonitorStatePointerEvent(PointerEventArgs e)
    {
        if (_monitorState == OverlayMonitorState.Active)
        {
            KeepCursorConfined(pointerEntered: false);
        }
        else if (_monitorState == OverlayMonitorState.Pending)
        {
            PointerActivity?.Invoke(this);
        }

        if (_monitorState != OverlayMonitorState.Inactive)
            return false;

        e.Handled = true;
        // The pointer is on this monitor, so the active overlay hides its crosshair and magnifier; its own pointer
        // exit is not reported on every compositor (XWayland on Sway).
        PointerLocationChanged?.Invoke(this, _captureControl.ToPhysicalPoint(e.GetPosition(_captureControl)));
        if (e is PointerPressedEventArgs)
        {
            ActiveOverlayRequested?.Invoke(this);
        }
        return true;
    }

    /// <summary>
    /// Handles keys on an inactive overlay. Escape still cancels the capture; other keys return focus
    /// to the active overlay so Enter cannot capture a monitor outside the active one.
    /// </summary>
    private bool HandleInactiveMonitorKey(KeyEventArgs e)
    {
        if (_monitorState != OverlayMonitorState.Inactive)
            return false;

        if (e.Key == Key.Escape)
        {
            OnCancelled();
        }
        else
        {
            ActiveOverlayRequested?.Invoke(this);
        }

        e.Handled = true;
        return true;
    }

    private void ApplyInactiveCursorPolicy()
    {
        var arrow = new Cursor(StandardCursorType.Arrow);
        Cursor = arrow;
        _captureControl.Cursor = arrow;

        if (this.FindControl<Grid>("OverlayRoot") is { } root)
            root.Cursor = arrow;

        if (this.FindControl<Panel>("RootPanel") is { } panel)
            panel.Cursor = arrow;

        if (_annotationCanvas != null)
            _annotationCanvas.Cursor = arrow;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Avalonia raises Activated before it sets IsActive and Deactivated after it clears it, so the
        // confinement follows the property: ShareX clips on Activated and releases on Deactivate.
        if (change.Property == IsActiveProperty)
            UpdateCursorConfinement();
    }

    /// <summary>
    /// Confines the cursor to the active monitor while its overlay has focus, like ShareX does
    /// (Helpers.LockCursorToWindow clips on Activated and releases on Deactivate). Windows clips the
    /// cursor; on X11 and XWayland the overlay holds a pointer grab confined to its window.
    /// </summary>
    private void UpdateCursorConfinement()
    {
        if (ShouldConfineCursor())
        {
            if (IsCursorConfined || !CursorConfinementService.IsSupported)
                return;

            _cursorConfinement = CursorConfinementService.TryConfine(this, _monitor.PhysicalBounds);
            if (_cursorConfinement != null)
                DebugHelper.WriteLine($"[OverlayWindow] {_monitor.DeviceName}: cursor confined to the active monitor");
        }
        else if (_cursorConfinement != null)
        {
            _cursorConfinement.Release();
            _cursorConfinement = null;
        }
    }

    private bool ShouldConfineCursor() =>
        _options.ActiveMonitorMode &&
        _monitorState == OverlayMonitorState.Active &&
        IsActive &&
        !_windowClosed;

    /// <summary>
    /// Called on pointer input. A confinement that failed (the overlay was not viewable yet, or another
    /// client held the pointer) is retried, at most once per <see cref="ConfinementRetryIntervalMs"/> on
    /// movement and always on entry. A held confinement is re-asserted when the pointer re-enters the
    /// overlay: through XWayland the compositor drops its constraint whenever another surface, such as a
    /// flyout of this overlay, takes the pointer, and only a new grab brings it back.
    /// </summary>
    private void KeepCursorConfined(bool pointerEntered)
    {
        if (!ShouldConfineCursor() || !CursorConfinementService.IsSupported)
            return;

        if (IsCursorConfined)
        {
            if (pointerEntered)
                _cursorConfinement!.Reassert();
            return;
        }

        long now = Environment.TickCount64;
        if (!pointerEntered && now < _nextConfinementRetryTick)
            return;

        _nextConfinementRetryTick = now + ConfinementRetryIntervalMs;
        UpdateCursorConfinement();
    }
}
