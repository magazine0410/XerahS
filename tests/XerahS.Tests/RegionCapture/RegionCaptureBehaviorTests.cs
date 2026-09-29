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
using Avalonia.Media;
using NUnit.Framework;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Presentation.Controls;
using SkiaSharp;
using XerahS.RegionCapture;
using XerahS.RegionCapture.Models;
using XerahS.RegionCapture.Services;
using XerahS.RegionCapture.UI;
using XerahS.RegionCapture.ViewModels;
using CaptureMonitor = XerahS.RegionCapture.Models.MonitorInfo;
using CaptureRect = XerahS.RegionCapture.Models.PixelRect;

namespace XerahS.Tests.RegionCapture;

[TestFixture]
[NonParallelizable]
public class RegionCaptureBehaviorTests
{
    private static readonly CaptureRect MonitorBounds = new(0, 0, 640, 480);
    private static readonly CaptureMonitor Monitor = new("Test", MonitorBounds, MonitorBounds, 1, true);
    private static readonly Point Start = new(100, 200);
    private static readonly Point End = new(300, 360);
    private static readonly Point Inside = new(200, 280);

    private static (OverlayWindow Window, TaskCompletionSource<RegionSelectionResult?> Completion) Open(
        RegionCaptureOptions? options = null)
    {
        options ??= new RegionCaptureOptions();
        options = options with
        {
            EnableWindowSnapping = false,
            EnableMagnifier = false,
            ShowInfo = false,
            UseTransparentOverlay = true,
            SnapSizes = [],
            CaptureBounds = options.CaptureBounds ?? MonitorBounds
        };
        var completion = new TaskCompletionSource<RegionSelectionResult?>();
        var window = new OverlayWindow(Monitor, completion, options: options);
        window.Show();
        window.UpdateLayout();
        return (window, completion);
    }

    private static void Click(Window window, Point point, MouseButton button)
    {
        window.MouseDown(point, button);
        window.MouseUp(point, button);
    }

    private static void Drag(Window window, Point start, Point end)
    {
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
    }

