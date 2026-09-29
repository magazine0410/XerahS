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

namespace XerahS.RegionCapture.Services;

/// <summary>
/// State machine for managing region capture interaction states.
/// Handles transitions: Hovering &lt;-&gt; Dragging -&gt; Selected -&gt; Confirmed/Cancelled
/// </summary>
public sealed class SelectionStateMachine
{
    private enum InteractionKind
    {
        None,
        Creating,
        Moving,
        Resizing
    }

    private readonly bool _quickCrop;
    private readonly IReadOnlyList<CaptureSnapSize> _snapSizes;
    private readonly double _snapDistance;
    private readonly PixelRect? _selectionBounds;

    private CaptureState _currentState = CaptureState.Hovering;
    private PixelPoint _startPoint;
    private PixelPoint _currentPoint;
    private PixelRect _selectionRect;
    private PixelRect _interactionOriginRect;
    private WindowInfo? _hoveredWindow;
    private SelectionModifier _modifiers = SelectionModifier.None;
    private double _aspectRatio = 1.0;
    private InteractionKind _interaction = InteractionKind.None;
    private SelectionHandle _resizeHandle = SelectionHandle.None;
    private bool _isMovingSelectionDuringCreation;
    private bool _controlHeldAtCreationStart;
    private PixelPoint _lastCreationPoint;

    public SelectionStateMachine()
        : this(quickCrop: true, snapSizes: CaptureSnapSize.DefaultPresets, snapDistance: 30)
    {
    }

    public SelectionStateMachine(
        bool quickCrop,
        IReadOnlyList<CaptureSnapSize>? snapSizes = null,
        double snapDistance = 30,
        PixelRect? selectionBounds = null)
    {
        _quickCrop = quickCrop;
        _snapSizes = snapSizes ?? CaptureSnapSize.DefaultPresets;
        _snapDistance = snapDistance;
        _selectionBounds = selectionBounds;
    }

    /// <summary>
    /// Gets the current capture state.
    /// </summary>
    public CaptureState CurrentState => _currentState;

    /// <summary>
    /// Gets the current selection rectangle (in physical pixels).
    /// </summary>
    public PixelRect SelectionRect => _selectionRect;

    /// <summary>
    /// Gets the currently hovered window (if any).
    /// </summary>
    public WindowInfo? HoveredWindow => _hoveredWindow;

    /// <summary>
    /// Gets the current cursor position (in physical pixels).
    /// </summary>
    public PixelPoint CurrentPoint => _currentPoint;

    /// <summary>
    /// Gets the active modifiers.
    /// </summary>
    public SelectionModifier Modifiers => _modifiers;

    /// <summary>
    /// Event fired when selection is confirmed.
    /// </summary>
    public event Action<RegionSelectionResult>? SelectionConfirmed;

    /// <summary>
    /// Event fired when selection is cancelled.
    /// </summary>
    public event Action? SelectionCancelled;

    /// <summary>
    /// Event fired when the selection rectangle changes.
    /// </summary>
    public event Action<PixelRect>? SelectionChanged;

    /// <summary>
    /// Event fired when state changes.
    /// </summary>
    public event Action<CaptureState>? StateChanged;

    /// <summary>
    /// Updates cursor position during hover or drag.
    /// </summary>
    public void UpdateCursorPosition(PixelPoint physicalPoint)
    {
        _currentPoint = physicalPoint;

        if (_currentState == CaptureState.Dragging)
        {
            UpdateSelectionRect();
        }
    }

    /// <summary>
    /// Updates the hovered window.
    /// </summary>
    public void UpdateHoveredWindow(WindowInfo? window)
    {
        if (_currentState == CaptureState.Hovering)
        {
            _hoveredWindow = window;
        }
    }

    /// <summary>
    /// Sets the active keyboard modifiers.
    /// </summary>
    public void SetModifiers(SelectionModifier modifiers)
    {
        _modifiers = modifiers;

        if (_currentState == CaptureState.Dragging)
        {
            UpdateSelectionRect();
        }
    }

