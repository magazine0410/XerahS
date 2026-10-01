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
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Media.Metadata;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.UI.Helpers;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.Views;

[TestFixture, NonParallelizable]
public class HotkeyPrerequisiteTests
{
    [AvaloniaTest]
    public void Inspector_UsesPlatformDetails_ChangesControls_AndClearsClosedWindows()
    {
        var native = new TestWindowService();
        using var model = new InspectWindowViewModel(native);
        model.SelectWindow(new IntPtr(42), true);
        Assert.That(model.SelectedTitle, Is.EqualTo("Test window"));
        Assert.That(model.ClipboardText, Does.Contain("/test/process"));
        Assert.That(model.Details.Any(p => p.Name.Contains("styles")), Is.False, "X11 has no Win32 styles.");
        Assert.That(model.CanPickControl, Is.False);
        model.IsTopMost = true;
        model.Opacity = 40;
        Assert.That(native.Topmost, Is.True);
        Assert.That(native.Alpha, Is.EqualTo(102));
        native.Exists = false;
        model.RefreshCommand.Execute(null);
        Assert.That(model.HasSelection, Is.False);
        Assert.That(model.Details, Is.Empty);
    }

    [AvaloniaTest]
    public void Borderless_UsesRememberedTitleAndWorkingArea_PersistsOnlySuccessfulToggles()
    {
        var options = new BorderlessWindowSettings { WindowTitle = "Editor", ExcludeTaskbarArea = true, AutoCloseWindow = true };
        string? title = null;
        bool area = false, closed = false, succeed = false;
        using var model = new BorderlessWindowViewModel(options, (t, a) => { title = t; area = a; return succeed; });
        model.CloseRequested = () => closed = true;
        model.WindowTitle = "  Terminal  ";
        model.ToggleCommand.Execute(null);
        Assert.That(model.HasError, Is.True);
        Assert.That(options.WindowTitle, Is.EqualTo("Editor"));
        Assert.That(closed, Is.False);
        succeed = true;
        model.ToggleCommand.Execute(null);
        Assert.That(title, Is.EqualTo("Terminal"));
        Assert.That(area, Is.True);
        Assert.That(options.WindowTitle, Is.EqualTo("Terminal"));
        Assert.That(closed, Is.True);
    }