    private static void PressKey(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    [AvaloniaTest]
    public void EachSecondaryButton_HonorsCancelAndNone()
    {
        foreach (var action in new[] { RegionCaptureAction.CancelCapture, RegionCaptureAction.None })
        foreach (var button in new[] { MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2 })
        {
            var (window, completion) = Open(new RegionCaptureOptions
            {
                RightClickAction = action,
                MiddleClickAction = action,
                X1ClickAction = action,
                X2ClickAction = action
            });
            try
            {
                Click(window, Inside, button);
                Assert.That(completion.Task.IsCompleted, Is.EqualTo(action == RegionCaptureAction.CancelCapture), button.ToString());
                if (completion.Task.IsCompleted)
                    Assert.That(completion.Task.Result, Is.Null);
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaTest]
    public void SideButtons_CaptureVirtualDesktopAndCurrentMonitor()
    {
        var desktop = new CaptureRect(-640, -120, 1280, 600);
        foreach (var button in new[] { MouseButton.XButton1, MouseButton.XButton2 })
        {
            var (window, completion) = Open(new RegionCaptureOptions { CaptureBounds = desktop });
            try
            {
                Click(window, Inside, button);
                Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
                Assert.That(completion.Task.Result!.Value.Region,
                    Is.EqualTo(button == MouseButton.XButton1 ? desktop : MonitorBounds));
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaTest]
    public void CaptureLastRegion_ClipsToSessionBounds_AndIgnoresMissingRegion()
    {
        foreach (var lastRegion in new[] { CaptureRect.Empty, new CaptureRect(-50, 200, 200, 100), new CaptureRect(900, 0, 100, 100) })
        {
            var (window, completion) = Open(new RegionCaptureOptions
            {
                X1ClickAction = RegionCaptureAction.CaptureLastRegion,
                LastRegion = lastRegion
            });
            try
            {
                Click(window, Inside, MouseButton.XButton1);
                var expected = lastRegion.Intersect(MonitorBounds);
                Assert.That(completion.Task.IsCompleted, Is.EqualTo(!expected.IsEmpty));
                if (!expected.IsEmpty)
                    Assert.That(completion.Task.Result!.Value.Region, Is.EqualTo(expected));
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaTest]
    public void RightClick_RemovesSelectedRegion_ThenCancelsOnEmptySpace()
    {
        var (window, completion) = Open(new RegionCaptureOptions { QuickCrop = false });
        try
        {
            Drag(window, Start, End);
            Click(window, Inside, MouseButton.Right);
            Assert.That(completion.Task.IsCompleted, Is.False);
            var control = window.FindControl<Panel>("RootPanel")!.Children.OfType<RegionCaptureControl>().Single();
            Assert.That(control.TryConfirmCurrentSelection(), Is.False);

            Click(window, Inside, MouseButton.Right);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(completion.Task.Result, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RemoveShapeOnly_RemovesAnnotationsWithoutCancelling()
    {
        var (window, completion) = Open(new RegionCaptureOptions { X2ClickAction = RegionCaptureAction.RemoveShape });
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            vm.SelectToolCommand.Execute(EditorTool.Rectangle);
            Drag(window, Start, End);
            Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(1));

            Click(window, new Point(100, 280), MouseButton.XButton2);
            Assert.That(vm.EditorCore.Annotations, Is.Empty);
            Click(window, Inside, MouseButton.XButton2);
            Assert.That(completion.Task.IsCompleted, Is.False);

            Click(window, Inside, MouseButton.Right);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(completion.Task.Result, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void MiddleClick_SwapsRegionAndLastAnnotationTool_WithExistingShapes()
    {
        var (window, completion) = Open();
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            vm.SelectToolCommand.Execute(EditorTool.Arrow);
            Drag(window, Start, End);
            Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(1));

            Click(window, Inside, MouseButton.Middle);
            Assert.That(vm.IsRegionToolActive, Is.True);
            Assert.That(window.FindControl<Canvas>("AnnotationCanvas")!.IsHitTestVisible, Is.False);

            Click(window, Inside, MouseButton.Middle);
            Assert.That(vm.ActiveTool, Is.EqualTo(EditorTool.Arrow));
            Assert.That(window.FindControl<Canvas>("AnnotationCanvas")!.IsHitTestVisible, Is.True);
            Assert.That(completion.Task.IsCompleted, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void DisabledAnnotations_BlockToolbarShortcutsAndSwap_ButAllowCapture()
    {
        var (window, completion) = Open(new RegionCaptureOptions { EnableAnnotations = false });
        try
        {
            PressKey(window, Key.R);
            PressKey(window, Key.Tab);
            Click(window, Inside, MouseButton.Middle);
            window.ShowAnnotationToolbar();

            Assert.That(window.FindControl<AnnotationToolbar>("AnnotationToolbarControl")!.IsVisible, Is.False);
            Assert.That(window.FindControl<Canvas>("AnnotationCanvas")!.IsHitTestVisible, Is.False);
            Assert.That(((RegionCaptureAnnotationViewModel)window.DataContext!).ActiveTool, Is.EqualTo(EditorTool.Select));

            Drag(window, Start, End);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(completion.Task.Result!.Value.AnnotationLayer, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void QuickCapture_ControlsWhetherReleaseFinishesOrEnterIsRequired()
    {
        foreach (bool quickCapture in new[] { true, false })
        {
            var (window, completion) = Open(new RegionCaptureOptions { QuickCrop = quickCapture });
            try
            {
                Drag(window, Start, End);
                Assert.That(completion.Task.IsCompleted, Is.EqualTo(quickCapture));
                if (!quickCapture)
                {
                    Drag(window, End, new Point(340, 400));
                    Assert.That(completion.Task.IsCompleted, Is.False);
                    PressKey(window, Key.Enter);
                }
                Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
                Assert.That(completion.Task.Result!.Value.Region,
                    Is.EqualTo(quickCapture ? new CaptureRect(100, 200, 200, 160) : new CaptureRect(100, 200, 240, 200)));
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaTest]
    public void AnnotatedSelection_RemainsEditable_AndOneEnterFinishes()
    {
        var (window, completion) = Open();
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            vm.SelectToolCommand.Execute(EditorTool.Rectangle);
            Drag(window, new Point(150, 230), new Point(200, 270));
            Click(window, Inside, MouseButton.Middle);
            Drag(window, Start, End);
            Assert.That(completion.Task.IsCompleted, Is.False);
            Drag(window, End, new Point(340, 400));

            PressKey(window, Key.Enter);

            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
            var result = completion.Task.Result!.Value;
            using var layer = result.AnnotationLayer;
            Assert.That(result.Region, Is.EqualTo(new CaptureRect(100, 200, 240, 200)));
            Assert.That(layer, Is.Not.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void SecondaryRelease_DoesNotFinishLeftButtonDrag()
    {
        var (window, completion) = Open(new RegionCaptureOptions { MiddleClickAction = RegionCaptureAction.None });
        try
        {
            window.MouseDown(Start, MouseButton.Left);
            window.MouseMove(Inside, RawInputModifiers.LeftMouseButton);
            window.MouseDown(Inside, MouseButton.Middle, RawInputModifiers.LeftMouseButton);
            window.MouseUp(Inside, MouseButton.Middle, RawInputModifiers.LeftMouseButton);
            Assert.That(completion.Task.IsCompleted, Is.False);
            window.MouseMove(End, RawInputModifiers.LeftMouseButton);
            window.MouseUp(End, MouseButton.Left);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(completion.Task.Result!.Value.Region, Is.EqualTo(new CaptureRect(100, 200, 200, 160)));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void LeftReleaseWhileSecondaryHeld_FinishesSelectionAndAnnotation()
    {
        foreach (bool annotate in new[] { false, true })
        {
            var (window, completion) = Open(new RegionCaptureOptions { MiddleClickAction = RegionCaptureAction.None });
            try
            {
                var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
                if (annotate)
                    vm.SelectToolCommand.Execute(EditorTool.Rectangle);

                window.MouseDown(Start, MouseButton.Left);
                window.MouseMove(End, RawInputModifiers.LeftMouseButton);
                window.MouseDown(End, MouseButton.Middle, RawInputModifiers.LeftMouseButton);
                window.MouseUp(End, MouseButton.Left, RawInputModifiers.MiddleMouseButton);
                Assert.That(completion.Task.IsCompleted, Is.EqualTo(!annotate));
                window.MouseUp(End, MouseButton.Middle);

                if (annotate)
                {
                    Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(1));
                    var annotation = vm.EditorCore.Annotations[0];
                    var endPoint = annotation.EndPoint;
                    window.MouseMove(new Point(500, 420));
                    Assert.That(annotation.EndPoint, Is.EqualTo(endPoint));
                }
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaTest]
    public void RightClickDuringDrag_ClearsUnfinishedSelectionWithoutClosing()
    {
        var (window, completion) = Open();
        try
        {
            window.MouseDown(Start, MouseButton.Left);
            window.MouseMove(Inside, RawInputModifiers.LeftMouseButton);
            window.MouseDown(Inside, MouseButton.Right, RawInputModifiers.LeftMouseButton);
            window.MouseUp(Inside, MouseButton.Right, RawInputModifiers.LeftMouseButton);
            window.MouseUp(Inside, MouseButton.Left);
            Assert.That(completion.Task.IsCompleted, Is.False);

            Drag(window, Start, End);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void DimmingAndCenterCrosshair_AffectRenderedPixels()
    {
        using var enabled = RenderSelection(showCenterCrosshair: true, dimOpacity: 0.5);
        using var disabled = RenderSelection(showCenterCrosshair: false, dimOpacity: 0);

        Assert.That(enabled.GetPixel(205, 280).Red, Is.GreaterThan(220));
        Assert.That(disabled.GetPixel(205, 280).Red, Is.InRange(115, 125));
        Assert.That(enabled.GetPixel(500, 400).Red, Is.InRange(55, 65));
        Assert.That(disabled.GetPixel(500, 400).Red, Is.InRange(115, 125));
    }

    private static SKBitmap RenderSelection(bool showCenterCrosshair, double dimOpacity)
    {
        var control = new RegionCaptureControl(Monitor, new RegionCaptureOptions
        {
            QuickCrop = false,
            EnableWindowSnapping = false,
            EnableMagnifier = false,
            ShowInfo = false,
            ShowScreenCrosshair = false,
            ShowCenterCrosshair = showCenterCrosshair,
            DimOpacity = dimOpacity,
            UseTransparentOverlay = true,
            SnapSizes = []
        });
        var window = new Window
        {
            Width = 640,
            Height = 480,
            Background = new SolidColorBrush(Color.FromRgb(120, 140, 160)),
            Content = control
        };
        try
        {
            window.Show();
            Drag(window, Start, End);
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            using var stream = new MemoryStream();
            frame!.Save(stream, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            return SKBitmap.Decode(stream);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RightClickWhileTyping_CommitsTextWithoutRunningAction()
    {
        var (window, completion) = Open();
        try
        {
            // The overlay refocuses itself 50, 200 and 500 ms after opening, which would commit the
            // text box before the right-click. Let those retries finish first.
            Thread.Sleep(600);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            vm.SelectToolCommand.Execute(EditorTool.Text);
            Drag(window, Start, new Point(300, 260));

            var canvas = window.FindControl<Canvas>("AnnotationCanvas")!;
            var textBox = canvas.Children.OfType<TextBox>().Single();
            textBox.Text = "note";

            // Default right-click action is "remove shape or cancel"; an empty spot would cancel.
            Click(window, new Point(500, 420), MouseButton.Right);

            Assert.That(completion.Task.IsCompleted, Is.False);
            Assert.That(canvas.Children.OfType<TextBox>(), Is.Empty);
            Assert.That(vm.EditorCore.Annotations.OfType<TextAnnotation>().Single().Text, Is.EqualTo("note"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RightClickWhileMovingSelection_RunsConfiguredAction()
    {
        var (window, completion) = Open(new RegionCaptureOptions
        {
            QuickCrop = false,
            RightClickAction = RegionCaptureAction.None
        });
        try
        {
            Drag(window, Start, End);
            window.MouseDown(Inside, MouseButton.Left);
            window.MouseMove(new Point(210, 290), RawInputModifiers.LeftMouseButton);
            window.MouseDown(new Point(210, 290), MouseButton.Right, RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(210, 290), MouseButton.Right, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(220, 300), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(220, 300), MouseButton.Left);
            Assert.That(completion.Task.IsCompleted, Is.False);

            // Action "None" keeps the moved selection.
            PressKey(window, Key.Enter);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(completion.Task.Result!.Value.Region, Is.EqualTo(new CaptureRect(120, 220, 200, 160)));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RightClickWhileMovingSelection_DefaultActionRemovesItWithoutCancelling()
    {
        var (window, completion) = Open(new RegionCaptureOptions { QuickCrop = false });
        try
        {
            Drag(window, Start, End);
            window.MouseDown(Inside, MouseButton.Left);
            window.MouseMove(new Point(210, 290), RawInputModifiers.LeftMouseButton);
            window.MouseDown(new Point(210, 290), MouseButton.Right, RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(210, 290), MouseButton.Right, RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(210, 290), MouseButton.Left);

            var control = window.FindControl<Panel>("RootPanel")!.Children.OfType<RegionCaptureControl>().Single();
            Assert.That(completion.Task.IsCompleted, Is.False);
            Assert.That(control.TryConfirmCurrentSelection(), Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RightClickWhileDrawingRegion_ClearsItWhateverTheAction()
    {
        var (window, completion) = Open(new RegionCaptureOptions
        {
            QuickCrop = false,
            RightClickAction = RegionCaptureAction.None
        });
        try
        {
            window.MouseDown(Start, MouseButton.Left);
            window.MouseMove(Inside, RawInputModifiers.LeftMouseButton);
            window.MouseDown(Inside, MouseButton.Right, RawInputModifiers.LeftMouseButton);
            window.MouseUp(Inside, MouseButton.Right, RawInputModifiers.LeftMouseButton);
            window.MouseUp(Inside, MouseButton.Left);

            var control = window.FindControl<Panel>("RootPanel")!.Children.OfType<RegionCaptureControl>().Single();
            Assert.That(completion.Task.IsCompleted, Is.False);
            Assert.That(control.TryConfirmCurrentSelection(), Is.False);
        }
        finally { window.Close(); }
    }

    [Test]
    public void ToolSwap_SynchronizesRegionModeAndRestoresAnnotationToolAcrossMonitors()
    {
        var coordinator = new RegionCaptureAnnotationToolCoordinator();
        var first = new RegionCaptureAnnotationViewModel();
        var second = new RegionCaptureAnnotationViewModel();
        coordinator.Register(first);
        coordinator.Register(second);
        first.SelectToolCommand.Execute(EditorTool.Ellipse);
        first.ActivateRegionTool();
        Assert.That(second.IsRegionToolActive, Is.True);
        second.ActivateLastAnnotationTool();
        Assert.That(first.IsRegionToolActive, Is.False);
        Assert.That(first.ActiveTool, Is.EqualTo(EditorTool.Ellipse));
    }
}
