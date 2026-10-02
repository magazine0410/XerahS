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
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Tests.Xip0052;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.Views;

[TestFixture, NonParallelizable]
public class AfterCaptureWindowsTests
{
    [AvaloniaTest]
    public void QuickMenu_ContinuePreservesTasks_AndPresetReplacesBothTaskLists()
    {
        var presets = SettingsManager.Settings.QuickTaskPresets;
        try
        {
            SettingsManager.Settings.QuickTaskPresets = [new() { Name = "Copy", AfterCapture = AfterCaptureTasks.CopyImageToClipboard, AfterUpload = AfterUploadTasks.None }];
            var settings = new TaskSettings { AfterCaptureJob = AfterCaptureTasks.SaveImageToFile, AfterUploadJob = AfterUploadTasks.CopyURLToClipboard };
            QuickTaskMenuResult? accepted = null;
            var menu = AfterCaptureInteractionService.CreateQuickTaskMenu(settings, result => accepted = result);
            var items = menu.Items.OfType<MenuItem>().ToList();
            items.First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(accepted, Is.EqualTo(QuickTaskMenuResult.Continue));
            Assert.That(settings.AfterCaptureJob, Is.EqualTo(AfterCaptureTasks.SaveImageToFile));
            items.Single(item => Equals(item.Header, "Copy")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(settings.AfterCaptureJob, Is.EqualTo(AfterCaptureTasks.CopyImageToClipboard));
            Assert.That(settings.AfterUploadJob, Is.EqualTo(AfterUploadTasks.None));
            items.Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(accepted, Is.EqualTo(QuickTaskMenuResult.Cancel));
        }
        finally { SettingsManager.Settings.QuickTaskPresets = presets; }
    }

    [AvaloniaTest]
    public async Task QuickMenu_TaskCancellationClosesThePopup()
    {
        using var token = new CancellationTokenSource();
        var menu = AfterCaptureInteractionService.ShowQuickTaskMenuAsync(new TaskSettings(), token.Token);
        Dispatcher.UIThread.RunJobs();
        token.Cancel();
        Dispatcher.UIThread.RunJobs();
        Assert.CatchAsync<OperationCanceledException>(async () => await menu.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public void QuickMenuEditor_CancelDoesNotSaveEdits_AndShowsNamedTaskOptions()
    {
        var presets = SettingsManager.Settings.QuickTaskPresets;
        var window = new QuickTaskMenuEditorWindow();
        window.Show();
        try
        {
            var vm = (QuickTaskMenuEditorViewModel)window.DataContext!;
            vm.SelectedPreset!.Name = "Unsaved edit";
            Assert.That(presets[0].Name, Is.Not.EqualTo("Unsaved edit"));
            Assert.That(vm.SelectedPreset.CaptureOptions.Select(option => option.Name), Does.Contain("Beautify image"));
            SavePreview(window, "quick-task-editor");
        }
        finally { window.Close(); }
        Assert.That(SettingsManager.Settings.QuickTaskPresets, Is.SameAs(presets));
    }

    [AvaloniaTest]
    public async Task BeforeUpload_CloseCancelsAndDoesNotChangeDestination()
    {
        var settings = new TaskSettings { DestinationInstanceId = "original" };
        using var image = new SKBitmap(120, 80);
        image.Erase(SKColors.CornflowerBlue);
        var window = new BeforeUploadWindow(new TaskInfo(settings) { Metadata = new(image) });
        var completion = window.ShowAsync(default);
        var vm = (BeforeUploadViewModel)window.DataContext!;
        vm.SelectedDestination = vm.Destinations[0];
        Assert.That(vm.Preview, Is.Not.Null);
        SavePreview(window, "before-upload");
        window.Close();
        Assert.That(await completion, Is.False);
        Assert.That(settings.DestinationInstanceId, Is.EqualTo("original"));
    }

    [AvaloniaTest]
    public async Task BeforeUpload_UploadAppliesSelection_AndCancellationTokenClosesWindow()
    {
        var settings = new TaskSettings { DestinationInstanceId = "original" };
        var window = new BeforeUploadWindow(new TaskInfo(settings));
        var completion = window.ShowAsync(default);
        var viewModel = (BeforeUploadViewModel)window.DataContext!;
        viewModel.SelectedDestination = viewModel.Destinations[0];
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Upload"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(await completion, Is.True);
        Assert.That(settings.DestinationInstanceId, Is.Empty);

        using var token = new CancellationTokenSource();
        var cancelled = new BeforeUploadWindow(new TaskInfo());
        var cancellation = cancelled.ShowAsync(token.Token);
        token.Cancel();
        Dispatcher.UIThread.RunJobs();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await cancellation);
        Assert.That(cancelled.IsVisible, Is.False);
    }

    [AvaloniaTest]
    public void ActionsSettings_DefaultInheritanceAndOrderArePreserved()
    {
        var previous = SettingsManager.DefaultTaskSettings.ExternalPrograms;
        try
        {
            SettingsManager.DefaultTaskSettings.ExternalPrograms = [new ExternalProgram("Default action", "/usr/bin/true")];
            var settings = new TaskSettings { UseDefaultActions = true };
            var vm = new TaskSettingsViewModel(settings, new FakeUiViewModelFactory().ViewDialogService);
            Assert.That(vm.ActionsSettingsEnabled, Is.False);
            Assert.That(vm.Actions.Single().Name, Is.EqualTo("Default action"));
            vm.OverrideActions = true;
            vm.AddActionCommand.Execute(null);
            vm.SelectedAction!.Name = "First";
            vm.AddActionCommand.Execute(null);
            vm.SelectedAction!.Name = "Second";
            vm.MoveActionUpCommand.Execute(null);
            Assert.That(settings.ExternalPrograms.Select(action => action.Name), Is.EqualTo(new[] { "Second", "First" }));
            Assert.That(SettingsManager.DefaultTaskSettings.ExternalPrograms.Single().Name, Is.EqualTo("Default action"));
            var window = new SurfaceWindow { Width = 900, Height = 660, RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme(), Content = new TaskActionsSettingsPanel { DataContext = vm } };
            window.Show();
            try { SavePreview(window, "actions-settings"); }
            finally { window.Close(); }
            vm.OverrideActions = false;
            Assert.That(vm.Actions.Single().Name, Is.EqualTo("Default action"));
        }
        finally { SettingsManager.DefaultTaskSettings.ExternalPrograms = previous; }
    }

    private static void SavePreview(Window window, string name)
    {
        window.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://XerahS.UI/"))
        {
            Source = new Uri("avares://XerahS.UI/Themes/ThemeResources.axaml")
        });
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        string? directory = Environment.GetEnvironmentVariable("XERAHS_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
