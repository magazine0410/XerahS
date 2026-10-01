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
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NUnit.Framework;
using ShareX.ImageEditor.Presentation.Controls;
using XerahS.RegionCapture;
using XerahS.RegionCapture.Models;
using XerahS.RegionCapture.Services;
using XerahS.RegionCapture.UI;
using XerahS.RegionCapture.ViewModels;
using CaptureMonitor = XerahS.RegionCapture.Models.MonitorInfo;
using CaptureRect = XerahS.RegionCapture.Models.PixelRect;
using PixelPoint = XerahS.RegionCapture.Models.PixelPoint;

namespace XerahS.Tests.RegionCapture;

[TestFixture]
[NonParallelizable]
public class ActiveMonitorModeTests
{
    private static readonly CaptureRect LeftBounds = new(0, 0, 640, 480);
    private static readonly CaptureRect RightBounds = new(640, 0, 640, 480);
    private static readonly CaptureMonitor LeftMonitor = new("Left", LeftBounds, LeftBounds, 1, true);
    private static readonly CaptureMonitor RightMonitor = new("Right", RightBounds, RightBounds, 1, false);

    private int[] _focusRetryDelays = [];

    // The overlays retry focusing themselves after 50, 200 and 500 ms. These tests move keyboard
    // focus themselves, so a retry that arrives during a slow run would send their key presses
    // to another overlay. Turn the retries off except where a test sets its own.
    [SetUp]
    public void DisableFocusRetries()
    {
        _focusRetryDelays = OverlayWindow.FocusRetryDelayMs;
        OverlayWindow.FocusRetryDelayMs = [];
    }

    [TearDown]
    public void RestoreFocusRetries() => OverlayWindow.FocusRetryDelayMs = _focusRetryDelays;

    private sealed class Session : IDisposable
    {
        public required OverlayWindow Left { get; init; }
        public required OverlayWindow Right { get; init; }
        public required ActiveMonitorCoordinator Coordinator { get; init; }
        public required TaskCompletionSource<RegionSelectionResult?> Completion { get; init; }

        public void Dispose()
        {
            Coordinator.Dispose();
            Left.Close();
            Right.Close();
        }
    }

    private static Session Open(bool rightIsInitiallyActive = false)
    {
        var completion = new TaskCompletionSource<RegionSelectionResult?>();
        var toolCoordinator = new RegionCaptureAnnotationToolCoordinator();

        OverlayWindow Create(CaptureMonitor monitor)
        {
            var window = new OverlayWindow(monitor, completion, options: new RegionCaptureOptions
            {
                ActiveMonitorMode = true,
                CaptureBounds = monitor.PhysicalBounds,
                EnableWindowSnapping = false,
                EnableMagnifier = false,
                ShowInfo = false,
                UseTransparentOverlay = true,
                SnapSizes = []
            }, annotationToolCoordinator: toolCoordinator);
            window.Show();
            window.UpdateLayout();
            return window;
        }

        var left = Create(LeftMonitor);
        var right = Create(RightMonitor);
        var coordinator = new ActiveMonitorCoordinator([left, right], rightIsInitiallyActive ? right : null);
        return new Session { Left = left, Right = right, Coordinator = coordinator, Completion = completion };
    }

