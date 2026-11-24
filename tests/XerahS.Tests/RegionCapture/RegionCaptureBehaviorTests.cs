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
using Avalonia.VisualTree;
using NUnit.Framework;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Presentation.Controls;
using ShareX.ImageEditor.Presentation.Emoji;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
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

    private static void PressKey(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    // Annotation visuals are rebuilt in a posted dispatcher job.
    private static void RunJobs() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    // The overlay refocuses itself three times after opening, waiting 50, 200 and 500 ms in turn,
    // and each wait only starts once the dispatcher has run the retry before it. Tests that depend
    // on which control has keyboard focus let all three finish first.
    private static void LetFocusRetriesFinish()
    {
        foreach (int delayMs in new[] { 50, 200, 500 })
        {
            Thread.Sleep(delayMs + 50);
            RunJobs();
        }
    }

    private static Point ToolButtonCenter(Window window, EditorTool tool)
    {
        var toolbar = window.FindControl<AnnotationToolbar>("AnnotationToolbarControl")!;
        var button = toolbar.GetVisualDescendants().OfType<Button>()
            .First(b => b.Tag is ToolbarCustomizationItemViewModel item && item.Tool == tool);
        return button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    }

    [AvaloniaTest]
    public void Enter_FinishesCaptureRightAfterToolbarButtonClick()
    {
        var (window, completion) = Open();
        try
        {
            LetFocusRetriesFinish();
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;

            Click(window, ToolButtonCenter(window, EditorTool.Rectangle), MouseButton.Left);
            Assert.That(vm.ActiveTool, Is.EqualTo(EditorTool.Rectangle));

            PressKey(window, Key.Enter);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Enter_FinishesCaptureAfterUsingToolbarDropdown()
    {
        var (window, completion) = Open();
        try
        {
            LetFocusRetriesFinish();
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            vm.SelectToolCommand.Execute(EditorTool.Rectangle);
            RunJobs();

            var toolbar = window.FindControl<AnnotationToolbar>("AnnotationToolbarControl")!;
            var dropdown = toolbar.GetVisualDescendants().OfType<WidthPickerDropdown>().First(d => d.IsEffectivelyVisible);
            var button = dropdown.FindControl<Button>("DropdownButton")!;
            var popup = dropdown.FindControl<global::Avalonia.Controls.Primitives.Popup>("WidthPopup")!;
            Click(window, button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value, MouseButton.Left);
            RunJobs();
            Assert.That(popup.IsOpen, Is.True);

            var option = popup.Child!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.CommandParameter, 8));
            option.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            RunJobs();
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(vm.StrokeWidth, Is.EqualTo(8));

            PressKey(window, Key.Enter);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void DeleteKey_RemovesSelectedAnnotation_AndShiftDeleteRemovesAll_OnlyInAnnotationMode()
    {
        var (window, completion) = Open();
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var nodeCanvas = window.FindControl<Canvas>("AnnotationHandleCanvas")!;
            vm.SelectToolCommand.Execute(EditorTool.Rectangle);
            Drag(window, new Point(100, 200), new Point(200, 260));
            Drag(window, new Point(300, 300), new Point(400, 360));
            RunJobs();
            var first = vm.EditorCore.Annotations[0];
            Assert.That(nodeCanvas.Children, Is.Not.Empty);

            // The shape drawn last is selected.
            PressKey(window, Key.Delete);
            RunJobs();
            Assert.That(vm.EditorCore.Annotations, Is.EqualTo(new[] { first }));
            Assert.That(nodeCanvas.Children, Is.Empty, "after Delete");

            // Nothing is selected now, so Delete has nothing to remove.
            PressKey(window, Key.Delete);
            RunJobs();
            Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(1));

            Drag(window, new Point(300, 300), new Point(400, 360));
            Click(window, new Point(500, 420), MouseButton.Middle);
            Assert.That(vm.IsRegionToolActive, Is.True);
            PressKey(window, Key.Delete);
            PressKey(window, Key.Delete, RawInputModifiers.Shift);
            RunJobs();
            Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(2), "region mode");

            Click(window, new Point(500, 420), MouseButton.Middle);
            RunJobs();
            Assert.That(nodeCanvas.Children, Is.Not.Empty);
            PressKey(window, Key.Delete, RawInputModifiers.Shift);
            RunJobs();
            Assert.That(vm.EditorCore.Annotations, Is.Empty);
            Assert.That(nodeCanvas.Children, Is.Empty, "after Shift+Delete");

            // The nodes also go when the selected shape is undone or removed with a right click.
            Drag(window, Start, End);
            RunJobs();
            Assert.That(nodeCanvas.Children, Is.Not.Empty);
            PressKey(window, Key.Z, RawInputModifiers.Control);
            RunJobs();
            Assert.That(vm.EditorCore.Annotations, Is.Empty);
            Assert.That(nodeCanvas.Children, Is.Empty, "after undo");

            Drag(window, Start, End);
            RunJobs();
            Assert.That(nodeCanvas.Children, Is.Not.Empty);
            Click(window, new Point(100, 280), MouseButton.Right);
            RunJobs();
            Assert.That(vm.EditorCore.Annotations, Is.Empty);
            Assert.That(nodeCanvas.Children, Is.Empty, "after right-click removal");
            Assert.That(completion.Task.IsCompleted, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void SelectedAnnotation_ShowsGrabbableNodes_OnlyInAnnotationMode_AndNotInTheCapture()
    {
        var (window, completion) = Open();
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var nodeCanvas = window.FindControl<Canvas>("AnnotationHandleCanvas")!;
            List<Point> NodeCenters() => nodeCanvas.Children.OfType<Border>()
                .Select(n => new Point(Canvas.GetLeft(n) + n.Width / 2, Canvas.GetTop(n) + n.Height / 2)).ToList();

            Assert.That(nodeCanvas.Children, Is.Empty);
            vm.SelectToolCommand.Execute(EditorTool.Rectangle);
            Drag(window, Start, End);
            RunJobs();

            // Eight resize nodes and the rotation node, 30 above the top edge.
            Assert.That(NodeCenters(), Has.Count.EqualTo(9));
            Assert.That(NodeCenters(), Does.Contain(Start).And.Contain(End).And.Contain(new Point(200, 170)));

            // The whole drawn node can be grabbed: 6 px from its centre is inside the 15 px node.
            Drag(window, new Point(End.X + 6, End.Y + 6), new Point(340, 400));
            RunJobs();
            Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(1));
            Assert.That(vm.EditorCore.Annotations[0].GetBounds(), Is.EqualTo(new SKRect(100, 200, 340, 400)));
            Assert.That(NodeCenters(), Does.Contain(new Point(340, 400)).And.Contain(new Point(220, 170)));

            Click(window, new Point(500, 420), MouseButton.Middle);
            Assert.That(vm.IsRegionToolActive, Is.True);
            Assert.That(nodeCanvas.Children, Is.Empty, "region mode");
            Click(window, new Point(500, 420), MouseButton.Middle);
            Assert.That(NodeCenters(), Has.Count.EqualTo(9));

            PressKey(window, Key.Enter);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
            using var layer = completion.Task.Result!.Value.AnnotationLayer!;
            Assert.That(layer.GetPixel(100, 300).Alpha, Is.GreaterThan(0), "rectangle border");
            Assert.That(layer.GetPixel(220, 170).Alpha, Is.EqualTo(0), "rotation node");
            Assert.That(layer.GetPixel(220, 185).Alpha, Is.EqualTo(0), "rotation line");
            Assert.That(layer.GetPixel(346, 406).Alpha, Is.EqualTo(0), "corner node");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void DoubleClick_ReopensText_EscapeKeepsIt_AndCommitKeepsItsBounds()
    {
        var (window, completion) = Open();
        try
        {
            LetFocusRetriesFinish();
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var canvas = window.FindControl<Canvas>("AnnotationCanvas")!;
            var nodeCanvas = window.FindControl<Canvas>("AnnotationHandleCanvas")!;
            TextBox? Editor() => canvas.Children.OfType<TextBox>().SingleOrDefault();
            void DoubleClick(Point point)
            {
                Click(window, point, MouseButton.Left);
                Click(window, point, MouseButton.Left);
                RunJobs();
            }

            vm.SelectToolCommand.Execute(EditorTool.Text);
            Drag(window, Start, new Point(300, 260));
            RunJobs();
            Editor()!.Text = "note";
            PressKey(window, Key.Enter);
            RunJobs();
            var text = vm.EditorCore.Annotations.OfType<TextAnnotation>().Single();
            var bounds = text.GetBounds();
            Assert.That(nodeCanvas.Children, Is.Not.Empty);

            // With the Text tool still active, the text just written is the selected annotation.
            DoubleClick(new Point(150, 220));
            Assert.That(Editor()?.Text, Is.EqualTo("note"));
            Assert.That(nodeCanvas.Children, Is.Empty, "nodes are hidden under the text box");
            Editor()!.Text = "discarded";
            PressKey(window, Key.Escape);
            RunJobs();
            Assert.That(Editor(), Is.Null);
            Assert.That(vm.EditorCore.Annotations, Is.EqualTo(new[] { text }));
            Assert.That(text.Text, Is.EqualTo("note"));

            // With the Select tool, any text reopens, selected or not.
            vm.SelectToolCommand.Execute(EditorTool.Rectangle);
            Drag(window, new Point(400, 300), new Point(480, 380));
            vm.SelectToolCommand.Execute(EditorTool.Select);
            DoubleClick(new Point(250, 240));
            Assert.That(Editor()?.Text, Is.EqualTo("note"));
            Editor()!.Text = "changed";
            PressKey(window, Key.Enter);
            RunJobs();

            Assert.That(Editor(), Is.Null);
            Assert.That(text.Text, Is.EqualTo("changed"));
            Assert.That(text.GetBounds(), Is.EqualTo(bounds));
            Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(2));
            Assert.That(completion.Task.IsCompleted, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void DeleteKey_InRegionMode_ClearsTheSelectedRegion_AndKeepsAnnotations()
    {
        var (window, completion) = Open();
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var capture = window.GetVisualDescendants().OfType<RegionCaptureControl>().Single();
            vm.SelectToolCommand.Execute(EditorTool.Rectangle);
            Drag(window, new Point(400, 300), new Point(480, 380));
            Click(window, new Point(500, 420), MouseButton.Middle);
            Assert.That(vm.IsRegionToolActive, Is.True);

            // With an annotation present, the region waits for Enter.
            Drag(window, Start, End);
            Assert.That(capture.HasPendingSelection, Is.True);

            PressKey(window, Key.Delete);
            RunJobs();
            Assert.That(capture.HasPendingSelection, Is.False);
            Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(1));
            Assert.That(completion.Task.IsCompleted, Is.False);

            // Without a region, Enter captures the whole monitor.
            PressKey(window, Key.Enter);
            Assert.That(completion.Task.Result!.Value.Region, Is.EqualTo(MonitorBounds));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void SelectedFreehandStroke_ShowsDashedOutline_OnlyInAnnotationMode()
    {
        var (window, _) = Open();
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var nodeCanvas = window.FindControl<Canvas>("AnnotationHandleCanvas")!;
            vm.SelectToolCommand.Execute(EditorTool.Freehand);
            Drag(window, Start, End);
            RunJobs();
            var stroke = vm.EditorCore.Annotations.OfType<FreehandAnnotation>().Single();
            Assert.That(nodeCanvas.Children, Is.Empty, "a freehand stroke is not selected after drawing");

            vm.SelectToolCommand.Execute(EditorTool.Select);
            Click(window, Inside, MouseButton.Left);
            RunJobs();
            Assert.That(vm.EditorCore.SelectedAnnotation, Is.SameAs(stroke));
            var outlines = nodeCanvas.Children.OfType<global::Avalonia.Controls.Shapes.Polyline>().ToList();
            Assert.That(nodeCanvas.Children, Has.Count.EqualTo(2));
            Assert.That(outlines.Select(o => o.Stroke), Is.EqualTo(new[] { Brushes.Black, Brushes.White }));
            Assert.That(outlines[0].Points, Is.EqualTo(stroke.Points.Select(p => new Point(p.X, p.Y))));

            // The outline follows the stroke when it is moved.
            Drag(window, Inside, new Point(Inside.X + 40, Inside.Y + 20));
            RunJobs();
            outlines = nodeCanvas.Children.OfType<global::Avalonia.Controls.Shapes.Polyline>().ToList();
            Assert.That(outlines[0].Points[0], Is.EqualTo(new Point(Start.X + 40, Start.Y + 20)));

            Click(window, new Point(500, 420), MouseButton.Middle);
            Assert.That(nodeCanvas.Children, Is.Empty, "region mode");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void LeftClickAwayFromTextBox_CommitsTheText_WithoutStartingAnotherAnnotation()
    {
        var (window, completion) = Open();
        try
        {
            LetFocusRetriesFinish();
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var canvas = window.FindControl<Canvas>("AnnotationCanvas")!;
            var emptySpot = new Point(500, 420);
            TextBox? Editor() => canvas.Children.OfType<TextBox>().SingleOrDefault();

            // New text, with the text box holding keyboard focus as it does while typing.
            vm.SelectToolCommand.Execute(EditorTool.Text);
            Drag(window, Start, new Point(300, 260));
            RunJobs();
            Assert.That(Editor()!.IsFocused, Is.True);
            Editor()!.Text = "note";
            Click(window, emptySpot, MouseButton.Left);
            RunJobs();
            Assert.That(Editor(), Is.Null);
            var text = (TextAnnotation)vm.EditorCore.Annotations.Single();
            Assert.That(text.Text, Is.EqualTo("note"));

            // Reopened text.
            Click(window, new Point(150, 220), MouseButton.Left);
            Click(window, new Point(150, 220), MouseButton.Left);
            RunJobs();
            Editor()!.Text = "changed";
            Click(window, emptySpot, MouseButton.Left);
            RunJobs();
            Assert.That(Editor(), Is.Null);
            Assert.That(vm.EditorCore.Annotations, Is.EqualTo(new[] { text }));
            Assert.That(text.Text, Is.EqualTo("changed"));
            Assert.That(completion.Task.IsCompleted, Is.False);

            // The keyboard still reaches the overlay after the text box is gone.
            PressKey(window, Key.Enter);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void ClicksInsideOpenTextBox_StayInTheTextBox()
    {
        var (window, completion) = Open();
        try
        {
            LetFocusRetriesFinish();
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var canvas = window.FindControl<Canvas>("AnnotationCanvas")!;
            var inside = new Point(150, 225);
            void ClickAndDoubleClickInside(string state)
            {
                var bounds = vm.EditorCore.Annotations.Single().GetBounds();
                for (int i = 0; i < 3; i++)
                {
                    Click(window, inside, MouseButton.Left);
                    RunJobs();
                }
                Assert.That(canvas.Children.OfType<TextBox>().ToList(), Has.Count.EqualTo(1), state);
                Assert.That(vm.EditorCore.Annotations, Has.Count.EqualTo(1), state);
                Assert.That(vm.EditorCore.Annotations[0].GetBounds(), Is.EqualTo(bounds), state);
            }

            vm.SelectToolCommand.Execute(EditorTool.Text);
            Drag(window, Start, new Point(300, 260));
            RunJobs();
            ClickAndDoubleClickInside("new, empty");
            canvas.Children.OfType<TextBox>().Single().Text = "note";
            ClickAndDoubleClickInside("new, with text");
            PressKey(window, Key.Enter);
            RunJobs();
            Assert.That(canvas.Children.OfType<TextBox>(), Is.Empty);

            // Reopened at another point, so that these two clicks do not count on from the ones above.
            Click(window, new Point(250, 240), MouseButton.Left);
            Click(window, new Point(250, 240), MouseButton.Left);
            RunJobs();
            ClickAndDoubleClickInside("reopened");
            Assert.That(completion.Task.IsCompleted, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Spotlight_DarkensEverythingOutsideItsBox_InThePreviewAndTheCapture()
    {
        var (window, completion) = Open();
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var canvas = window.FindControl<Canvas>("AnnotationCanvas")!;
            vm.SelectToolCommand.Execute(EditorTool.Spotlight);

            // The darkening shows while the box is dragged, not only after the release.
            window.MouseDown(Start, MouseButton.Left);
            window.MouseMove(Inside, RawInputModifiers.LeftMouseButton);
            Assert.That(canvas.Children.OfType<SpotlightOverlayControl>().Single().IsVisible, Is.True);
            window.MouseMove(End, RawInputModifiers.LeftMouseButton);
            window.MouseUp(End, MouseButton.Left);
            RunJobs();
            Assert.That(canvas.Children.OfType<SpotlightOverlayControl>().ToList(), Has.Count.EqualTo(1));

            // It follows the box when the box is moved.
            Drag(window, Inside, new Point(Inside.X + 200, Inside.Y + 40));
            RunJobs();
            var spotlight = vm.EditorCore.Annotations.OfType<SpotlightAnnotation>().Single();
            Assert.That(spotlight.GetBounds(), Is.EqualTo(new SKRect(300, 240, 500, 400)));

            PressKey(window, Key.Enter);
            using var layer = completion.Task.Result!.Value.AnnotationLayer!;
            byte darkening = spotlight.DarkenOpacity;
            Assert.That(darkening, Is.GreaterThan(0));
            Assert.That(layer.GetPixel(50, 50).Alpha, Is.EqualTo(darkening).Within(1), "far from the box");
            Assert.That(layer.GetPixel(200, 280).Alpha, Is.EqualTo(darkening).Within(1), "where the box was before the move");
            Assert.That(layer.GetPixel(400, 320).Alpha, Is.EqualTo(0), "inside the box");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Spotlight_Blur_ReplacesTheSurroundingsWithTheBlurredScreen()
    {
        var background = new SKBitmap(640, 480, SKColorType.Bgra8888, SKAlphaType.Premul);
        background.Erase(SKColors.SteelBlue);
        var (window, completion) = Open(new RegionCaptureOptions { BackgroundImage = background });
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            vm.SelectToolCommand.Execute(EditorTool.Spotlight);
            Drag(window, Start, End);
            RunJobs();

            // Blur is an option of the selected spotlight.
            vm.SelectToolCommand.Execute(EditorTool.Select);
            vm.SpotlightBlur = 12;
            RunJobs();
            var spotlight = vm.EditorCore.Annotations.OfType<SpotlightAnnotation>().Single();
            Assert.That(spotlight.BlurAmount, Is.EqualTo(12));

            PressKey(window, Key.Enter);
            using var layer = completion.Task.Result!.Value.AnnotationLayer!;
            // Outside the box: the blurred screen, darkened, so fully opaque and bluish. Inside: nothing.
            SKColor outside = layer.GetPixel(500, 420);
            Assert.That(outside.Alpha, Is.EqualTo(255));
            Assert.That(outside.Blue, Is.GreaterThan(outside.Red));
            Assert.That(layer.GetPixel((int)Inside.X, (int)Inside.Y).Alpha, Is.EqualTo(0));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Spotlight_WithoutAnAreaOrDeleted_DarkensNothing_AndLeavesNoLayer()
    {
        var (window, completion) = Open();
        try
        {
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var canvas = window.FindControl<Canvas>("AnnotationCanvas")!;
            vm.SelectToolCommand.Execute(EditorTool.Spotlight);

            // A click without a drag makes a spotlight without an area, which must not darken the screen.
            Click(window, new Point(500, 420), MouseButton.Left);
            RunJobs();
            Assert.That(canvas.Children.OfType<SpotlightOverlayControl>(), Is.Empty);
            PressKey(window, Key.Delete);
            RunJobs();

            Drag(window, Start, End);
            RunJobs();
            Assert.That(canvas.Children.OfType<SpotlightOverlayControl>().ToList(), Has.Count.EqualTo(1));
            PressKey(window, Key.Delete);
            RunJobs();
            Assert.That(vm.EditorCore.Annotations, Is.Empty);
            Assert.That(canvas.Children, Is.Empty, "no spotlight control is left in the canvas");

            PressKey(window, Key.Enter);
            Assert.That(completion.Task.Result!.Value.AnnotationLayer, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void EmojiButton_OpensThePickerInsideTheOverlay_AndAPickedEmojiBecomesASelectedSticker()
    {
        var (window, completion) = Open();
        try
        {
            LetFocusRetriesFinish();
            var vm = (RegionCaptureAnnotationViewModel)window.DataContext!;
            var modalLayer = window.FindControl<Panel>("ModalLayer")!;
            vm.SelectToolCommand.Execute(EditorTool.Rectangle);
            Assert.That(modalLayer.IsVisible, Is.False);

            vm.SelectToolCommand.Execute(EditorTool.Emoji);
            RunJobs();
            var picker = modalLayer.GetVisualDescendants().OfType<EmojiPickerDialogView>().Single();
            Assert.That(modalLayer.IsVisible, Is.True);
            // The picker is 760x640; on this 640x480 monitor it is scaled down to fit.
            window.UpdateLayout();
            var pickerSize = picker.GetTransformedBounds()!.Value.Clip.Size;
            Assert.That(pickerSize.Width, Is.LessThanOrEqualTo(640).And.GreaterThan(300));
            Assert.That(pickerSize.Height, Is.LessThanOrEqualTo(480).And.GreaterThan(300));
            Assert.That(vm.ActiveTool, Is.EqualTo(EditorTool.Rectangle), "Emoji is not a drawing tool");

            // The picker has the keyboard: a letter is not a tool shortcut, Enter does not finish,
            // and Escape closes the picker, not the capture.
            PressKey(window, Key.E);
            PressKey(window, Key.Enter);
            Assert.That(vm.ActiveTool, Is.EqualTo(EditorTool.Rectangle));
            Assert.That(completion.Task.IsCompleted, Is.False);
            // A click on the dimmed area around the picker draws nothing.
            Drag(window, new Point(5, 400), new Point(60, 470));
            Assert.That(vm.EditorCore.Annotations, Is.Empty);
            PressKey(window, Key.Escape);
            Assert.That(modalLayer.IsVisible, Is.False);
            Assert.That(modalLayer.Children, Is.Empty);
            Assert.That(completion.Task.IsCompleted, Is.False);

            vm.SelectToolCommand.Execute(EditorTool.Emoji);
            RunJobs();
            picker = modalLayer.GetVisualDescendants().OfType<EmojiPickerDialogView>().Single();
            var pickerViewModel = (EmojiPickerDialogViewModel)picker.DataContext!;
            pickerViewModel.SelectEmojiCommand.Execute(new EmojiCatalogEntry { Unicode = "1F600", Name = "grinning face" });
            RunJobs();

            Assert.That(modalLayer.IsVisible, Is.False);
            var sticker = (EmojiAnnotation)vm.EditorCore.Annotations.Single();
            Assert.That(sticker.UnicodeSequence, Is.EqualTo("1F600"));
            Assert.That(sticker.GetBounds(), Is.EqualTo(new SKRect(240, 160, 400, 320)), "160 px in the middle of 640x480");
            Assert.That(vm.EditorCore.SelectedAnnotation, Is.SameAs(sticker));
            Assert.That(vm.ActiveTool, Is.EqualTo(EditorTool.Select));
            Assert.That(window.FindControl<Canvas>("AnnotationHandleCanvas")!.Children, Is.Not.Empty);

            // It moves like any other shape, and Enter still finishes the capture.
            Drag(window, new Point(320, 240), new Point(380, 280));
            Assert.That(sticker.GetBounds(), Is.EqualTo(new SKRect(300, 200, 460, 360)));
            PressKey(window, Key.Enter);
            Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
            completion.Task.Result!.Value.AnnotationLayer?.Dispose();
        }
        finally { window.Close(); }
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

    [AvaloniaTest]
    public void SelectedRegion_ShowsSystemCursorAndResizeCursorsOverHandles()
    {
        var (window, _) = Open(new RegionCaptureOptions { QuickCrop = false });
        try
        {
            var control = window.FindControl<Panel>("RootPanel")!.Children.OfType<RegionCaptureControl>().Single();
            window.MouseMove(Inside);
            Assert.That(control.PointerCursorTypeForTests, Is.EqualTo(StandardCursorType.None));

            // Start (100,200) to End (300,360): the pointer ends on the bottom-right handle.
            Drag(window, Start, End);
            Assert.That(control.PointerCursorTypeForTests, Is.EqualTo(StandardCursorType.BottomRightCorner));
            window.MouseMove(new Point(200, 200));
            Assert.That(control.PointerCursorTypeForTests, Is.EqualTo(StandardCursorType.SizeNorthSouth));
            window.MouseMove(Inside);
            Assert.That(control.PointerCursorTypeForTests, Is.EqualTo(StandardCursorType.SizeAll));
            window.MouseMove(new Point(50, 450));
            Assert.That(control.PointerCursorTypeForTests, Is.EqualTo(StandardCursorType.Arrow));

            // Resizing keeps the handle's cursor until the button is released.
            window.MouseMove(new Point(300, 280));
            window.MouseDown(new Point(300, 280), MouseButton.Left);
            window.MouseMove(new Point(340, 300), RawInputModifiers.LeftMouseButton);
            Assert.That(control.PointerCursorTypeForTests, Is.EqualTo(StandardCursorType.SizeWestEast));
            window.MouseUp(new Point(340, 300), MouseButton.Left);

            // Drawing a new region outside the selection brings the crosshair back.
            window.MouseDown(new Point(20, 20), MouseButton.Left);
            window.MouseMove(new Point(60, 60), RawInputModifiers.LeftMouseButton);
            Assert.That(control.PointerCursorTypeForTests, Is.EqualTo(StandardCursorType.None));
            window.MouseUp(new Point(60, 60), MouseButton.Left);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RulerSelection_KeepsCrosshair()
    {
        var (window, _) = Open(new RegionCaptureOptions { QuickCrop = false, Mode = RegionCaptureMode.Ruler });
        try
        {
            var control = window.FindControl<Panel>("RootPanel")!.Children.OfType<RegionCaptureControl>().Single();
            Drag(window, Start, End);
            window.MouseMove(Inside);
            Assert.That(control.PointerCursorTypeForTests, Is.EqualTo(StandardCursorType.None));
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

    [AvaloniaTest]
    public void Toolbar_HidesWhileSelectionDragIsNearIt()
    {
        var (window, _) = Open(new RegionCaptureOptions { QuickCrop = false });
        try
        {
            var toolbar = window.FindControl<AnnotationToolbar>("AnnotationToolbarControl")!;
            var panel = toolbar.GetVisualDescendants().OfType<Border>().First();
            var onToolbar = panel.TranslatePoint(new Point(panel.Bounds.Width / 2, panel.Bounds.Height / 2), window)!.Value;
            var nearToolbar = new Point(onToolbar.X, onToolbar.Y + panel.Bounds.Height / 2 + 20);

            window.MouseMove(nearToolbar);
            Assert.That(toolbar.Opacity, Is.EqualTo(1), "hovering without a drag");

            window.MouseDown(Start, MouseButton.Left);
            window.MouseMove(Inside, RawInputModifiers.LeftMouseButton);
            Assert.That(toolbar.Opacity, Is.EqualTo(1), "drag away from the toolbar");

            window.MouseMove(nearToolbar, RawInputModifiers.LeftMouseButton);
            Assert.That(toolbar.Opacity, Is.EqualTo(0), "drag near the toolbar");
            Assert.That(toolbar.IsHitTestVisible, Is.False);

            window.MouseMove(onToolbar, RawInputModifiers.LeftMouseButton);
            Assert.That(toolbar.Opacity, Is.EqualTo(0), "drag over the toolbar");

            window.MouseMove(Inside, RawInputModifiers.LeftMouseButton);
            Assert.That(toolbar.Opacity, Is.EqualTo(1), "drag moved away again");

            window.MouseMove(onToolbar, RawInputModifiers.LeftMouseButton);
            window.MouseUp(onToolbar, MouseButton.Left);
            Assert.That(toolbar.Opacity, Is.EqualTo(1), "drag ended under the toolbar");
            Assert.That(toolbar.IsHitTestVisible, Is.True);
        }
        finally { window.Close(); }
    }
}
