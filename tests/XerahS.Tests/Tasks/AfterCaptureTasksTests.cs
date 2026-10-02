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
using NUnit.Framework;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Hosting;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Core.Tasks.Processors;
using XerahS.Platform.Abstractions;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Tasks;

[TestFixture, NonParallelizable]
public class AfterCaptureTasksTests
{
    private string _directory = null!;
    private object? _previousFolder, _previousOverride;
    private static readonly FieldInfo PersonalFolder = typeof(PathsManager).GetField("_personalFolder", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FolderOverride = typeof(PathsManager).GetField("_personalFolderOverrideSet", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly List<string> _instances = [];
    private const string ProviderId = "after-capture-test-provider";
    private static Action<Stream, string>? _onUpload;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-after-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousFolder = PersonalFolder.GetValue(null);
        _previousOverride = FolderOverride.GetValue(null);
        PathsManager.PersonalFolder = _directory;
        PlatformServices.Reset();
        ProviderCatalog.RegisterProvider(new TestProvider());
    }

    [TearDown]
    public void TearDown()
    {
        CaptureJobProcessor.ShowQuickTaskMenuCallback = null;
        CaptureJobProcessor.SaveImageWithDialogCallback = null;
        UploadJobProcessor.ShowBeforeUploadCallback = null;
        _onUpload = null;
        foreach (string id in _instances) InstanceManager.Instance.RemoveInstance(id);
        _instances.Clear();
        PlatformServices.Reset();
        PersonalFolder.SetValue(null, _previousFolder);
        FolderOverride.SetValue(null, _previousOverride);
        Directory.Delete(_directory, recursive: true);
    }

    private TaskSettings Settings(AfterCaptureTasks tasks) => new()
    {
        Job = WorkflowType.PrintScreen, AfterCaptureJob = tasks, AfterUploadJob = AfterUploadTasks.None,
        OverrideScreenshotsFolder = true, ScreenshotsFolder = _directory,
        ImageSettings = new() { FileExistAction = FileExistAction.UniqueName }
    };

    [Test]
    public async Task QuickMenuCancel_StopsBeforeSaving()
    {
        var settings = Settings(AfterCaptureTasks.ShowQuickTaskMenu | AfterCaptureTasks.SaveImageToFile);
        CaptureJobProcessor.ShowQuickTaskMenuCallback = (_, _) => Task.FromResult(QuickTaskMenuResult.Cancel);
        using var image = new SKBitmap(20, 10);
        var info = new TaskInfo(settings) { Metadata = new(image) };
        Assert.That(await new CaptureJobProcessor().ProcessAsync(info, default), Is.False);
        Assert.That(Directory.GetFiles(_directory, "*.png", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task QuickPreset_AppliesForThisRun_WithoutReopeningMenuOrChangingSavedWorkflow()
    {
        var saved = Settings(AfterCaptureTasks.ShowQuickTaskMenu | AfterCaptureTasks.SaveImageToFile);
        var settings = TaskSettings.GetSafeTaskSettings(saved, saved);
        int menus = 0;
        CaptureJobProcessor.ShowQuickTaskMenuCallback = (execution, _) =>
        {
            menus++;
            execution.AfterCaptureJob = AfterCaptureTasks.ShowQuickTaskMenu | AfterCaptureTasks.SaveImageToFileWithDialog;
            execution.AfterUploadJob = AfterUploadTasks.None;
            return Task.FromResult(QuickTaskMenuResult.Preset);
        };
        CaptureJobProcessor.SaveImageWithDialogCallback = (_, _) => Task.FromResult<string?>(null);
        using var image = new SKBitmap(20, 10);
        Assert.That(await new CaptureJobProcessor().ProcessAsync(new(settings) { Metadata = new(image) }, default), Is.True);
        Assert.That(menus, Is.EqualTo(1));
        Assert.That(saved.AfterCaptureJob, Is.EqualTo(AfterCaptureTasks.ShowQuickTaskMenu | AfterCaptureTasks.SaveImageToFile));
        Assert.That(Directory.GetFiles(_directory, "*.png", SearchOption.AllDirectories), Is.Empty);
    }

    [TestCase(QuickTaskMenuResult.Continue, true)]
    [TestCase(QuickTaskMenuResult.Preset, false)]
    public async Task ClipboardImage_SaveOnlyPresetDoesNotUpload(QuickTaskMenuResult selection, bool shouldUpload)
    {
        var settings = Settings(AfterCaptureTasks.ShowQuickTaskMenu);
        CaptureJobProcessor.ShowQuickTaskMenuCallback = (execution, _) =>
        {
            if (selection == QuickTaskMenuResult.Preset) execution.AfterCaptureJob = AfterCaptureTasks.SaveImageToFile;
            return Task.FromResult(selection);
        };
        using var image = new SKBitmap(20, 10);
        var info = new TaskInfo(settings) { Job = TaskJob.DataUpload, DataType = EDataType.Image, Metadata = new(image) };
        Assert.That(await new CaptureJobProcessor().ProcessAsync(info, default), Is.True);
        Assert.That(info.IsUploadJob, Is.EqualTo(shouldUpload));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClipboardImage_RunsAfterCaptureTasksOnlyWhenProcessingIsOn(bool process)
    {
        const AfterCaptureTasks tasks = AfterCaptureTasks.ShowQuickTaskMenu | AfterCaptureTasks.SaveImageToFile;
        var settings = Settings(tasks);
        settings.Job = WorkflowType.ClipboardUpload;
        settings.AdvancedSettings.ProcessImagesDuringClipboardUpload = process;
        PlatformServices.Clipboard = new TestClipboard { Image = new SKBitmap(20, 10) };
        using var worker = WorkerTask.Create(settings);
        Assert.That(worker.TryLoadClipboardContent(worker.Info.TaskSettings, worker.Info.Metadata, out _), Is.True);
        Assert.Multiple(() =>
        {
            // As in ShareX: off uploads the image only; on runs it as an image task with its own after capture tasks.
            Assert.That(worker.Info.TaskSettings.AfterCaptureJob, Is.EqualTo(process ? tasks : AfterCaptureTasks.None));
            Assert.That(worker.Info.Job, Is.EqualTo(process ? TaskJob.Job : TaskJob.DataUpload));
            Assert.That(worker.Info.IsUploadJob, Is.EqualTo(!process), "With processing on, only 'Upload image to host' uploads.");
        });
    }

    [Test]
    public async Task SaveDialog_UsesChosenPathForThumbnailAndFolderClipboard()
    {
        var settings = Settings(AfterCaptureTasks.SaveImageToFile | AfterCaptureTasks.SaveImageToFileWithDialog |
            AfterCaptureTasks.SaveThumbnailImageToFile | AfterCaptureTasks.CopyFolderPathToClipboard);
        settings.ImageSettings.ThumbnailWidth = 10;
        string selectedFolder = Path.Combine(_directory, "chosen folder");
        string selected = Path.Combine(selectedFolder, "chosen.png");
        string? automatic = null;
        CaptureJobProcessor.SaveImageWithDialogCallback = (info, _) =>
        {
            automatic = info.FilePath;
            TaskHelpers.SaveImageToPath(info.Metadata.Image!, selected, settings);
            return Task.FromResult<string?>(selected);
        };
        var clipboard = new TestClipboard();
        PlatformServices.Clipboard = clipboard;
        using var image = new SKBitmap(20, 10);
        var info = new TaskInfo(settings) { Metadata = new(image) };
        Assert.That(await new CaptureJobProcessor().ProcessAsync(info, default), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(automatic), Is.True);
            Assert.That(info.FilePath, Is.EqualTo(selected));
            Assert.That(info.ThumbnailFilePath, Is.EqualTo(Path.Combine(selectedFolder, "chosen-thumbnail.jpg")));
            Assert.That(clipboard.Text, Is.EqualTo(selectedFolder));
        });
        using var thumbnail = SKBitmap.Decode(info.ThumbnailFilePath);
        Assert.That((thumbnail.Width, thumbnail.Height), Is.EqualTo((10, 5)));
        Assert.That(thumbnail.GetPixel(0, 0), Is.EqualTo(SKColors.White));
    }

    [TestCase(40, 0, false, 40, 20)]
    [TestCase(0, 40, false, 80, 40)]
    [TestCase(40, 40, false, 40, 40)]
    [TestCase(40, 0, true, 0, 0)]
    [TestCase(10, 10, true, 0, 0)]
    [TestCase(10, 0, true, 10, 5)]
    public async Task Thumbnail_UsesShareXResizeAndOnlyIfLargerRules(int width, int height, bool checkSize, int expectedWidth, int expectedHeight)
    {
        var settings = Settings(AfterCaptureTasks.None);
        settings.ImageSettings.ThumbnailWidth = width;
        settings.ImageSettings.ThumbnailHeight = height;
        settings.ImageSettings.ThumbnailCheckSize = checkSize;
        using var image = new SKBitmap(20, 10);
        var info = new TaskInfo(settings) { FilePath = Path.Combine(_directory, "image.png"), Metadata = new(image) };
        string? thumbnail = await AfterCaptureFileTasks.SaveThumbnailAsync(info);
        if (expectedWidth == 0) Assert.That(thumbnail, Is.Null);
        else
        {
            using var decoded = SKBitmap.Decode(thumbnail);
            Assert.That((decoded.Width, decoded.Height), Is.EqualTo((expectedWidth, expectedHeight)));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Thumbnail_EmptySuffixCannotOverwriteTheCapture(bool captureAlreadySaved)
    {
        var settings = Settings(AfterCaptureTasks.None);
        settings.ImageSettings.ThumbnailName = "";
        settings.ImageSettings.ThumbnailWidth = 5;
        settings.ImageSettings.FileExistAction = FileExistAction.Overwrite;
        string main = Path.Combine(_directory, "capture.jpg");
        using var image = new SKBitmap(20, 10);
        if (captureAlreadySaved) ImageHelpers.SaveBitmap(image, main);
        byte[]? original = captureAlreadySaved ? await File.ReadAllBytesAsync(main) : null;
        var info = new TaskInfo(settings) { FilePath = captureAlreadySaved ? main : "", Metadata = new(image) };
        info.SetFileName("capture.jpg");
        string? thumbnail = await AfterCaptureFileTasks.SaveThumbnailAsync(info);
        Assert.That(thumbnail, Is.Not.EqualTo(main));
        if (captureAlreadySaved) Assert.That(await File.ReadAllBytesAsync(main), Is.EqualTo(original));
        else Assert.That(File.Exists(main), Is.False);
        using var result = SKBitmap.Decode(thumbnail);
        Assert.That(result.Width, Is.EqualTo(5));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Thumbnail_ExistingFileUsesConflictDialog(bool accept)
    {
        var settings = Settings(AfterCaptureTasks.None);
        settings.ImageSettings.FileExistAction = FileExistAction.Ask;
        string existing = Path.Combine(_directory, "capture-thumbnail.jpg");
        await File.WriteAllTextAsync(existing, "old thumbnail");
        string chosen = Path.Combine(_directory, "chosen-thumbnail.jpg");
        var ui = new ConflictUI(accept ? new FileConflictResolution(chosen, false) : null);
        PlatformServices.RegisterUIService(ui);
        using var image = new SKBitmap(20, 10);
        var info = new TaskInfo(settings) { FilePath = Path.Combine(_directory, "capture.png"), Metadata = new(image) };
        string? result = await AfterCaptureFileTasks.SaveThumbnailAsync(info);
        Assert.That(ui.RequestedPath, Is.EqualTo(existing));
        Assert.That(result, Is.EqualTo(accept ? chosen : null));
        Assert.That(await File.ReadAllTextAsync(existing), Is.EqualTo("old thumbnail"));
        Assert.That(File.Exists(chosen), Is.EqualTo(accept));
    }

    [Test]
    public void SaveImage_FailedCommitRemovesTemporaryFile()
    {
        string destination = Path.Combine(_directory, "folder.png");
        Directory.CreateDirectory(destination);
        using var image = new SKBitmap(20, 10);
        Assert.Throws<IOException>(() => TaskHelpers.SaveImageToPath(image, destination, Settings(AfterCaptureTasks.None)));
        Assert.That(Directory.Exists(destination), Is.True);
        Assert.That(Directory.GetFiles(_directory, ".xerahs-*.tmp"), Is.Empty);
    }

    [Test]
    public async Task SaveAs_AvifUsesTheConfiguredEncoderAndQuality()
    {
        var encoder = new TestImageEncoder();
        PlatformServices.RegisterImageEncoderService(encoder);
        var settings = Settings(AfterCaptureTasks.None);
        settings.ImageSettings.ImageFormat = XerahS.Services.Abstractions.EImageFormat.AVIF;
        settings.ImageSettings.ImageJPEGQuality = 73;
        string path = Path.Combine(_directory, "saved.avif");
        using var image = new SKBitmap(20, 10);
        await TaskHelpers.SaveImageToPathAsync(image, path, settings);
        Assert.That(encoder.Format, Is.EqualTo(XerahS.Services.Abstractions.EImageFormat.AVIF));
        Assert.That(encoder.Quality, Is.EqualTo(73));
        Assert.That(encoder.Path, Does.EndWith(".avif"));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("encoded avif"));
        Assert.That(File.Exists(encoder.Path), Is.False);
    }

    [Test]
    public void SaveAs_UnavailableTiffEncoderDoesNotWriteDisguisedPngOrOverwriteTheTarget()
    {
        var settings = Settings(AfterCaptureTasks.None);
        settings.ImageSettings.ImageFormat = XerahS.Services.Abstractions.EImageFormat.TIFF;
        string path = Path.Combine(_directory, "saved.tiff");
        File.WriteAllText(path, "original");
        using var image = new SKBitmap(20, 10);
        Assert.ThrowsAsync<NotSupportedException>(() => TaskHelpers.SaveImageToPathAsync(image, path, settings));
        Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
    }

    [Test]
    public async Task Beautify_OpensBackgroundPanelInTaskMode_AndCancelStopsSaving()
    {
        var editor = new TestEditor();
        PlatformServices.RegisterUIService(editor);
        using var image = new SKBitmap(20, 10);
        var info = new TaskInfo(Settings(AfterCaptureTasks.BeautifyImage | AfterCaptureTasks.SaveImageToFile)) { Metadata = new(image) };
        Assert.That(await new CaptureJobProcessor().ProcessAsync(info, default), Is.False);
        Assert.That(editor.BackgroundPanel && editor.TaskMode, Is.True);
        Assert.That(info.FilePath, Is.Empty);
    }

    [Test]
    public async Task Actions_RunBeforeUpload_AndDeleteOnlyAfterUploaderHasReadResult()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Uses Linux cp for the configured external action.");
        var settings = Settings(AfterCaptureTasks.SaveImageToFile | AfterCaptureTasks.PerformActions |
            AfterCaptureTasks.ShowBeforeUploadWindow | AfterCaptureTasks.UploadImageToHost | AfterCaptureTasks.DeleteFile);
        settings.ExternalPrograms.Add(new ExternalProgram("Copy", "/usr/bin/cp")
        {
            IsActive = true, Args = "\"$input\" \"$output\"", OutputExtension = "result.png", DeleteInputFile = true
        });
        settings.DestinationInstanceId = AddInstance();
        string? uploadPath = null;
        int confirmations = 0, uploads = 0;
        UploadJobProcessor.ShowBeforeUploadCallback = (info, _) =>
        {
            confirmations++;
            uploadPath = info.FilePath;
            Assert.That(uploadPath, Does.EndWith(".result.png"));
            Assert.That(File.Exists(uploadPath), Is.True);
            return Task.FromResult(true);
        };
        _onUpload = (stream, name) =>
        {
            uploads++;
            Assert.That(File.Exists(uploadPath), Is.True);
            using var bitmap = SKBitmap.Decode(stream);
            Assert.That((bitmap.Width, bitmap.Height), Is.EqualTo((20, 10)));
        };
        using var worker = WorkerTask.Create(settings, new SKBitmap(20, 10));
        await worker.StartAsync();
        Assert.Multiple(() =>
        {
            Assert.That(worker.Status, Is.EqualTo(XerahS.Core.TaskStatus.Completed));
            Assert.That(confirmations, Is.EqualTo(1));
            Assert.That(uploads, Is.EqualTo(1));
            Assert.That(File.Exists(uploadPath), Is.False);
            Assert.That(Directory.GetFiles(_directory, "*.png", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public async Task BeforeUploadCancel_SkipsTheUpload_WithoutFailingATextUpload()
    {
        var settings = Settings(AfterCaptureTasks.ShowBeforeUploadWindow);
        settings.DestinationInstanceId = AddInstance();
        UploadJobProcessor.ShowBeforeUploadCallback = (_, _) => Task.FromResult(false);
        _onUpload = (_, _) => Assert.Fail("Cancelled upload was dispatched.");
        var info = new TaskInfo(settings) { Job = TaskJob.TextUpload, DataType = EDataType.Text, TextContent = "test" };
        var context = new XerahS.Core.Tasks.Pipeline.PipelineContext { Info = info };
        var result = await new XerahS.Core.Tasks.Pipeline.FinalizationStage().ExecuteAsync(context, default);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(XerahS.Core.Tasks.Pipeline.PipelineStageResult.Continue));
            Assert.That(context.Status, Is.Not.EqualTo(XerahS.Core.TaskStatus.Failed));
            Assert.That(info.UploadCancelled, Is.True);
        });
    }

    [Test]
    public async Task BeforeUploadCancel_CompletesTheCapture_AndKeepsTheSavedFileInHistory()
    {
        var settings = Settings(AfterCaptureTasks.SaveImageToFile | AfterCaptureTasks.ShowBeforeUploadWindow | AfterCaptureTasks.UploadImageToHost);
        settings.DestinationInstanceId = AddInstance();
        int confirmations = 0;
        UploadJobProcessor.ShowBeforeUploadCallback = (_, _) =>
        {
            confirmations++;
            return Task.FromResult(false);
        };
        _onUpload = (_, _) => Assert.Fail("Cancelled upload was dispatched.");
        using var worker = WorkerTask.Create(settings, new SKBitmap(20, 10));
        await worker.StartAsync();
        Assert.Multiple(() =>
        {
            Assert.That(worker.Status, Is.EqualTo(XerahS.Core.TaskStatus.Completed));
            Assert.That(confirmations, Is.EqualTo(1), "The upload stage must not show the window again.");
            Assert.That(File.Exists(worker.Info.FilePath), Is.True);
            Assert.That(worker.Info.HistoryItemId, Is.Not.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FileUpload_ActionsAndDeletionRequireAdvancedOptIn(bool enabled)
    {
        var settings = Settings(AfterCaptureTasks.CopyFolderPathToClipboard | AfterCaptureTasks.DeleteFile);
        settings.AdvancedSettings.UseAfterCaptureTasksDuringFileUpload = enabled;
        settings.DestinationInstanceId = AddInstance();
        string path = Path.Combine(_directory, "original.txt");
        await File.WriteAllTextAsync(path, "input");
        var clipboard = new TestClipboard();
        PlatformServices.Clipboard = clipboard;
        var info = new TaskInfo(settings) { Job = TaskJob.FileUpload, FilePath = path, DataType = EDataType.File };
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(File.Exists(path), Is.EqualTo(!enabled));
        Assert.That(clipboard.Text, Is.EqualTo(enabled ? _directory : null));
    }

    [Test]
    public async Task FileClipboard_PrecedesFilePathAndFolderPath()
    {
        string path = Path.Combine(_directory, "file.txt");
        await File.WriteAllTextAsync(path, "input");
        var clipboard = new TestClipboard();
        PlatformServices.Clipboard = clipboard;
        var info = new TaskInfo(Settings(AfterCaptureTasks.CopyFileToClipboard | AfterCaptureTasks.CopyFilePathToClipboard | AfterCaptureTasks.CopyFolderPathToClipboard)) { FilePath = path };
        await AfterCaptureFileTasks.ProcessAsync(info, default);
        Assert.That(clipboard.Files, Is.EqualTo(new[] { path }));
        Assert.That(clipboard.Text, Is.Null);
    }

    [Test]
    public void ExternalAction_CanBeCancelledWhileRunning()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Uses Linux sleep.");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var action = new ExternalProgram("Wait", "/usr/bin/sleep") { Args = "30" };
        Assert.CatchAsync<OperationCanceledException>(() => action.RunAsync(Path.Combine(_directory, "input.png"), cancellation.Token));
    }

    private string AddInstance()
    {
        var instance = new UploaderInstance { ProviderId = ProviderId, Category = UploaderCategory.File, DisplayName = "Test upload", IsAvailable = true };
        InstanceManager.Instance.AddInstance(instance);
        _instances.Add(instance.InstanceId);
        return instance.InstanceId;
    }

    private sealed class TestProvider : UploaderProviderBase
    {
        public override string ProviderId => AfterCaptureTasksTests.ProviderId;
        public override string Name => "After capture test provider";
        public override string Description => "Local tests";
        public override Version Version => new(1, 0);
        public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.File, UploaderCategory.Text];
        public override Type ConfigModelType => typeof(object);
        public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new() { [UploaderCategory.File] = ["*"] };
        public override Uploader CreateInstance(string settingsJson) => new TestUploader();
    }
    private sealed class TestUploader : FileUploader
    {
        public override UploadResult Upload(Stream stream, string fileName)
        {
            _onUpload?.Invoke(stream, fileName);
            return new UploadResult { IsSuccess = true, URL = "https://example.test/image" };
        }
    }
    private sealed class TestImageEncoder : XerahS.Services.Abstractions.IImageEncoderService
    {
        public string? Path { get; private set; }
        public XerahS.Services.Abstractions.EImageFormat Format { get; private set; }
        public int Quality { get; private set; }
        public Task EncodeAsync(SKBitmap bitmap, string filePath, XerahS.Services.Abstractions.EImageFormat format, int quality = 100)
        {
            Path = filePath; Format = format; Quality = quality;
            return File.WriteAllTextAsync(filePath, "encoded avif");
        }
    }

    private sealed class ConflictUI(FileConflictResolution? result) : XerahS.CLI.Services.HeadlessUIService, IUIService
    {
        public string? RequestedPath { get; private set; }
        public Task<FileConflictResolution?> ResolveFileConflictAsync(string path, CancellationToken cancellationToken = default)
        {
            RequestedPath = path;
            return Task.FromResult(result);
        }
    }

    private sealed class TestEditor : XerahS.CLI.Services.HeadlessUIService, IUIService
    {
        public bool BackgroundPanel { get; private set; }
        public bool TaskMode { get; private set; }
        public Task<ImageEditorSessionResult?> ShowEditorSessionAsync(SKBitmap image, ImageEditorOptions editorOptions,
            string? sourceFilePath = null, bool taskMode = false, IReadOnlyList<Annotation>? annotations = null,
            bool restoredAnnotations = false, bool openBackgroundPanel = false)
        {
            BackgroundPanel = openBackgroundPanel;
            TaskMode = taskMode;
            return Task.FromResult<ImageEditorSessionResult?>(null);
        }
    }
    private sealed class TestClipboard : XerahS.Platform.Abstractions.IClipboardService
    {
        public string? Text { get; private set; }
        public string[]? Files { get; private set; }
        public void Clear() { Text = null; Files = null; }
        public bool ContainsText() => Text != null;
        public SKBitmap? Image { get; set; }
        public bool ContainsImage() => Image != null;
        public bool ContainsFileDropList() => Files != null;
        public string? GetText() => Text;
        public void SetText(string text) => Text = text;
        public SKBitmap? GetImage() => Image;
        public void SetImage(SKBitmap image) { }
        public string[]? GetFileDropList() => Files;
        public void SetFileDropList(string[] files) => Files = files;
        public object? GetData(string format) => null;
        public void SetData(string format, object data) { }
        public bool ContainsData(string format) => false;
        public Task<string?> GetTextAsync() => Task.FromResult(Text);
        public Task SetTextAsync(string text) { Text = text; return Task.CompletedTask; }
    }
}
