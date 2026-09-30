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
using Avalonia.Automation;
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
using XerahS.UI.Views;
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
    [TestCase(WorkflowType.ImageEffects)]
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
    public void Options_ComeFromDefaultTaskSettings_UnlessTheWorkflowOverridesTools()
    {
        var inheriting = AddWorkflow("inheriting");
        var overriding = AddWorkflow("overriding", overrideTools: true);
        var defaults = SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions;

        Assert.Multiple(() =>
        {
            Assert.That(inheriting.UseDefaultToolsSettings, Is.True, "ShareX default");
            Assert.That(ImageEditorOptionsStore.GetEditorOptions(inheriting), Is.SameAs(defaults));
            Assert.That(ImageEditorOptionsStore.GetEditorOptions(), Is.SameAs(defaults));
            Assert.That(ImageEditorOptionsStore.GetEditorOptions(overriding), Is.SameAs(overriding.ToolsSettings.ImageEditorOptions));
            Assert.That(ImageEditorOptionsStore.GetEditorOptions(overriding), Is.Not.SameAs(defaults));
            Assert.That(ImageEditorOptionsStore.GetEditorOptions(new TaskSettings()), Is.SameAs(defaults), "Ad hoc tasks use the defaults.");
        });
    }

    [Test]
    public void ExecutionCopies_FollowTheSavedWorkflowsOverride()
    {
        var overriding = AddWorkflow("overriding", overrideTools: true);
        AddWorkflow("inheriting");

        // Copies carry a stale flag; the saved workflow decides.
        var overridingCopy = new TaskSettings { WorkflowId = "overriding", UseDefaultToolsSettings = true };
        var inheritingCopy = new TaskSettings { WorkflowId = "inheriting", UseDefaultToolsSettings = false };
        var removedCopy = new TaskSettings { WorkflowId = "removed", UseDefaultToolsSettings = false };
        ImageEditorOptionsStore.GetEditorOptions(overridingCopy).Thickness = 17;

        Assert.Multiple(() =>
        {
            Assert.That(overriding.ToolsSettings.ImageEditorOptions.Thickness, Is.EqualTo(17));
            Assert.That(SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions.Thickness, Is.EqualTo(4));
            Assert.That(ImageEditorOptionsStore.GetEditorOptions(inheritingCopy),
                Is.SameAs(SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions));
            Assert.That(ImageEditorOptionsStore.GetEditorOptions(removedCopy), Is.SameAs(removedCopy.ToolsSettings.ImageEditorOptions));
        });
    }

    [Test]
    public async Task Preferences_RoundTripThroughWorkflowStorage()
    {
        var settings = AddWorkflow("saved", overrideTools: true);
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
            Assert.That(loaded.Hotkeys.Single(workflow => workflow.Id == "saved").TaskSettings.UseDefaultToolsSettings, Is.False);
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
            Assert.That(settings.UseDefaultToolsSettings, Is.True, "Saved tasks without the flag use the default tools settings.");
        });
    }

    [AvaloniaTest]
    public async Task Dispatcher_OpensEachSharedTool_AndBackgroundRemoverHandlesMissingModels()
    {
        Window? opened = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => opened = window);
        var settings = AddWorkflow("tools", overrideTools: true);
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
    public void ImageEffectsWindow_EditsTheSavedWorkflowPreset_AndAppliesItToTheImage()
    {
        UiViewModelFactoryAccessor.Configure(new FakeUiViewModelFactory());
        var saved = AddWorkflow("effects");
        saved.ImageSettings.ImageEffectsPreset = new ImageEffectPreset { Name = "Flip" };
        saved.AfterCaptureJob = AfterCaptureTasks.AddImageEffects | AfterCaptureTasks.UploadImageToHost;
        // Left half red, right half blue, so a horizontal flip is visible.
        using var image = new SKBitmap(80, 60);
        using (var canvas = new SKCanvas(image))
        {
            canvas.Clear(SKColors.Red);
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(40, 0, 40, 60, blue);
        }
        var manager = new RecordingTaskManager();
        var executionCopy = new TaskSettings { WorkflowId = "effects", AfterCaptureJob = saved.AfterCaptureJob };

        var window = ImageEditingToolService.CreateImageEffectsWindow(image, "effects.png", executionCopy, manager);
        try
        {
            window.Show();
            var vm = window.ViewModel;
            Assert.That(vm.TryAddFlipHorizontalEffect(), Is.True);
            window.UpdateLayout();
            using var result = window.CreateResult();
            Assert.Multiple(() =>
            {
                Assert.That(saved.ImageSettings.ImageEffectsPreset.Effects, Has.Count.EqualTo(1), "The preset belongs to the saved workflow.");
                Assert.That(executionCopy.ImageSettings.ImageEffectsPreset.Effects, Is.Empty);
                Assert.That(vm.PreviewBitmap!.PixelSize.Width, Is.EqualTo(80), "The preview shows the chosen image.");
                Assert.That(result!.GetPixel(10, 30), Is.EqualTo(SKColors.Blue));
                Assert.That(image.GetPixel(10, 30), Is.EqualTo(SKColors.Red), "The caller's image is not changed.");
                Assert.That(window.Title, Does.Contain("effects.png"));
            });
            SavePreview(window, "ImageEffectsTool");

            ImageEditingToolService.UploadImageEffectsResultAsync(result!, executionCopy, manager).GetAwaiter().GetResult();
            Assert.Multiple(() =>
            {
                Assert.That(manager.Calls, Is.EqualTo(1));
                Assert.That(manager.Settings!.AfterCaptureJob, Is.EqualTo(AfterCaptureTasks.UploadImageToHost),
                    "The preset is already applied, so Add image effects is skipped.");
                Assert.That(manager.Settings.WorkflowId, Is.EqualTo("effects"));
            });
        }
        finally
        {
            window.Close();
            UiViewModelFactoryAccessor.Reset();
        }
    }

    [AvaloniaTest]
    public async Task ImageEffectsJob_OpensThePresetWindow_AndReturnsWhenItCloses()
    {
        UiViewModelFactoryAccessor.Configure(new FakeUiViewModelFactory());
        ImageEffectsToolWindow? opened = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<ImageEffectsToolWindow>((window, _) => opened = window);
        string path = Path.Combine(_directory, "effects.png");
        using (var bitmap = new SKBitmap(4, 3))
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(path)) data.SaveTo(stream);
        try
        {
            Assert.That(await ImageEditingToolService.OpenImageEffectsAsync(Path.Combine(_directory, "missing.png"), null, new RecordingTaskManager()), Is.False);

            var task = ImageEditingToolService.OpenImageEffectsAsync(path, null, new RecordingTaskManager());
            Dispatcher.UIThread.RunJobs();
            Assert.That(opened, Is.Not.Null);
            Assert.That(task.IsCompleted, Is.False);
            opened!.Close();
            Assert.That(await task, Is.True);
        }
        finally
        {
            UiViewModelFactoryAccessor.Reset();
        }
    }

    [AvaloniaTest]
    public async Task Beautifier_UsesTaskPreferences_RendersBackground_AndPreservesCallerImage()
    {
        var settings = AddWorkflow("beautifier", overrideTools: true);
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
        var settings = AddWorkflow("file", overrideTools: true);
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task CaptureAnnotation_PassesTheOptionsTheWorkflowUses(bool overrideTools)
    {
        var settings = AddWorkflow("capture", overrideTools);
        var service = new RecordingUIService();
        PlatformServices.RegisterUIService(service);
        var executionCopy = new TaskSettings { WorkflowId = "capture", AfterCaptureJob = AfterCaptureTasks.AnnotateMedia };
        using var image = new SKBitmap(10, 10);
        var info = new TaskInfo(executionCopy) { Metadata = new TaskMetadata(image) };

        Assert.That(await new CaptureJobProcessor().ProcessAsync(info, CancellationToken.None), Is.True);
        var expected = overrideTools
            ? settings.ToolsSettings.ImageEditorOptions
            : SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions;
        Assert.That(service.Options, Is.SameAs(expected));
        Assert.That(service.TaskMode, Is.True);
    }

    [Test]
    public void BackgroundRemoval_NativeCpuRuntimeLoads()
    {
        using var options = new SessionOptions();
        Assert.DoesNotThrow(() => options.AppendExecutionProvider_CPU());
    }

    [AvaloniaTest]
    public void WorkflowToolsTab_IsDisabledUntilOverride_AndEditsTheLocalTask()
    {
        var saved = AddWorkflow("settings");
        var edited = new TaskSettings { WorkflowId = "settings" };
        SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions.QuickCrop = false;
        var vm = new XerahS.UI.ViewModels.TaskSettingsViewModel(edited, new FakeViewDialogService());
        var panel = new XerahS.UI.Views.TaskSettingsPanel { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = panel };
        try
        {
            window.Show();
            SelectTab(panel, "Tools");
            var overrideBox = FindCheckBox(panel, "Override tools settings");
            var quickCrop = FindCheckBox(panel, "Quick crop");
            Assert.Multiple(() =>
            {
                Assert.That(overrideBox.IsVisible, Is.True);
                Assert.That(overrideBox.IsChecked, Is.False);
                Assert.That(quickCrop.IsEffectivelyEnabled, Is.False, "Inherited tools settings are read-only.");
                Assert.That(quickCrop.IsChecked, Is.False, "While inheriting, the controls show the defaults the workflow uses.");
            });
            SavePreview(window, "WorkflowToolsInherited");

            overrideBox.IsChecked = true;
            window.UpdateLayout();
            Assert.That(quickCrop.IsChecked, Is.True, "With the override on, the controls show the workflow's own settings.");
            quickCrop.IsChecked = false;
            Assert.Multiple(() =>
            {
                Assert.That(edited.UseDefaultToolsSettings, Is.False);
                Assert.That(quickCrop.IsEffectivelyEnabled, Is.True);
                Assert.That(edited.ToolsSettings.ImageEditorOptions.QuickCrop, Is.False);
                Assert.That(saved.ToolsSettings.ImageEditorOptions.QuickCrop, Is.True, "Editing settings must not bypass workflow dialog Save/Cancel.");
                Assert.That(panel.GetVisualDescendants().OfType<TabItem>().Where(item => item.IsVisible).Select(item => item.Header),
                    Has.None.EqualTo("Image").And.None.EqualTo("Video").And.None.EqualTo("Index Folder"));
            });
            SavePreview(window, "WorkflowToolsOverride");
            window.Width = 640;
            window.Height = 480;
            window.UpdateLayout();
            Assert.That(quickCrop.Bounds.Width, Is.GreaterThan(0));
            SavePreview(window, "WorkflowToolsCompact");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void DefaultTaskSettingsPage_EditsTheDefaults_WithoutOverrideCheckbox()
    {
        var vm = new FakeUiViewModelFactory().CreateDefaultTaskSettingsViewModel();
        var panel = new XerahS.UI.Views.TaskSettingsPanel { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = panel };
        try
        {
            window.Show();
            var visibleTabs = panel.GetVisualDescendants().OfType<TabItem>().Where(item => item.IsVisible).Select(item => item.Header).ToList();
            SelectTab(panel, "Tools");
            var quickCrop = FindCheckBox(panel, "Quick crop");
            var screenColorFormat = panel.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Screen Color Picker Format");
            Assert.Multiple(() =>
            {
                Assert.That(vm.Model, Is.SameAs(SettingsManager.DefaultTaskSettings));
                Assert.That(visibleTabs, Does.Contain("Image").And.Contain("Video").And.Contain("Index Folder").And.Contain("Tools"));
                Assert.That(FindCheckBox(panel, "Override tools settings").IsVisible, Is.False);
                Assert.That(quickCrop.IsEffectivelyEnabled, Is.True);
            });

            quickCrop.IsChecked = false;
            screenColorFormat.Text = "$r, $g, $b";
            Assert.Multiple(() =>
            {
                Assert.That(SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions.QuickCrop, Is.False);
                Assert.That(SettingsManager.DefaultTaskSettings.ToolsSettings.ScreenColorPickerFormat, Is.EqualTo("$r, $g, $b"));
                Assert.That(ImageEditorOptionsStore.GetEditorOptions(AddWorkflow("inheriting")).QuickCrop, Is.False);
            });
            SavePreview(window, "DefaultTaskSettingsTools");
            SelectTab(panel, "Image");
            SavePreview(window, "DefaultTaskSettingsImage");
            SelectTab(panel, "Video");
            SavePreview(window, "DefaultTaskSettingsVideo");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task TaskModeEditor_ClosingAnUnchangedCapture_DoesNotAskToSave()
    {
        var options = ImageEditorOptionsStore.GetEditorOptions();
        options.RememberWindowState = false;
        Assert.That(options.ShowExitConfirmation, Is.True);
        HostEditorWindow? opened = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<HostEditorWindow>((window, _) => opened = window);
        using var image = new SKBitmap(30, 20);
        var service = new AvaloniaUIService(new FakeDesktopTaskManager());

        var task = service.ShowEditorSessionAsync(image, taskMode: true);
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)opened!.DataContext!;
        Assert.That(vm.IsDirty, Is.False, "Loading the capture is not an edit.");
        opened.Close();

        Assert.That(vm.IsModalOpen, Is.False);
        Assert.That(await task, Is.Null);
    }

    [Test]
    public async Task PersistingToolPreferences_SavesWithoutRaisingSettingsChanged()
    {
        int raised = 0;
        EventHandler handler = (_, _) => raised++;
        SettingsManager.SettingsChanged += handler;
        try
        {
            SettingsManager.DefaultTaskSettings.ToolsSettings.ImageEditorOptions.Thickness = 9;
            await ImageEditorOptionsStore.PersistAsync();
            await SettingsManager.SaveWorkflowsConfigAsync();
        }
        finally
        {
            SettingsManager.SettingsChanged -= handler;
        }

        var loaded = WorkflowsConfig.Load(SettingsManager.WorkflowsConfigFilePath, fallbackSupport: false);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.DefaultTaskSettings.ToolsSettings.ImageEditorOptions.Thickness, Is.EqualTo(9));
            Assert.That(raised, Is.EqualTo(1), "Only the ordinary workflows save raises SettingsChanged.");
        });
    }

    [Test]
    public void IndexFolder_ShowsInheritedSettings_UntilTheWorkflowOverridesThem()
    {
        SettingsManager.DefaultTaskSettings.ToolsSettings.IndexerFolderPath = "/default/folder";
        var inheriting = AddWorkflow("index");
        inheriting.ToolsSettings.IndexerFolderPath = "/own/folder";
        var overriding = AddWorkflow("index-override", overrideTools: true);
        overriding.ToolsSettings.IndexerFolderPath = "/override/folder";

        var tool = new XerahS.UI.ViewModels.IndexFolderViewModel(inheriting, false, new FakeViewDialogService(), new FakeDesktopTaskManager());
        var overridingTool = new XerahS.UI.ViewModels.IndexFolderViewModel(overriding, false, new FakeViewDialogService(), new FakeDesktopTaskManager());
        // The workflow editor edits a copy of the workflow.
        var editedCopy = new TaskSettings { WorkflowId = "index" };
        editedCopy.ToolsSettings.IndexerFolderPath = "/own/folder";
        var editor = new XerahS.UI.ViewModels.IndexFolderViewModel(editedCopy, true, new FakeViewDialogService(), new FakeDesktopTaskManager());
        string inheritedEditorPath = editor.FolderPath;
        editedCopy.UseDefaultToolsSettings = false;
        editor.ReloadToolsSettings();

        Assert.Multiple(() =>
        {
            Assert.That(tool.FolderPath, Is.EqualTo("/default/folder"));
            Assert.That(overridingTool.FolderPath, Is.EqualTo("/override/folder"));
            Assert.That(inheritedEditorPath, Is.EqualTo("/default/folder"), "While inheriting, the editor shows the defaults.");
            Assert.That(editor.FolderPath, Is.EqualTo("/own/folder"));
            Assert.That(SettingsManager.DefaultTaskSettings.ToolsSettings.IndexerFolderPath, Is.EqualTo("/default/folder"));
        });
    }

    private static void SelectTab(Control panel, string header)
    {
        var tabs = panel.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, header));
        (TopLevel.GetTopLevel(panel) as Window)?.UpdateLayout();
    }

    private static CheckBox FindCheckBox(Control panel, string content) =>
        panel.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, content));

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

    private static TaskSettings AddWorkflow(string id, bool overrideTools = false)
    {
        var settings = new TaskSettings { WorkflowId = id, UseDefaultToolsSettings = !overrideTools };
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
