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

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.ML.OnnxRuntime;
using Newtonsoft.Json;
using NUnit.Framework;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Core.BackgroundRemoval;
using ShareX.ImageEditor.Hosting;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
using SkiaSharp;
using XerahS.Common;
using XerahS.Bootstrap;
using XerahS.Core;
using XerahS.Core.Hotkeys;
using XerahS.Core.Tasks.Processors;
using XerahS.Platform.Abstractions;
using XerahS.Tests.Xip0052;
using XerahS.UI.Helpers;
using XerahS.UI.Services;
using HostEditorWindow = XerahS.UI.Views.EditorWindow;

namespace XerahS.Tests.Editor;

[TestFixture]
[NonParallelizable]
public class ImageEditingIntegrationTests
{
    private WorkflowsConfig _previousWorkflows = null!;
    private TaskSettingsTools _previousTools = null!;
    private string _previousWorkflowsPath = null!;
    private object? _previousPersonalFolder;
    private object? _previousFolderOverride;
    private string _directory = null!;
    private static readonly FieldInfo PersonalFolderField = typeof(PathsManager).GetField("_personalFolder", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FolderOverrideField = typeof(PathsManager).GetField("_personalFolderOverrideSet", BindingFlags.Static | BindingFlags.NonPublic)!;

    [SetUp]
    public void SetUp()
    {
        _previousWorkflows = SettingsManager.WorkflowsConfig;
        _previousTools = SettingsManager.DefaultTaskSettings.ToolsSettings;
        _previousWorkflowsPath = SettingsManager.Settings.CustomWorkflowsConfigPath;
        _previousPersonalFolder = PersonalFolderField.GetValue(null);
        _previousFolderOverride = FolderOverrideField.GetValue(null);
        _directory = Path.Combine(Path.GetTempPath(), $"xerahs-image-editing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        PathsManager.PersonalFolder = _directory;
        SettingsManager.Settings.CustomWorkflowsConfigPath = _directory;
        SettingsManager.DefaultTaskSettings.ToolsSettings = new TaskSettingsTools();
        SettingsManager.WorkflowsConfig = new WorkflowsConfig { Hotkeys = [], DefaultTaskSettings = SettingsManager.DefaultTaskSettings };
        PlatformServices.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        SettingsManager.WorkflowsConfig = _previousWorkflows;
        SettingsManager.DefaultTaskSettings.ToolsSettings = _previousTools;
        SettingsManager.Settings.CustomWorkflowsConfigPath = _previousWorkflowsPath;
        PersonalFolderField.SetValue(null, _previousPersonalFolder);
        FolderOverrideField.SetValue(null, _previousFolderOverride);
        PlatformServices.Reset();
        Directory.Delete(_directory, recursive: true);
    }

    [TestCase(WorkflowType.BackgroundRemover)]
    [TestCase(WorkflowType.ImageComparer)]
    [TestCase(WorkflowType.IconConverter)]
    [TestCase(WorkflowType.ImageBeautifier)]
    public void NewTools_AreRoutableImageWorkflows_WithoutRenumberingOldJobs(WorkflowType job)
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorkflowCatalog.IsToolWorkflow(job), Is.True);
            Assert.That(TaskHelpers.GetJobMediaType(job), Is.EqualTo(TaskHelpers.JobMediaType.Image));
            Assert.That(ToolNavigationRegistry.TryResolve($"Tools_{job}", out var route), Is.True);
            Assert.That(route.WorkflowType, Is.EqualTo(job));
            Assert.That(NavigationSearchKeywords.ForTag($"Tools_{job}"), Is.Not.Empty);
            Assert.That((int)job, Is.GreaterThan((int)WorkflowType.ExitShareX));
        });
    }

    [Test]
    public void Options_AreIsolatedPerTask_AndExecutionCopiesReferToSavedOptions()
    {
        var first = AddWorkflow("first");
        var second = AddWorkflow("second");
        var executionCopy = new TaskSettings { WorkflowId = "first" };
        var options = ImageEditorOptionsStore.GetEditorOptions(executionCopy);
        options.Thickness = 17;
        options.BackgroundMargin = 123;

        Assert.Multiple(() =>
        {
            Assert.That(options, Is.SameAs(first.ToolsSettings.ImageEditorOptions));
            Assert.That(first.ToolsSettings.ImageEditorOptions.Thickness, Is.EqualTo(17));
            Assert.That(second.ToolsSettings.ImageEditorOptions.Thickness, Is.EqualTo(4));
            Assert.That(ImageEditorOptionsStore.GetEditorOptions(), Is.SameAs(SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions));
            Assert.That(SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions.Thickness, Is.EqualTo(4));
        });

        var removedWorkflow = new TaskSettings { WorkflowId = "removed" };
        Assert.That(ImageEditorOptionsStore.GetEditorOptions(removedWorkflow), Is.SameAs(removedWorkflow.ToolsSettings.ImageEditorOptions));
    }

    [Test]
    public async Task Preferences_RoundTripThroughWorkflowStorage()
    {
        var settings = AddWorkflow("saved");
        var options = settings.ToolsSettings.ImageEditorOptions;
        options.ShowExitConfirmation = false;
        options.BackgroundMargin = 96;
        options.BackgroundType = "Gradient";
        options.Thickness = 11;
        options.RecentImageFiles = ["example.png"];
        options.ToolbarItems = [new ImageEditorToolbarItemOptions { Id = "rectangle", Hotkey = "R" }];
        settings.ToolsSettings.BackgroundRemoverOptions.SelectedDevice = BackgroundRemovalDevice.CPU;
        settings.ToolsSettings.BackgroundRemoverOptions.SelectedModelFileName = "model.onnx";

        await ImageEditorOptionsStore.PersistAsync();
        var loaded = WorkflowsConfig.Load(SettingsManager.WorkflowsConfigFilePath, fallbackSupport: false);
        var tools = loaded.Hotkeys.Single(workflow => workflow.Id == "saved").TaskSettings.ToolsSettings;

        Assert.Multiple(() =>
        {
            Assert.That(tools.ImageEditorOptions.ShowExitConfirmation, Is.False);
            Assert.That(tools.ImageEditorOptions.BackgroundMargin, Is.EqualTo(96));
            Assert.That(tools.ImageEditorOptions.BackgroundType, Is.EqualTo("Gradient"));
            Assert.That(tools.ImageEditorOptions.Thickness, Is.EqualTo(11));
            Assert.That(tools.ImageEditorOptions.RecentImageFiles, Is.EqualTo(new[] { "example.png" }));
            Assert.That(tools.ImageEditorOptions.ToolbarItems, Has.Count.EqualTo(1));
            Assert.That(tools.BackgroundRemoverOptions.SelectedDevice, Is.EqualTo(BackgroundRemovalDevice.CPU));
            Assert.That(tools.BackgroundRemoverOptions.SelectedModelFileName, Is.EqualTo("model.onnx"));
            Assert.That(File.ReadAllText(SettingsManager.WorkflowsConfigFilePath), Does.Not.Contain("ToolsSettingsReference"));
        });
    }

    [Test]
    public void OldSettings_ReceiveEditorDefaults()
    {
        var settings = JsonConvert.DeserializeObject<TaskSettings>("{\"ToolsSettings\":{\"IndexerFolderPath\":\"legacy\"}}")!;
        Assert.Multiple(() =>
        {
            Assert.That(settings.ToolsSettings.ImageEditorOptions, Is.Not.Null);
            Assert.That(settings.ToolsSettings.BackgroundRemoverOptions, Is.Not.Null);
            Assert.That(settings.ToolsSettings.IndexerFolderPath, Is.EqualTo("legacy"));
            Assert.That(settings.ToolsSettings.ImageEditorOptions.QuickCrop, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Dispatcher_OpensEachSharedTool_AndBackgroundRemoverHandlesMissingModels()
    {
        Window? opened = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => opened = window);
        var settings = AddWorkflow("tools");
        foreach (var job in new[] { WorkflowType.BackgroundRemover, WorkflowType.ImageComparer, WorkflowType.IconConverter })
        {
            opened = null;
            Assert.That(ToolWorkflowDispatcher.TryDispatch(job, null, settings, new FakeDesktopTaskManager(), out var task), Is.True);
            Assert.That(opened, Is.Not.Null);
            try
            {
                Assert.That(opened!.IsVisible, Is.True);
                if (job == WorkflowType.BackgroundRemover)
                {
                    Assert.That(opened, Is.TypeOf<BackgroundRemoverWindow>());
                    var vm = (BackgroundRemoverViewModel)opened.DataContext!;
                    Assert.That(vm.ModelsFolder, Is.EqualTo(PathsManager.ModelsFolder));
                    Assert.That(vm.CanRemoveBackground, Is.False);
                    Assert.That(vm.IsGuideHighlighted, Is.True);
                    Assert.That(vm.AvailableDevices.Contains(BackgroundRemovalDevice.GPU), Is.EqualTo(OperatingSystem.IsWindows()));
                    vm.SelectedDevice = BackgroundRemovalDevice.CPU;
                    Assert.That(settings.ToolsSettings.BackgroundRemoverOptions.SelectedDevice, Is.EqualTo(BackgroundRemovalDevice.CPU));
                }
                else
                {
                    Assert.That(opened.GetType(), Is.EqualTo(job == WorkflowType.ImageComparer ? typeof(ImageComparerWindow) : typeof(IconConverterWindow)));
                }
                SavePreview(opened, job.ToString());
            }
            finally
            {
                opened!.Close();
                await task;
            }
        }
    }

    [AvaloniaTest]
    public async Task Beautifier_UsesTaskPreferences_RendersBackground_AndPreservesCallerImage()
    {
        var settings = AddWorkflow("beautifier");
        var options = settings.ToolsSettings.ImageEditorOptions;
        options.RememberWindowState = false;
        options.BackgroundType = "Color";
        options.BackgroundColorHex = "#FF008080";
        options.BackgroundSmartPadding = false;
        options.BackgroundMargin = 20;
        options.BackgroundPadding = 10;
        options.BackgroundRoundedCorner = 0;
        options.BackgroundShadowRadius = 0;
        HostEditorWindow? opened = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<HostEditorWindow>((window, _) => opened = window);
        using var image = new SKBitmap(80, 60);
        image.Erase(SKColors.Crimson);
        var service = new AvaloniaUIService(new FakeDesktopTaskManager());
        var task = service.ShowEditorSessionAsync(image, options, sourceFilePath: "sample.png", openBackgroundPanel: true);
        Dispatcher.UIThread.RunJobs();
        Assert.That(opened, Is.Not.Null);
        var vm = (MainViewModel)opened!.DataContext!;
        try
        {
            opened.UpdateLayout();
            Assert.Multiple(() =>
            {
                Assert.That(vm.Options, Is.SameAs(options));
                Assert.That(vm.IsSettingsPanelOpen, Is.True);
                Assert.That(vm.AreBackgroundEffectsActive, Is.True);
                Assert.That(vm.ShowFileMenu, Is.True);
                Assert.That(vm.UseContinueWorkflow, Is.False);
            });
            vm.BackgroundMargin = 25;
            SavePreview(opened, "Beautifier");
            vm.ContinueCommand.Execute(null);
            var result = await task;
            Assert.That(result, Is.Not.Null);
            using var rendered = result!.RenderedImage;
            using var source = result.SourceImage;
            Assert.Multiple(() =>
            {
                Assert.That(rendered.Width, Is.EqualTo(150));
                Assert.That(rendered.Height, Is.EqualTo(130));
                Assert.That(rendered.GetPixel(0, 0), Is.EqualTo(SKColors.Teal));
                Assert.That(rendered.GetPixel(75, 65), Is.EqualTo(SKColors.Crimson));
                Assert.That(image.Handle, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(source!.Width, Is.EqualTo(80));
            });
            var loaded = WorkflowsConfig.Load(SettingsManager.WorkflowsConfigFilePath, fallbackSupport: false);
            Assert.That(loaded.Hotkeys.Single(workflow => workflow.Id == "beautifier").TaskSettings.ToolsSettings.ImageEditorOptions.BackgroundMargin, Is.EqualTo(25));
        }
        finally
        {
            if (opened.IsVisible)
            {
                vm.IsDirty = false;
                vm.RequestClose(ignoreModal: true);
                await task;
            }
        }
    }

    [AvaloniaTest]
    public async Task Editor_CancelledCloseDoesNotFinishSession_AndCancelPersistsPreferences()
    {
        var options = ImageEditorOptionsStore.GetEditorOptions();
        options.IsWindowMaximized = false;
        options.WindowWidth = 900;
        options.WindowHeight = 640;
        HostEditorWindow? opened = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<HostEditorWindow>((window, _) => opened = window);
        using var image = new SKBitmap(30, 20);
        image.Erase(SKColors.Gold);
        var service = new AvaloniaUIService(new FakeDesktopTaskManager());
        var task = service.ShowEditorSessionAsync(image, taskMode: true);
        Dispatcher.UIThread.RunJobs();
        Assert.That(opened, Is.Not.Null);
        var vm = (MainViewModel)opened!.DataContext!;
        try
        {
            Assert.That(opened.Width, Is.EqualTo(900));
            Assert.That(vm.ShowFileMenu, Is.False);
            Assert.That(vm.UseContinueWorkflow, Is.True);
            Assert.That(vm.IsSettingsPanelOpen, Is.False);
            vm.IsDirty = true;
            opened.Close();
            Assert.That(vm.IsModalOpen, Is.True);
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(opened.IsVisible, Is.True);
            vm.CloseModalCommand.Execute(null);
            options.Thickness = 18;
            vm.IsDirty = false;
            vm.RequestClose();
            Assert.That(await task, Is.Null);
            Assert.That(image.GetPixel(0, 0), Is.EqualTo(SKColors.Gold));
            var loaded = WorkflowsConfig.Load(SettingsManager.WorkflowsConfigFilePath, fallbackSupport: false);
            Assert.That(loaded.DefaultTaskSettings.ToolsSettings.ImageEditorOptions.Thickness, Is.EqualTo(18));
        }
        finally
        {
            if (opened.IsVisible)
            {
                vm.IsDirty = false;
                vm.RequestClose(ignoreModal: true);
                await task;
            }
        }
    }

    [AvaloniaTest]
    public async Task StandaloneEditor_ClosedWithoutContinue_ReturnsAnnotationsForHistory()
    {
        var options = ImageEditorOptionsStore.GetEditorOptions();
        options.RememberWindowState = false;
        HostEditorWindow? opened = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<HostEditorWindow>((window, _) => opened = window);
        using var image = new SKBitmap(40, 30);
        image.Erase(SKColors.White);
        var annotation = new RectangleAnnotation { StartPoint = new SKPoint(2, 2), EndPoint = new SKPoint(20, 15) };
        var service = new AvaloniaUIService(new FakeDesktopTaskManager());

        // The History annotation flow: a standalone session with restored annotations.
        var task = service.ShowEditorSessionAsync(image, sourceFilePath: "history.png",
            annotations: [annotation], restoredAnnotations: true);
        Dispatcher.UIThread.RunJobs();
        Assert.That(opened, Is.Not.Null);
        var vm = (MainViewModel)opened!.DataContext!;
        vm.IsDirty = false;
        opened.Close();

        var result = await task;
        Assert.That(result, Is.Not.Null, "History needs the session to save the annotation sidecar.");
        using var rendered = result!.RenderedImage;
        using var source = result.SourceImage;
        Assert.Multiple(() =>
        {
            Assert.That(result.TaskResult, Is.EqualTo(MainViewModel.EditorTaskResult.Cancel));
            Assert.That(result.Annotations, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ImageFileLaunch_ForwardsOptionsAndBeautifierMode_AndDisposesImages()
    {
        var service = new RecordingUIService();
        var taskManager = new RecordingTaskManager();
        PlatformServices.RegisterUIService(service);
        var settings = AddWorkflow("file");
        string path = Path.Combine(_directory, "image.png");
        using (var bitmap = new SKBitmap(4, 3))
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(path))
        {
            data.SaveTo(stream);
        }

        Assert.That(await ImageEditingToolService.OpenImageFileAsync(path, settings, true, taskManager), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(service.Options, Is.SameAs(settings.ToolsSettings.ImageEditorOptions));
            Assert.That(service.BackgroundPanel, Is.True);
            Assert.That(service.Path, Is.EqualTo(path));
            Assert.That(service.Input!.Handle, Is.EqualTo(IntPtr.Zero));
            Assert.That(service.Output!.Handle, Is.EqualTo(IntPtr.Zero));
            Assert.That(taskManager.Calls, Is.EqualTo(1));
            Assert.That(taskManager.Settings, Is.Not.SameAs(settings));
            Assert.That(taskManager.Settings!.AfterCaptureJob, Is.EqualTo(settings.AfterCaptureJob));
            Assert.That(taskManager.Settings.WorkflowId, Is.EqualTo(settings.WorkflowId));
        });
        service.Cancel = true;
        Assert.That(await ImageEditingToolService.OpenImageFileAsync(path, settings, true, taskManager), Is.True);
        Assert.That(taskManager.Calls, Is.EqualTo(1), "Cancel must not run after-capture tasks.");
        Assert.That(service.Output!.Handle, Is.EqualTo(IntPtr.Zero));
        Assert.That(service.Source!.Handle, Is.EqualTo(IntPtr.Zero));
        Assert.That(await ImageEditingToolService.OpenImageFileAsync(Path.Combine(_directory, "missing.png"), settings, false, taskManager), Is.False);
        File.WriteAllText(path, "not an image");
        Assert.That(await ImageEditingToolService.OpenImageFileAsync(path, settings, false, taskManager), Is.False);
        Assert.That(service.Launches, Is.EqualTo(2));
    }

    [Test]
    public async Task CaptureAnnotation_PassesSavedTaskOptions()
    {
        var settings = AddWorkflow("capture");
        var service = new RecordingUIService();
        PlatformServices.RegisterUIService(service);
        var executionCopy = new TaskSettings { WorkflowId = "capture", AfterCaptureJob = AfterCaptureTasks.AnnotateMedia };
        using var image = new SKBitmap(10, 10);
        var info = new TaskInfo(executionCopy) { Metadata = new TaskMetadata(image) };

        Assert.That(await new CaptureJobProcessor().ProcessAsync(info, CancellationToken.None), Is.True);
        Assert.That(service.Options, Is.SameAs(settings.ToolsSettings.ImageEditorOptions));
        Assert.That(service.TaskMode, Is.True);
    }

    [Test]
    public void BackgroundRemoval_NativeCpuRuntimeLoads()
    {
        using var options = new SessionOptions();
        Assert.DoesNotThrow(() => options.AppendExecutionProvider_CPU());
    }

    [AvaloniaTest]
    public void TaskSettings_EditorControlsEditTheLocalTask_AndFitThePanel()
    {
        var saved = AddWorkflow("settings");
        var edited = new TaskSettings { WorkflowId = "settings" };
        var vm = new XerahS.UI.ViewModels.TaskSettingsViewModel(edited, new FakeViewDialogService());
        var panel = new XerahS.UI.Views.TaskSettingsPanel { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = panel };
        try
        {
            window.Show();
            var tabs = panel.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, "Image Editor"));
            window.UpdateLayout();
            var quickCrop = panel.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Quick crop"));
            quickCrop.IsChecked = false;
            Assert.That(edited.ToolsSettings.ImageEditorOptions.QuickCrop, Is.False);
            Assert.That(saved.ToolsSettings.ImageEditorOptions.QuickCrop, Is.True, "Editing settings must not bypass workflow dialog Save/Cancel.");
            SavePreview(window, "TaskSettings");
            window.Width = 640;
            window.Height = 480;
            window.UpdateLayout();
            Assert.That(quickCrop.Bounds.Width, Is.GreaterThan(0));
            SavePreview(window, "TaskSettingsCompact");
        }
        finally
        {
            window.Close();
        }
    }

    private static void SavePreview(Window window, string name)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        string directory = Path.Combine(Path.GetTempPath(), "xerahs-image-editing-previews");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private static TaskSettings AddWorkflow(string id)
    {
        var settings = new TaskSettings { WorkflowId = id };
        SettingsManager.WorkflowsConfig.Hotkeys.Add(new WorkflowSettings { Id = id, TaskSettings = settings });
        return settings;
    }

    private sealed class RecordingUIService : XerahS.CLI.Services.HeadlessUIService, IUIService
    {
        public ImageEditorOptions? Options { get; private set; }
        public bool BackgroundPanel { get; private set; }
        public bool TaskMode { get; private set; }
        public string? Path { get; private set; }
        public SKBitmap? Input { get; private set; }
        public SKBitmap? Output { get; private set; }
        public SKBitmap? Source { get; private set; }
        public int Launches { get; private set; }
        public bool Cancel { get; set; }

        public Task<ImageEditorSessionResult?> ShowEditorSessionAsync(SKBitmap image, ImageEditorOptions editorOptions,
            string? sourceFilePath = null, bool taskMode = false, IReadOnlyList<Annotation>? annotations = null,
            bool restoredAnnotations = false, bool openBackgroundPanel = false)
        {
            Options = editorOptions;
            TaskMode = taskMode;
            if (taskMode)
            {
                return Task.FromResult<ImageEditorSessionResult?>(null);
            }

            Launches++;
            BackgroundPanel = openBackgroundPanel;
            Path = sourceFilePath;
            Input = image;
            Output = image.Copy();
            Source = image.Copy();
            // A standalone editor closed with Exit or Cancel still returns its session, marked Cancel.
            return Task.FromResult<ImageEditorSessionResult?>(new ImageEditorSessionResult(Output, Source, [])
            {
                TaskResult = Cancel ? MainViewModel.EditorTaskResult.Cancel : MainViewModel.EditorTaskResult.Continue
            });
        }
    }

    private sealed class RecordingTaskManager : IDesktopTaskManager
    {
        public int Calls { get; private set; }
        public TaskSettings? Settings { get; private set; }
        public event EventHandler<XerahS.Core.Tasks.WorkerTask>? TaskCompleted { add { } remove { } }
        public event EventHandler<XerahS.Core.Tasks.WorkerTask>? TaskStarted { add { } remove { } }
        public IEnumerable<XerahS.Core.Tasks.WorkerTask> Tasks => [];
        public Task StartTask(TaskSettings? taskSettings, SKBitmap? inputImage = null)
        {
            Calls++;
            Settings = taskSettings;
            Assert.That(inputImage, Is.Not.Null);
            inputImage.Dispose();
            return Task.CompletedTask;
        }
        public Task StartFileTask(TaskSettings? taskSettings, string filePath) => throw new NotSupportedException();
        public Task StartImageUploadTask(TaskSettings? taskSettings, SKBitmap image) => throw new NotSupportedException();
        public Task StartTextTask(TaskSettings? taskSettings, string text) => throw new NotSupportedException();
        public void StopAllTasks() { }
    }
}
