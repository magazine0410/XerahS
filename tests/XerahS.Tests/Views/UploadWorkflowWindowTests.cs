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
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Bootstrap;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;
using XerahS.Uploaders;

namespace XerahS.Tests.Views;

[TestFixture]
[NonParallelizable]
public class UploadWorkflowWindowTests
{
    [AvaloniaTest]
    public async Task TextPrompt_PreservesMultipleLines_AndCancelReturnsNoInput()
    {
        var prompt = new UploadInputWindow(false);
        var completion = prompt.ShowAsync(null);
        var input = prompt.FindControl<TextBox>("Input")!;
        Assert.That(input.AcceptsReturn, Is.True);
        input.Text = "First line\nSecond line";
        SavePreview(prompt, "upload-text");
        prompt.FindControl<Button>("SubmitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(await completion, Is.EqualTo("First line\nSecond line"));

        var cancelled = new UploadInputWindow(false);
        var dismissed = cancelled.ShowAsync(null);
        cancelled.Close();
        Assert.That(await dismissed, Is.Null);
    }

    [AvaloniaTest]
    public async Task TextPrompt_SelectsClipboardText_AndCountsCharacters()
    {
        // As in ShareX's text upload window, pre-filled clipboard text is selected so typing replaces it.
        var prompt = new UploadInputWindow(false, "Clipboard text");
        var completion = prompt.ShowAsync(null);
        var input = prompt.FindControl<TextBox>("Input")!;
        var count = prompt.FindControl<TextBlock>("CharacterCount")!;
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(input.SelectedText, Is.EqualTo("Clipboard text"));
                Assert.That(count.IsVisible, Is.True);
                Assert.That(count.Text, Is.EqualTo("14 characters"));
            });
            SavePreview(prompt, "upload-text-prefilled");
            input.Text = "x";
            Dispatcher.UIThread.RunJobs();
            Assert.That(count.Text, Is.EqualTo("1 character"));
            Assert.That(UploadInputWindow.FormatCharacterCount(12345), Is.EqualTo(12345.ToString("N0") + " characters"));
        }
        finally { prompt.Close(); }
        Assert.That(await completion, Is.Null);

        var url = new UploadInputWindow(true, "https://example.test/a");
        _ = url.ShowAsync(null);
        try { Assert.That(url.FindControl<TextBlock>("CharacterCount")!.IsVisible, Is.False); }
        finally { url.Close(); }
    }

    [AvaloniaTest]
    public async Task UrlPrompt_RejectsInvalidInput_ThenAcceptsTrimmedUrl()
    {
        var prompt = new UploadInputWindow(true);
        var completion = prompt.ShowAsync(null);
        var input = prompt.FindControl<TextBox>("Input")!;
        var submit = prompt.FindControl<Button>("SubmitButton")!;
        try
        {
            input.Text = "file:///etc/hosts";
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(completion.IsCompleted, Is.False);
            Assert.That(prompt.FindControl<TextBlock>("Validation")!.IsVisible, Is.True);
            SavePreview(prompt, "shorten-url-validation");
            input.Text = "  https://example.test/a  ";
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(await completion, Is.EqualTo("https://example.test/a"));
        }
        finally { prompt.Close(); }
    }

    [TestCase(ContentPlacement.TopLeft, 10, 30)]
    [TestCase(ContentPlacement.TopCenter, 400, 30)]
    [TestCase(ContentPlacement.MiddleCenter, 400, 320)]
    [TestCase(ContentPlacement.BottomRight, 790, 610)]
    public void DropPlacement_UsesWorkingAreaAndScaledWindowSize(ContentPlacement alignment, int x, int y)
    {
        var position = DragDropUploadWindow.CalculatePosition(new PixelRect(0, 20, 1000, 800), new PixelSize(200, 200), alignment, 10);
        Assert.That(position, Is.EqualTo(new PixelPoint(x, y)));
    }

    [AvaloniaTest]
    public async Task DropWindow_AppliesPreferences_AndUploadsDroppedText()
    {
        using var manager = new RecordingManager();
        var settings = new TaskSettings { Job = WorkflowType.DragDropUpload, DestinationInstanceId = "selected-destination", WorkflowId = "drop-workflow" };
        var window = new DragDropUploadWindow();
        window.Configure(settings, manager);
        window.ApplySettings(new ApplicationConfig { DropSize = 180, DropOffset = 25, DropOpacity = 128, DropHoverOpacity = 240 });
        window.Show();
        try
        {
            Assert.That(window.Width, Is.EqualTo(180));
            Assert.That(window.Opacity, Is.EqualTo(128 / 255d));
            Assert.That(window.Topmost, Is.True);
            Assert.That(window.ShowInTaskbar, Is.False);
            SavePreview(window, "drop-target");
            using var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText("https://example.test/dropped-text"));
            Assert.That(DragDropUploadWindow.HasSupportedData(data), Is.True);
            await window.UploadDroppedDataAsync(data);
            Assert.That(manager.Text, Is.EqualTo("https://example.test/dropped-text"));
            Assert.That(manager.LastSettings!.Job, Is.EqualTo(WorkflowType.UploadText));
            Assert.That(manager.LastSettings.DestinationInstanceId, Is.EqualTo(settings.DestinationInstanceId));
            Assert.That(manager.LastSettings.WorkflowId, Is.EqualTo("drop-workflow"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task DropWindow_AppliesTheCustomTextTemplateToDroppedText()
    {
        using var manager = new RecordingManager();
        var settings = new TaskSettings { Job = WorkflowType.DragDropUpload };
        settings.AdvancedSettings.TextCustom = "<p>%input</p>";
        var window = new DragDropUploadWindow();
        window.Configure(settings, manager);
        window.Show();
        try
        {
            using var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText("a & b"));
            await window.UploadDroppedDataAsync(data);
            Assert.That(manager.Text, Is.EqualTo("<p>a &amp; b</p>"), "HTML encoding is on by default, as in ShareX.");

            settings.AdvancedSettings.TextCustomEncodeInput = false;
            Assert.That(UploadWorkflowService.ApplyCustomText("a & b", settings), Is.EqualTo("<p>a & b</p>"));
            settings.AdvancedSettings.TextCustom = "";
            Assert.That(UploadWorkflowService.ApplyCustomText("a & b", settings), Is.EqualTo("a & b"));
        }
        finally { window.Close(); }
    }

    [Test]
    public async Task FolderUpload_AsksBeforeUploadingMoreThanTenFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xerahs-multi-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        for (int i = 0; i < 11; i++) File.WriteAllText(Path.Combine(directory, $"file{i}.txt"), "x");
        var previousPrompt = UploadWorkflowService.ShowMultiUploadConfirmation;
        var previousSave = UploadWorkflowService.SaveApplicationSettings;
        bool previousWarning = SettingsManager.Settings.ShowMultiUploadWarning;
        var prompts = new List<int>();
        var answer = new XerahS.UI.Views.Dialogs.MultiUploadConfirmationResult(false, false);
        int saves = 0;
        UploadWorkflowService.ShowMultiUploadConfirmation = count => { prompts.Add(count); return Task.FromResult(answer); };
        UploadWorkflowService.SaveApplicationSettings = () => { saves++; return Task.CompletedTask; };
        try
        {
            SettingsManager.Settings.ShowMultiUploadWarning = true;
            using var manager = new RecordingManager();
            var settings = new TaskSettings { Job = WorkflowType.FolderUpload };

            await UploadWorkflowService.UploadPathsAsync([directory], settings, manager);
            Assert.That(prompts, Is.EqualTo(new[] { 11 }));
            Assert.That(manager.Files, Is.Empty, "Cancel uploads nothing.");

            answer = new(true, true);
            await UploadWorkflowService.UploadPathsAsync([directory], settings, manager);
            Assert.That(manager.Files, Has.Count.EqualTo(11));
            Assert.That(SettingsManager.Settings.ShowMultiUploadWarning, Is.False);
            Assert.That(saves, Is.EqualTo(1));

            manager.Files.Clear();
            await UploadWorkflowService.UploadPathsAsync([directory], settings, manager);
            Assert.That(prompts, Has.Count.EqualTo(2), "Don't show again stops the warning.");
            Assert.That(manager.Files, Has.Count.EqualTo(11));

            SettingsManager.Settings.ShowMultiUploadWarning = true;
            File.Delete(Path.Combine(directory, "file0.txt"));
            manager.Files.Clear();
            await UploadWorkflowService.UploadPathsAsync([directory], settings, manager);
            Assert.That(prompts, Has.Count.EqualTo(2), "Ten files do not ask.");
            Assert.That(manager.Files, Has.Count.EqualTo(10));
        }
        finally
        {
            UploadWorkflowService.ShowMultiUploadConfirmation = previousPrompt;
            UploadWorkflowService.SaveApplicationSettings = previousSave;
            SettingsManager.Settings.ShowMultiUploadWarning = previousWarning;
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task MultiUploadConfirmation_ReportsTheButtonAndDontShowAgain()
    {
        var window = new XerahS.UI.Views.Dialogs.MultiUploadConfirmationWindow(25);
        window.Show();
        Assert.That(window.FindControl<TextBlock>("MessageText")!.Text, Is.EqualTo("Are you sure you want to upload 25 files?"));
        window.FindControl<CheckBox>("DontShowAgainCheckBox")!.IsChecked = true;
        SavePreview(window, "multi-upload-confirmation");
        window.Close();
        Assert.That(await window.Result, Is.EqualTo(new XerahS.UI.Views.Dialogs.MultiUploadConfirmationResult(false, true)),
            "Don't show again applies to Cancel too, as in ShareX.");
    }

    [Test]
    public async Task FolderUpload_RecursesDeduplicatesAndPreservesWorkflowSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xerahs-folder-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        string first = Path.Combine(directory, "first.txt");
        string nested = Path.Combine(directory, "nested", "second.txt");
        File.WriteAllText(first, "one");
        File.WriteAllText(nested, "two");
        try
        {
            using var manager = new RecordingManager();
            var settings = new TaskSettings { Job = WorkflowType.FolderUpload, WorkflowId = "folder-workflow", DestinationInstanceId = "destination", AfterUploadJob = AfterUploadTasks.None };
            await UploadWorkflowService.UploadPathsAsync([directory, first], settings, manager);
            Assert.That(manager.Files, Is.EquivalentTo(new[] { first, nested }));
            Assert.That(manager.LastSettings!.DestinationInstanceId, Is.EqualTo("destination"));
            Assert.That(manager.LastSettings.WorkflowId, Is.EqualTo("folder-workflow"));
            Assert.That(manager.LastSettings.AfterUploadJob, Is.EqualTo(AfterUploadTasks.None));
            Assert.That(settings.Job, Is.EqualTo(WorkflowType.FolderUpload));

            manager.Files.Clear();
            manager.CancelAfterFirstFile = true;
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await UploadWorkflowService.UploadPathsAsync([directory], settings, manager));
            Assert.That(manager.Files, Has.Count.EqualTo(1), "Stop all uploads prevents starting the next file.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task StopAllUploads_StopsUploadContentBatch_AndLeavesUnstartedItemsPending()
    {
        using var manager = new RecordingManager { PauseTextUpload = true };
        using var viewModel = new UploadContentViewModel(manager);
        viewModel.AddTextItem("first");
        viewModel.AddTextItem("second");
        var upload = viewModel.UploadAllCommand.ExecuteAsync(null);
        await manager.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        UploadCancellationScope.CancelAll();
        manager.Release.SetResult();
        await upload;
        Assert.That(manager.TextCalls, Is.EqualTo(1));
        Assert.That(viewModel.Items[0].ErrorMessage, Is.EqualTo("Upload cancelled"));
        Assert.That(viewModel.Items[1].Status, Is.EqualTo(UploadQueueItemStatus.Pending));
        Assert.That(viewModel.IsUploading, Is.False);
    }

    [Test]
    public async Task UploadContent_UrlItemStartsExplicitShortenerJob()
    {
        using var manager = new RecordingManager();
        using var viewModel = new UploadContentViewModel(manager);
        viewModel.AddURLItem("https://example.test/long");
        await viewModel.UploadAllCommand.ExecuteAsync(null);
        Assert.That(manager.LastSettings!.Job, Is.EqualTo(WorkflowType.ShortenURL));
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
        string directory = Path.Combine(Path.GetTempPath(), "xerahs-hotkey-previews");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private sealed class RecordingManager : IDesktopTaskManager, IDisposable
    {
        public List<string> Files { get; } = [];
        public string? Text { get; private set; }
        public int TextCalls { get; private set; }
        public TaskSettings? LastSettings { get; private set; }
        public bool CancelAfterFirstFile { get; set; }
        public bool PauseTextUpload { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WorkerTask? _worker;
        public event EventHandler<WorkerTask>? TaskStarted;
        public event EventHandler<WorkerTask>? TaskCompleted { add { } remove { } }
        public IEnumerable<WorkerTask> Tasks => [];
        public Task StartFileTask(TaskSettings? settings, string path)
        {
            LastSettings = settings;
            Files.Add(path);
            if (CancelAfterFirstFile) UploadCancellationScope.CancelAll();
            return Task.CompletedTask;
        }
        public async Task StartTextTask(TaskSettings? settings, string text)
        {
            LastSettings = settings;
            Text = text;
            TextCalls++;
            _worker = WorkerTask.Create(settings!);
            TaskStarted?.Invoke(this, _worker);
            if (PauseTextUpload)
            {
                Entered.TrySetResult();
                await Release.Task;
                await _worker.StartAsync();
            }
            else
            {
                _worker.Info.Metadata.UploadURL = "https://example.test/result";
            }
        }
        public Task StartTask(TaskSettings? settings, SKBitmap? inputImage = null) => throw new NotSupportedException();
        public Task StartImageUploadTask(TaskSettings? settings, SKBitmap image) => throw new NotSupportedException();
        public void StopAllTasks() { }
        public void Dispose() => _worker?.Dispose();
    }
}