    /// <summary>
    /// Starts a drag operation from the current point.
    /// </summary>
    public void BeginDrag(PixelPoint startPoint)
    {
        if (_currentState is not CaptureState.Hovering and not CaptureState.Selected)
            return;

        _startPoint = startPoint;
        _currentPoint = startPoint;
        _lastCreationPoint = startPoint;
        _interaction = InteractionKind.Creating;
        _resizeHandle = SelectionHandle.None;
        _isMovingSelectionDuringCreation = false;
        _controlHeldAtCreationStart = _modifiers.HasFlag(SelectionModifier.PixelNudge);
        SetSelectionRect(new PixelRect(startPoint.X, startPoint.Y, 0, 0));
        _aspectRatio = 1.0;

        TransitionTo(CaptureState.Dragging);
    }

    public void BeginMove(PixelPoint point)
    {
        if (_currentState != CaptureState.Selected || _selectionRect.IsEmpty)
            return;

        _interaction = InteractionKind.Moving;
        _resizeHandle = SelectionHandle.Body;
        _startPoint = point;
        _currentPoint = point;
        _interactionOriginRect = _selectionRect.Normalize();
        TransitionTo(CaptureState.Dragging);
    }

    public void BeginResize(SelectionHandle handle, PixelPoint point)
    {
        if (_currentState != CaptureState.Selected ||
            _selectionRect.IsEmpty ||
            handle is SelectionHandle.None or SelectionHandle.Body)
            return;

        _interaction = InteractionKind.Resizing;
        _resizeHandle = handle;
        _startPoint = point;
        _currentPoint = point;
        _interactionOriginRect = _selectionRect.Normalize();
        TransitionTo(CaptureState.Dragging);
    }

    /// <summary>
    /// Immediately confirms a single-point selection.
    /// </summary>
    public void ConfirmPoint(PixelPoint point)
    {
        _currentPoint = point;
        SetSelectionRect(new PixelRect(point.X, point.Y, 1, 1));
        TransitionTo(CaptureState.Confirmed);
        SelectionConfirmed?.Invoke(new RegionSelectionResult(_selectionRect, _currentPoint));
    }

    /// <summary>
    /// Ends the current drag operation.
    /// </summary>
    public void EndDrag(bool deferConfirmation = false)
    {
        if (_currentState != CaptureState.Dragging)
            return;

        if (_interaction is InteractionKind.Moving or InteractionKind.Resizing)
        {
            SetSelectionRect(_selectionRect.Normalize());
            _interaction = InteractionKind.None;
            _resizeHandle = SelectionHandle.None;
            ResetCreationModifiers();
            FinishSelection(confirmImmediately: _quickCrop && !deferConfirmation);
            return;
        }

        SetSelectionRect(_selectionRect.Normalize());
        _interaction = InteractionKind.None;
        ResetCreationModifiers();

        // Check if selection is large enough to be considered a drag
        if (_selectionRect.Width > 3 && _selectionRect.Height > 3)
        {
            FinishSelection(confirmImmediately: _quickCrop && !deferConfirmation);
        }
        else
        {
            // Selection too small - interpret as a click
            // If we were hovering a window, snap to it
            if (_hoveredWindow != null)
            {
                SetSelectionRect(_hoveredWindow.SnapBounds);
                FinishSelection(confirmImmediately: _quickCrop && !deferConfirmation);
            }
            else
            {
                // No window hovered, just cancel back to hovering state
                TransitionTo(CaptureState.Hovering);
                SetSelectionRect(PixelRect.Empty);
            }
        }
    }