    [AvaloniaTest]
    public void ToolbarEditor_ReordersDuplicateJobsAndSeparators_AndReflectsChanges()
    {
        var saved = SettingsManager.Settings.ActionsToolbarList;
        SettingsManager.Settings.ActionsToolbarList = [WorkflowType.ImageViewer, WorkflowType.None, WorkflowType.ImageViewer];
        int changes = 0;
        var toolbar = new ActionsToolbarWindow();
        var editor = new ActionsToolbarEditorWindow(() => { changes++; toolbar.RefreshToolbar(); }, () => { });
        try
        {
            toolbar.Show();
            editor.Show();
            var list = editor.FindControl<ListBox>("ActionList")!;
            list.SelectedIndex = 2;
            editor.FindControl<Button>("MoveUpButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(SettingsManager.Settings.ActionsToolbarList, Is.EqualTo(new[] { WorkflowType.ImageViewer, WorkflowType.ImageViewer, WorkflowType.None }));
            editor.FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.AddAction(WorkflowType.Metadata);
            Assert.That(SettingsManager.Settings.ActionsToolbarList, Is.EqualTo(new[] { WorkflowType.ImageViewer, WorkflowType.None, WorkflowType.Metadata }));
            Assert.That(changes, Is.EqualTo(3));
            Assert.That(toolbar.FindControl<StackPanel>("ToolbarItems")!.Children, Has.Count.EqualTo(4));
            SavePreview(toolbar, "actions-toolbar");
            foreach (var button in toolbar.FindControl<StackPanel>("ToolbarItems")!.Children.OfType<Button>())
                Assert.That(((TextBlock)button.Content!).Foreground, Is.Not.Null, "Toolbar icons must resolve their theme brush after attachment.");
            SavePreview(editor, "actions-toolbar-editor");
        }
        finally { editor.Close(); toolbar.Close(); SettingsManager.Settings.ActionsToolbarList = saved; }
    }

    [AvaloniaTest]
    public async Task MetadataWindow_LoadsFiltersCopiesAndConfirmsStrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "xerahs-metadata-preview-" + Guid.NewGuid() + ".png");
        using (var bitmap = new SKBitmap(20, 30))
        {
            bitmap.Erase(SKColors.Coral);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(path, data.ToArray());
        }
        var window = new MetadataWindow(path, null);
        try
        {
            window.Show();
            var model = (MetadataViewModel)window.DataContext!;
            while (model.IsBusy) await Task.Delay(10);
            Assert.That(model.HasGroups, Is.True);
            string? copied = null;
            model.CopyTextRequested = text => { copied = text; return Task.CompletedTask; };
            await model.CopyAllCommand.ExecuteAsync(null);
            Assert.That(copied, Does.Contain("PNG").And.Contain("20"));
            model.SearchText = "no-match-for-this";
            Assert.That(model.HasGroups, Is.False);
            model.SearchText = "";
            model.RequestStripCommand.Execute(null);
            Assert.That(model.IsConfirmingStrip, Is.True);
            SavePreview(window, "metadata-strip-confirmation");
            model.CancelStripCommand.Execute(null);
            SavePreview(window, "metadata");
        }
        finally { window.Close(); File.Delete(path); }
    }

    [AvaloniaTest]
    public void WindowToolsAndMouseSettings_RenderWithPlatformDetails()
    {
        var native = new TestWindowService();
        var model = new InspectWindowViewModel(native);
        model.SelectWindow(new IntPtr(42), true);
        var inspect = new InspectWindowWindow(model);
        var borderless = new BorderlessWindowWindow(new BorderlessWindowViewModel(new BorderlessWindowSettings(), (_, _) => true, windows: native));
        var mouse = new MouseHighlighterWindow(new MouseHighlighterOptions(), () => { });
        try
        {
            inspect.Show(); borderless.Show(); mouse.Show();
            SavePreview(inspect, "inspect-window");
            SavePreview(borderless, "borderless-window");
            SavePreview(mouse, "mouse-highlighter");
        }
        finally { inspect.Close(); borderless.Close(); mouse.Close(); }
    }

    [AvaloniaTest]
    public void ToolWindows_UseTheShareXThemeVariant_SoTheirColorsResolve()
    {
        // The ShareX brushes exist only under the ShareXDark/ShareXLight variants; without one,
        // the windows render on a black background with invisible secondary text.
        // The test app runs entirely in ShareXDark; XerahS itself uses the plain Dark or Light variant.
        var application = global::Avalonia.Application.Current!;
        var appTheme = application.RequestedThemeVariant;
        application.RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
        var native = new TestWindowService();
        Window[] windows =
        [
            new MetadataWindow(),
            new InspectWindowWindow(new InspectWindowViewModel(native)),
            new InspectWindowPickerOverlay(),
            new BorderlessWindowWindow(new BorderlessWindowViewModel(new BorderlessWindowSettings(), (_, _) => true, windows: native)),
            new ActionsToolbarWindow(),
            new ActionsToolbarEditorWindow(() => { }, () => { }),
            new MouseHighlighterWindow(new MouseHighlighterOptions(), () => { })
        ];
        try
        {
            foreach (var window in windows)
            {
                window.Show();
                foreach (string key in new[] { "ShareX.Brush.Background.Main", "ShareX.Brush.Text.Secondary", "ShareX.Brush.Border" })
                    Assert.That(window.TryFindResource(key, window.ActualThemeVariant, out var brush) && brush != null, Is.True, $"{window.GetType().Name}: {key}");
            }
        }
        finally
        {
            foreach (var window in windows) window.Close();
            application.RequestedThemeVariant = appTheme;
        }
    }

    [Test]
    public void Toolbar_AllJobsHaveAnExplicitIcon()
    {
        foreach (var job in Enum.GetValues<WorkflowType>())
            Assert.That(WorkflowIcons.GetIcon(job), Is.Not.EqualTo(ShareX.ImageEditor.Presentation.Theming.LucideIcons.circle), job.ToString());
    }

    internal static void SavePreview(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.That(bitmap, Is.Not.Null);
        // Saved only on request, like MediaBrowserViewTests, so test runs do not leave files behind.
        string? root = Environment.GetEnvironmentVariable("XERAHS_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(root)) return;
        string path = Path.Combine(root, "prerequisite-previews");
        Directory.CreateDirectory(path);
        using var stream = File.Create(Path.Combine(path, name + ".png"));
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private sealed class TestWindowService : XerahS.UI.Services.WindowService, IWindowService
    {
        public bool Exists { get; set; } = true;
        public bool Topmost { get; private set; }
        public byte Alpha { get; private set; } = 255;
        public bool SupportsWindowInspection => true;
        public bool SupportsWindowOpacity => true;
        public bool SupportsTopmost => true;
        public bool SetWindowTopmost(IntPtr handle, bool value) { Topmost = value; return true; }
        public bool SetWindowOpacity(IntPtr handle, byte value) { Alpha = value; return true; }
        public WindowDetails? GetWindowDetails(IntPtr handle) => Exists ? new WindowDetails
        {
            Handle = handle, Title = "Test window", ClassName = "Example", ProcessId = 100,
            ProcessName = "process", ProcessFileName = "/test/process", Bounds = new System.Drawing.Rectangle(1, 2, 640, 480),
            ClientBounds = new System.Drawing.Rectangle(0, 0, 640, 460), IsTopmost = Topmost, Opacity = Alpha
        } : null;
    }
}
