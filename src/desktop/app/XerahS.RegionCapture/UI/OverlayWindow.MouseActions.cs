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
using Avalonia.Input;
using Avalonia.VisualTree;
using SkiaSharp;
using XerahS.RegionCapture.Models;
using PixelRect = XerahS.RegionCapture.Models.PixelRect;

namespace XerahS.RegionCapture.UI;

public partial class OverlayWindow
{
    private readonly HashSet<MouseButton> _pressedCaptureButtons = [];

    private bool IsCaptureSurface(object? source)
    {
        for (var visual = source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, _inlineTextBox))
                return false;

            if (ReferenceEquals(visual, _captureControl) || ReferenceEquals(visual, _annotationCanvas))
                return true;
        }
        return false;
    }

    private static MouseButton GetCaptureMouseButton(PointerUpdateKind updateKind) => updateKind switch
    {
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => MouseButton.Right,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => MouseButton.Middle,
        PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => MouseButton.XButton1,
        PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => MouseButton.XButton2,
        _ => MouseButton.None
    };

    private void OnCapturePointerMoved(object? sender, PointerEventArgs e)
    {
        if (HandleMonitorStatePointerEvent(e))
            return;

        // Avalonia reports additional button changes as moves while another button is held.
        switch (e.GetCurrentPoint(this).Properties.PointerUpdateKind)
        {
            case PointerUpdateKind.RightButtonPressed:
            case PointerUpdateKind.MiddleButtonPressed:
            case PointerUpdateKind.XButton1Pressed:
            case PointerUpdateKind.XButton2Pressed:
                OnCapturePointerPressed(sender, e);
                break;
            case PointerUpdateKind.RightButtonReleased:
            case PointerUpdateKind.MiddleButtonReleased:
            case PointerUpdateKind.XButton1Released:
            case PointerUpdateKind.XButton2Released:
                OnCapturePointerReleased(sender, e);
                break;
        }
    }

    private void OnCapturePointerPressed(object? sender, PointerEventArgs e)
    {
        if (HandleMonitorStatePointerEvent(e))
            return;

        var button = GetCaptureMouseButton(e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        if (button == MouseButton.None || !IsCaptureSurface(e.Source))
            return;

        e.Handled = true;

        // A click outside the inline text box only commits the text, like a left click does.
        // The button is not recorded, so its release does not run a capture action.
        if (_inlineTextBox != null)
        {
            CommitInlineText();
            return;
        }

        var point = e.GetPosition(_captureControl);
        _captureControl.UpdateAimFromOverlayPointer(point, e.KeyModifiers);

        // ShareX uses right-click while drawing a new region to abandon it. Moving or resizing
        // an existing region runs the configured right-click action on release instead.
        if (button == MouseButton.Right && _captureControl.IsCreatingSelection &&
            _captureControl.TryClearSelectionAt(point))
        {
            _pressedCaptureButtons.Remove(button);
            e.Pointer.Capture(null);
            return;
        }

        _pressedCaptureButtons.Add(button);
    }

    private void OnCapturePointerReleased(object? sender, PointerEventArgs e)
    {
        if (HandleMonitorStatePointerEvent(e))
            return;

        var button = GetCaptureMouseButton(e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        if (button == MouseButton.None)
            return;

        if (!_pressedCaptureButtons.Remove(button))
        {
            if (IsCaptureSurface(e.Source))
                e.Handled = true;
            return;
        }

        e.Handled = true;
        var point = e.GetPosition(_captureControl);
        _captureControl.UpdateAimFromOverlayPointer(point, e.KeyModifiers);
        var action = button switch
        {
            MouseButton.Right => _options.RightClickAction,
            MouseButton.Middle => _options.MiddleClickAction,
            MouseButton.XButton1 => _options.X1ClickAction,
            MouseButton.XButton2 => _options.X2ClickAction,
            _ => RegionCaptureAction.None
        };

        if (_options.Mode == RegionCaptureMode.ScreenColorPicker)
            action = button == MouseButton.Right ? RegionCaptureAction.CancelCapture : RegionCaptureAction.None;

        RunCaptureAction(action, point, _annotationCanvas != null ? e.GetPosition(_annotationCanvas) : e.GetPosition(this));
        if (!_captureControl.IsDraggingSelection && !_isDrawing && !_selectionInteractionActive)
            e.Pointer.Capture(null);
    }

    private void RunCaptureAction(RegionCaptureAction action, Point selectionPoint, Point annotationPoint)
    {
        if (_completionSource.Task.IsCompleted)
            return;

        switch (action)
        {
            case RegionCaptureAction.CancelCapture:
                OnCancelled();
                break;
            case RegionCaptureAction.RemoveShapeCancelCapture:
                if (!TryRemoveShape(selectionPoint, annotationPoint))
                    OnCancelled();
                break;
            case RegionCaptureAction.RemoveShape:
                TryRemoveShape(selectionPoint, annotationPoint);
                break;
            case RegionCaptureAction.SwapToolType when _options.EnableAnnotations:
                if (_captureControl.IsAnnotationMode)
                    _viewModel.ActivateRegionTool();
                else
                    _viewModel.ActivateLastAnnotationTool();
                break;
            case RegionCaptureAction.CaptureFullscreen:
                CompleteCaptureAction(_captureBounds);
                break;
            case RegionCaptureAction.CaptureActiveMonitor:
                CompleteCaptureAction(_monitor.PhysicalBounds);
                break;
            case RegionCaptureAction.CaptureLastRegion:
                CompleteCaptureAction(_options.LastRegion);
                break;
        }
    }

    private bool TryRemoveShape(Point selectionPoint, Point annotationPoint)
    {
        if (_captureControl.IsAnnotationMode)
        {
            int count = _viewModel.EditorCore.Annotations.Count;
            _viewModel.EditorCore.OnPointerPressed(
                new SKPoint((float)annotationPoint.X, (float)annotationPoint.Y), isRightButton: true);
            if (_viewModel.EditorCore.Annotations.Count == count)
                return false;

            _selectionInteractionActive = false;
            SyncAnnotationState();
            RebuildAnnotationCanvas();
            return true;
        }

        return _captureControl.TryClearSelectionAt(selectionPoint);
    }

    private void CompleteCaptureAction(PixelRect region)
    {
        region = region.Intersect(_captureBounds);
        if (region.IsEmpty)
            return;

        CommitInlineText();
        _viewModel.SaveOptions();
        _completionSource.TrySetResult(CreateResultWithAnnotations(
            new RegionSelectionResult(region, _captureControl.CurrentPosition)));
    }
}