    /// <summary>
    /// Confirms the current selected or hovered region.
    /// </summary>
    public bool TryConfirm()
    {
        if (_currentState == CaptureState.Selected && !_selectionRect.IsEmpty)
        {
            ConfirmSelection();
            return true;
        }

        if (_currentState == CaptureState.Hovering && _hoveredWindow is not null)
        {
            SetSelectionRect(_hoveredWindow.SnapBounds);
            if (_selectionRect.IsEmpty)
                return false;
            ConfirmSelection();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Snaps to the currently hovered window.
    /// </summary>
    public void SnapToWindow()
    {
        if (_currentState != CaptureState.Hovering || _hoveredWindow is null)
            return;

        SetSelectionRect(_hoveredWindow.SnapBounds);
        ConfirmSelection();
    }

    /// <summary>
    /// Cancels the current operation.
    /// </summary>
    public void Cancel()
    {
        TransitionTo(CaptureState.Cancelled);
        SelectionCancelled?.Invoke();
    }

    /// <summary>
    /// True while a new region is being drawn (not while an existing one is moved or resized).
    /// </summary>
    public bool IsCreatingSelection =>
        _currentState == CaptureState.Dragging && _interaction == InteractionKind.Creating;

    public bool TryClearSelection(PixelPoint point)
    {
        if (!IsCreatingSelection && !_selectionRect.Contains(point))
            return false;

        _interaction = InteractionKind.None;
        _resizeHandle = SelectionHandle.None;
        _hoveredWindow = null;
        ResetCreationModifiers();
        SetSelectionRect(PixelRect.Empty);
        TransitionTo(CaptureState.Hovering);
        return true;
    }

    /// <summary>
    /// Nudges the selection by the specified delta (for arrow key handling).
    /// </summary>
    public void NudgeSelection(int dx, int dy)
    {
        if (_currentState == CaptureState.Selected)
        {
            SetSelectionRect(KeepInsideBounds(_selectionRect.Offset(dx, dy)));
        }
        else if (_currentState == CaptureState.Dragging)
        {
            _currentPoint = _currentPoint.Offset(dx, dy);
            UpdateSelectionRect();
        }
    }

    /// <summary>
    /// Resizes the selection by the specified delta (for Ctrl+Arrow handling).
    /// </summary>
    public void ResizeSelection(int dWidth, int dHeight)
    {
        if (_currentState != CaptureState.Selected)
            return;

        var newWidth = Math.Max(1, _selectionRect.Width + dWidth);
        var newHeight = Math.Max(1, _selectionRect.Height + dHeight);

        SetSelectionRect(new PixelRect(
            _selectionRect.X,
            _selectionRect.Y,
            newWidth,
            newHeight));
    }

    private void UpdateSelectionRect()
    {
        if (_interaction == InteractionKind.Moving)
        {
            var dx = _currentPoint.X - _startPoint.X;
            var dy = _currentPoint.Y - _startPoint.Y;
            SetSelectionRect(KeepInsideBounds(_interactionOriginRect.Offset(dx, dy)));
            return;
        }

        if (_interaction == InteractionKind.Resizing)
        {
            SetSelectionRect(ResizeFromHandle(_interactionOriginRect, _resizeHandle, _currentPoint));
            return;
        }

        if (TryMoveSelectionDuringCreation())
        {
            return;
        }

        PixelRect newRect;
        var endPoint = _currentPoint;

        // Apply aspect ratio lock if Shift is held
        if (_modifiers.HasFlag(SelectionModifier.LockAspectRatio) && _aspectRatio > 0)
        {
            endPoint = ApplyAspectRatioLock(_startPoint, _currentPoint, _aspectRatio);
        }
        else if (_snapSizes.Count > 0)
        {
            endPoint = SelectionSnapHelper.SnapEndPoint(_startPoint, _currentPoint, _snapSizes, _snapDistance);
        }

        // Apply center expansion if Alt is held
        if (_modifiers.HasFlag(SelectionModifier.FromCenter))
        {
            var dx = endPoint.X - _startPoint.X;
            var dy = endPoint.Y - _startPoint.Y;

            newRect = new PixelRect(
                _startPoint.X - dx,
                _startPoint.Y - dy,
                dx * 2,
                dy * 2).Normalize();
        }
        else
        {
            newRect = PixelRect.FromCorners(_startPoint, endPoint);
        }

        SetSelectionRect(newRect);
        _lastCreationPoint = _currentPoint;
        _controlHeldAtCreationStart = _modifiers.HasFlag(SelectionModifier.PixelNudge);
    }

    private bool TryMoveSelectionDuringCreation()
    {
        bool controlHeld = _modifiers.HasFlag(SelectionModifier.PixelNudge);
        if (!controlHeld)
        {
            _isMovingSelectionDuringCreation = false;
            return false;
        }

        var current = _selectionRect.Normalize();
        if (current.Width <= 0 || current.Height <= 0)
        {
            return false;
        }

        // If Ctrl was already down when the drag started, keep resizing until it is
        // released and pressed again (ShareX region-creation move gesture).
        if (!_isMovingSelectionDuringCreation && _controlHeldAtCreationStart)
        {
            return false;
        }

        var moved = KeepInsideBounds(current.Offset(
            _currentPoint.X - _lastCreationPoint.X,
            _currentPoint.Y - _lastCreationPoint.Y));
        SetSelectionRect(moved);
        _startPoint = _startPoint.Offset(moved.X - current.X, moved.Y - current.Y);
        _isMovingSelectionDuringCreation = true;
        _controlHeldAtCreationStart = true;
        _lastCreationPoint = _currentPoint;
        return true;
    }

    private void ResetCreationModifiers()
    {
        _isMovingSelectionDuringCreation = false;
        _controlHeldAtCreationStart = false;
    }

    internal static PixelRect ResizeFromHandle(PixelRect original, SelectionHandle handle, PixelPoint current)
    {
        double left = original.Left;
        double top = original.Top;
        double right = original.Right;
        double bottom = original.Bottom;

        switch (handle)
        {
            case SelectionHandle.TopLeft:
                left = current.X;
                top = current.Y;
                break;
            case SelectionHandle.Top:
                top = current.Y;
                break;
            case SelectionHandle.TopRight:
                right = current.X;
                top = current.Y;
                break;
            case SelectionHandle.Right:
                right = current.X;
                break;
            case SelectionHandle.BottomRight:
                right = current.X;
                bottom = current.Y;
                break;
            case SelectionHandle.Bottom:
                bottom = current.Y;
                break;
            case SelectionHandle.BottomLeft:
                left = current.X;
                bottom = current.Y;
                break;
            case SelectionHandle.Left:
                left = current.X;
                break;
        }

        return PixelRect.FromCorners(new PixelPoint(left, top), new PixelPoint(right, bottom));
    }

    private void FinishSelection(bool confirmImmediately)
    {
        if (_selectionRect.IsEmpty)
        {
            TransitionTo(CaptureState.Hovering);
            return;
        }

        TransitionTo(CaptureState.Selected);
        if (confirmImmediately)
        {
            ConfirmSelection();
        }
    }

    private static PixelPoint ApplyAspectRatioLock(PixelPoint start, PixelPoint end, double ratio)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        // Determine which dimension to constrain
        var absDx = Math.Abs(dx);
        var absDy = Math.Abs(dy);

        if (absDx / ratio > absDy)
        {
            // Constrain height based on width
            dy = Math.Sign(dy) * absDx / ratio;
        }
        else
        {
            // Constrain width based on height
            dx = Math.Sign(dx) * absDy * ratio;
        }

        return new PixelPoint(start.X + dx, start.Y + dy);
    }

    private void ConfirmSelection()
    {
        TransitionTo(CaptureState.Confirmed);
        SelectionConfirmed?.Invoke(new RegionSelectionResult(_selectionRect, _currentPoint));
    }

    private void TransitionTo(CaptureState newState)
    {
        if (_currentState == newState)
            return;

        _currentState = newState;
        StateChanged?.Invoke(newState);
    }

    /// <summary>
    /// Shifts a moved rectangle back inside the selection bounds without changing its size,
    /// so moving a selection against a monitor edge stops it there (ShareX behavior).
    /// </summary>
    private PixelRect KeepInsideBounds(PixelRect rect)
    {
        if (_selectionBounds is not { } bounds)
            return rect;

        double x = rect.Width >= bounds.Width
            ? bounds.X
            : Math.Clamp(rect.X, bounds.X, bounds.Right - rect.Width);
        double y = rect.Height >= bounds.Height
            ? bounds.Y
            : Math.Clamp(rect.Y, bounds.Y, bounds.Bottom - rect.Height);
        return new PixelRect(x, y, rect.Width, rect.Height);
    }

    private void SetSelectionRect(PixelRect rect)
    {
        if (_selectionBounds is { } bounds)
            rect = rect.Intersect(bounds);

        if (rect == _selectionRect)
            return;

        _selectionRect = rect;
        SelectionChanged?.Invoke(rect);
    }
}