    private static void PressKey(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void Drag(Window window, Point start, Point end)
    {
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
    }

    [Test]
    public void InitialActiveMonitor_UsesCursorOnlyWhenKnown()
    {
        CaptureMonitor[] monitors = [LeftMonitor, RightMonitor];

        Assert.That(ActiveMonitorCoordinator.ResolveInitialActiveMonitor(monitors, new PixelPoint(700, 100)), Is.SameAs(RightMonitor));
        Assert.That(ActiveMonitorCoordinator.ResolveInitialActiveMonitor(monitors, null), Is.Null);
        Assert.That(ActiveMonitorCoordinator.ResolveInitialActiveMonitor(monitors, new PixelPoint(9000, 9000)), Is.Null);
        Assert.That(ActiveMonitorCoordinator.ResolveInitialActiveMonitor([LeftMonitor], null), Is.SameAs(LeftMonitor));
    }

    [Test]
    public void CursorPosition_IsNotTrustedOnWayland()
    {
        Assert.That(CoordinateTranslationService.IsCursorPositionReliable(true, "wayland", null), Is.False);
        Assert.That(CoordinateTranslationService.IsCursorPositionReliable(true, "x11", "wayland-0"), Is.False);
        Assert.That(CoordinateTranslationService.IsCursorPositionReliable(true, "x11", null), Is.True);
        Assert.That(CoordinateTranslationService.IsCursorPositionReliable(false, "wayland", "wayland-0"), Is.True);
    }

    [AvaloniaTest]
    public void UnknownCursor_FirstPointerInputChoosesActiveMonitor()
    {
        using var session = Open();
        Assert.That(session.Left.MonitorState, Is.EqualTo(OverlayMonitorState.Pending));
        Assert.That(session.Right.MonitorState, Is.EqualTo(OverlayMonitorState.Pending));

        session.Right.MouseMove(new Point(100, 100));

        Assert.That(session.Coordinator.ActiveOverlay, Is.SameAs(session.Right));
        Assert.That(session.Right.MonitorState, Is.EqualTo(OverlayMonitorState.Active));
        Assert.That(session.Left.MonitorState, Is.EqualTo(OverlayMonitorState.Inactive));

        // Later input on another monitor does not move the active monitor.
        session.Left.MouseMove(new Point(100, 100));
        Assert.That(session.Coordinator.ActiveOverlay, Is.SameAs(session.Right));

        Drag(session.Right, new Point(100, 200), new Point(300, 360));
        Assert.That(session.Completion.Task.IsCompletedSuccessfully, Is.True);
        Assert.That(session.Completion.Task.Result!.Value.Region, Is.EqualTo(new CaptureRect(740, 200, 200, 160)));
    }

    [AvaloniaTest]
    public void InactiveMonitor_IgnoresPointerAndCaptureKeys()
    {
        using var session = Open(rightIsInitiallyActive: true);
        Assert.That(session.Left.MonitorState, Is.EqualTo(OverlayMonitorState.Inactive));

        // Clicks, drags and secondary buttons on the inactive monitor do not capture or cancel.
        Drag(session.Left, new Point(100, 200), new Point(300, 360));
        Assert.That(session.Completion.Task.IsCompleted, Is.False, "left drag");
        session.Left.MouseDown(new Point(200, 200), MouseButton.Right);
        session.Left.MouseUp(new Point(200, 200), MouseButton.Right);
        Assert.That(session.Completion.Task.IsCompleted, Is.False, "right click");
        session.Left.MouseDown(new Point(200, 200), MouseButton.XButton1);
        session.Left.MouseUp(new Point(200, 200), MouseButton.XButton1);
        Assert.That(session.Completion.Task.IsCompleted, Is.False, "X1 click");
        Assert.That(session.Left.FindControl<AnnotationToolbar>("AnnotationToolbarControl")!.IsVisible, Is.False);
        Assert.That(session.Left.FindControl<Canvas>("AnnotationCanvas")!.IsHitTestVisible, Is.False);

        // A click on the inactive monitor asks for focus to return to the active overlay
        // (headless windows all report IsActive, so the request is counted instead).
        int focusRequests = 0;
        session.Left.ActiveOverlayRequested += _ => focusRequests++;
        session.Left.MouseDown(new Point(50, 50), MouseButton.Left);
        session.Left.MouseUp(new Point(50, 50), MouseButton.Left);
        Assert.That(focusRequests, Is.EqualTo(1));

        // Keys on the inactive monitor do not capture it or pick tools; they ask for focus too.
        session.Left.FocusOverlay();
        PressKey(session.Left, Key.Enter);
        session.Left.FocusOverlay();
        PressKey(session.Left, Key.R);
        Assert.That(session.Completion.Task.IsCompleted, Is.False, "Enter");
        Assert.That(focusRequests, Is.EqualTo(3));
        Assert.That(((RegionCaptureAnnotationViewModel)session.Left.DataContext!).ActiveTool,
            Is.EqualTo(ShareX.ImageEditor.Core.Annotations.EditorTool.Select));

        Drag(session.Right, new Point(100, 200), new Point(300, 360));
        Assert.That(session.Completion.Task.Result!.Value.Region, Is.EqualTo(new CaptureRect(740, 200, 200, 160)));
    }

    private static RegionCaptureControl CaptureControl(OverlayWindow window) =>
        window.FindControl<Panel>("RootPanel")!.Children.OfType<RegionCaptureControl>().Single();

    private static (OverlayWindow Left, OverlayWindow Right, Action Disconnect) OpenTwoMonitors()
    {
        var completion = new TaskCompletionSource<RegionSelectionResult?>();
        OverlayWindow Create(CaptureMonitor monitor)
        {
            var window = new OverlayWindow(monitor, completion, options: new RegionCaptureOptions
            {
                CaptureBounds = new CaptureRect(0, 0, 1280, 480),
                EnableWindowSnapping = false,
                EnableMagnifier = true,
                ShowInfo = true,
                UseTransparentOverlay = true,
                SnapSizes = []
            });
            window.Show();
            window.UpdateLayout();
            return window;
        }

        var left = Create(LeftMonitor);
        var right = Create(RightMonitor);
        return (left, right, OverlayManager.ConnectPointerPresence([left, right]));
    }

    [AvaloniaTest]
    public void CrosshairAndMagnifier_ShowOnlyOnMonitorUnderPointer()
    {
        var (left, right, disconnect) = OpenTwoMonitors();
        try
        {
            left.MouseMove(new Point(100, 100));
            Assert.Multiple(() =>
            {
                Assert.That(CaptureControl(left).IsPointerOnMonitor, Is.True);
                Assert.That(CaptureControl(left).MagnifierForTests.IsVisible, Is.True);
                Assert.That(CaptureControl(right).IsPointerOnMonitor, Is.False);
                Assert.That(CaptureControl(right).MagnifierForTests.IsVisible, Is.False);
            });

            right.MouseMove(new Point(50, 50));
            Assert.Multiple(() =>
            {
                Assert.That(CaptureControl(left).IsPointerOnMonitor, Is.False);
                Assert.That(CaptureControl(left).MagnifierForTests.IsVisible, Is.False);
                Assert.That(CaptureControl(right).IsPointerOnMonitor, Is.True);
                Assert.That(CaptureControl(right).MagnifierForTests.IsVisible, Is.True);
            });
        }
        finally
        {
            disconnect();
            left.Close();
            right.Close();
        }
    }

    [AvaloniaTest]
    public void DragAcrossMonitors_MovesCrosshairToMonitorUnderPointer()
    {
        var (left, right, disconnect) = OpenTwoMonitors();
        try
        {
            // The drag starts on the left overlay, which keeps the pointer capture. Local x=740 is
            // physical x=740, on the right monitor.
            left.MouseDown(new Point(500, 200), MouseButton.Left);
            left.MouseMove(new Point(740, 260), RawInputModifiers.LeftMouseButton);

            Assert.Multiple(() =>
            {
                Assert.That(CaptureControl(left).IsPointerOnMonitor, Is.False);
                Assert.That(CaptureControl(right).IsPointerOnMonitor, Is.True);
                Assert.That(CaptureControl(right).CurrentPointForTests, Is.EqualTo(new PixelPoint(740, 260)));
            });

            left.MouseUp(new Point(740, 260), MouseButton.Left);
        }
        finally
        {
            disconnect();
            left.Close();
            right.Close();
        }
    }

    [AvaloniaTest]
    public void InactiveMonitor_EscapeStillCancels()
    {
        using var session = Open(rightIsInitiallyActive: true);

        PressKey(session.Left, Key.Escape);

        Assert.That(session.Completion.Task.IsCompletedSuccessfully, Is.True);
        Assert.That(session.Completion.Task.Result, Is.Null);
    }

    [AvaloniaTest]
    public void ActiveMonitor_FullscreenActionCapturesOnlyThatMonitor()
    {
        using var session = Open(rightIsInitiallyActive: true);

        session.Right.MouseDown(new Point(200, 200), MouseButton.XButton1);
        session.Right.MouseUp(new Point(200, 200), MouseButton.XButton1);

        Assert.That(session.Completion.Task.Result!.Value.Region, Is.EqualTo(RightBounds));
    }

    [AvaloniaTest]
    public void InactiveMonitor_FocusRetryDoesNotTakeFocusFromTheActiveOverlay()
    {
        using var session = Open(rightIsInitiallyActive: true);
        // The headless platform activates each shown window from a queued job; run those first.
        Dispatcher.UIThread.RunJobs();
        Assert.That(session.Right.IsKeyboardFocusWithin, Is.True);

        session.Left.RetryFocus();
        Assert.That(session.Right.IsKeyboardFocusWithin, Is.True);
        Assert.That(session.Left.IsKeyboardFocusWithin, Is.False);
    }
}
