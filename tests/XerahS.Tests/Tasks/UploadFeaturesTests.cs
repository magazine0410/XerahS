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
using System.Text;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Managers;
using XerahS.Core.Tasks;
using XerahS.Core.Tasks.Processors;
using XerahS.Platform.Abstractions;
using XerahS.Services.Abstractions;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;
using TaskStatus = XerahS.Core.TaskStatus;

namespace XerahS.Tests.Tasks;

/// <summary>ShareX's upload settings: retries, buffer size, large file warning, early URL copy, and the task start steps.</summary>
[TestFixture]
[NonParallelizable]
public class UploadFeaturesTests
{
    private const string ProviderId = "upload-features-tests";
    private static readonly FieldInfo PersonalFolder = typeof(PathsManager).GetField("_personalFolder", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FolderOverride = typeof(PathsManager).GetField("_personalFolderOverrideSet", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly List<Upload> Uploads = [];
    private static TaskCompletionSource _blockEntered = new();
    private static TaskCompletionSource _blockRelease = new();
    private static FakeClipboard? _clipboard;

    private object? _previousFolder, _previousOverride;
    private string _directory = null!;
    private readonly List<string> _instances = [];
    private (int UploadLimit, int BufferSizePower, int Retry, int LargeFile, bool BinaryUnits) _previousSettings;

    private sealed record Upload(string Behaviour, string FileName, byte[] Content, int BufferSize, string? ClipboardText);

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-upload-features-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousFolder = PersonalFolder.GetValue(null);
        _previousOverride = FolderOverride.GetValue(null);
        PathsManager.PersonalFolder = _directory;
        PlatformServices.Reset();
        _clipboard = new FakeClipboard();
        PlatformServices.Clipboard = _clipboard;
        ProviderCatalog.RegisterProvider(new TestProvider());

        var settings = SettingsManager.Settings;
        _previousSettings = (settings.UploadLimit, settings.BufferSizePower, settings.MaxUploadFailRetry, settings.ShowLargeFileSizeWarning, settings.BinaryUnits);
        settings.UploadLimit = 0;
        settings.BufferSizePower = 5;
        settings.MaxUploadFailRetry = 1;
        settings.ShowLargeFileSizeWarning = 0;
        UploadJobProcessor.RetryDelay = TimeSpan.Zero;
        lock (Uploads) Uploads.Clear();
        _blockEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _blockRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [TearDown]
    public void TearDown()
    {
        _blockRelease.TrySetResult();
        UploadJobProcessor.ShowLargeFileUploadWarningCallback = null;
        UploadJobProcessor.SaveApplicationSettings = () => SettingsManager.SaveApplicationConfigAsync();
        UploadJobProcessor.RetryDelay = TimeSpan.FromSeconds(1);
        CaptureJobProcessor.ShowQuickTaskMenuCallback = null;
        var settings = SettingsManager.Settings;
        (settings.UploadLimit, settings.BufferSizePower, settings.MaxUploadFailRetry, settings.ShowLargeFileSizeWarning, settings.BinaryUnits) = _previousSettings;
        foreach (string id in _instances) InstanceManager.Instance.RemoveInstance(id);
        _instances.Clear();
        _clipboard = null;
        PlatformServices.Reset();
        PersonalFolder.SetValue(null, _previousFolder);
        FolderOverride.SetValue(null, _previousOverride);
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task FailedUpload_IsTriedAgain_AndTheSecondTryCounts()
    {
        var settings = Settings(AddInstance(UploaderCategory.Text, "fail-once"));
        var task = await RunTextUploadAsync(settings, "hello");

        Assert.Multiple(() =>
        {
            Assert.That(task.Status, Is.EqualTo(TaskStatus.Completed));
            Assert.That(Uploads, Has.Count.EqualTo(2));
            Assert.That(task.Info.Result.URL, Does.StartWith("https://upload.test/"));
        });
    }

    [TestCase(0, 1)]
    [TestCase(1, 2)]
    [TestCase(3, 4)]
    public async Task FailingUpload_IsTriedOnceAndThenRetriedTheConfiguredNumberOfTimes(int retries, int attempts)
    {
        SettingsManager.Settings.MaxUploadFailRetry = retries;
        var settings = Settings(AddInstance(UploaderCategory.Text, "fail"));
        var task = await RunTextUploadAsync(settings, "hello");

        Assert.That(task.Status, Is.EqualTo(TaskStatus.Failed));
        Assert.That(Uploads, Has.Count.EqualTo(attempts));
    }

    [TestCase(5, 32 * 1024)]
    [TestCase(0, 1024)]
    [TestCase(13, 8 * 1024 * 1024)]
    public async Task Uploader_UsesTheBufferSizeFromTheSettings(int power, int bytes)
    {
        SettingsManager.Settings.BufferSizePower = power;
        await RunTextUploadAsync(Settings(AddInstance(UploaderCategory.Text, "record")), "hello");
        Assert.That(Uploads.Single().BufferSize, Is.EqualTo(bytes));
    }

    [Test]
    public void BufferSize_StaysWithinShareXsList()
    {
        Assert.That(TaskHelpers.GetUploadBufferSize(-2), Is.EqualTo(1024));
        Assert.That(TaskHelpers.GetUploadBufferSize(40), Is.EqualTo(8 * 1024 * 1024));
    }

    [Test]
    public async Task LargeUpload_AsksFirst_AndCancelStopsTheTask()
    {
        SettingsManager.Settings.ShowLargeFileSizeWarning = 1;
        SettingsManager.Settings.BinaryUnits = false;
        int asked = 0;
        UploadJobProcessor.ShowLargeFileUploadWarningCallback = _ =>
        {
            asked++;
            return Task.FromResult(new LargeFileUploadWarningResult(false, false));
        };

        var task = await RunTextUploadAsync(Settings(AddInstance(UploaderCategory.Text, "record")), new string('a', 1_000_001));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(1));
            Assert.That(task.Status, Is.EqualTo(TaskStatus.Stopped), "As in ShareX, Cancel stops the task.");
            Assert.That(Uploads, Is.Empty);
            Assert.That(SettingsManager.Settings.ShowLargeFileSizeWarning, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task LargeUpload_Continue_Uploads_AndDontShowAgainTurnsTheWarningOff()
    {
        SettingsManager.Settings.ShowLargeFileSizeWarning = 1;
        SettingsManager.Settings.BinaryUnits = false;
        int saves = 0;
        UploadJobProcessor.SaveApplicationSettings = () => { saves++; return Task.CompletedTask; };
        UploadJobProcessor.ShowLargeFileUploadWarningCallback = _ => Task.FromResult(new LargeFileUploadWarningResult(true, true));

        var task = await RunTextUploadAsync(Settings(AddInstance(UploaderCategory.Text, "record")), new string('a', 1_000_001));

        Assert.Multiple(() =>
        {
            Assert.That(task.Status, Is.EqualTo(TaskStatus.Completed));
            Assert.That(Uploads, Has.Count.EqualTo(1));
            Assert.That(SettingsManager.Settings.ShowLargeFileSizeWarning, Is.Zero);
            Assert.That(saves, Is.EqualTo(1));
        });
    }

    [TestCase(false, 1_000_000, false)]
    [TestCase(false, 1_000_001, true)]
    [TestCase(true, 1_048_576, false)]
    [TestCase(true, 1_048_577, true)]
    public async Task LargeFileWarning_UsesMegabytesInTheChosenUnits(bool binaryUnits, int length, bool expectWarning)
    {
        SettingsManager.Settings.ShowLargeFileSizeWarning = 1;
        SettingsManager.Settings.BinaryUnits = binaryUnits;
        bool asked = false;
        UploadJobProcessor.ShowLargeFileUploadWarningCallback = _ =>
        {
            asked = true;
            return Task.FromResult(new LargeFileUploadWarningResult(true, false));
        };

        string file = Path.Combine(_directory, "large.bin");
        await File.WriteAllBytesAsync(file, new byte[length]);
        var task = await RunFileUploadAsync(Settings(AddInstance(UploaderCategory.File, "record")), file);

        Assert.That(asked, Is.EqualTo(expectWarning));
        Assert.That(task.Status, Is.EqualTo(TaskStatus.Completed));
    }

    [Test]
    public void LargeFileSize_DoesNotOverflowFrom2048Megabytes()
    {
        Assert.That(UploadJobProcessor.GetLargeFileSizeLimit(4096, binaryUnits: true), Is.EqualTo(4096L * 1024 * 1024));
        Assert.That(UploadJobProcessor.GetLargeFileSizeLimit(0, binaryUnits: true), Is.Zero);
    }

    [Test]
    public async Task CopyURLBeforeUpload_PutsTheURLOnTheClipboardBeforeTheUpload()
    {
        var settings = Settings(AddInstance(UploaderCategory.File, "early"));
        settings.AfterUploadJob = AfterUploadTasks.CopyURLToClipboard;
        settings.AdvancedSettings.EarlyCopyURL = true;
        string file = Path.Combine(_directory, "early.txt");
        await File.WriteAllTextAsync(file, "early");

        var task = await RunFileUploadAsync(settings, file);

        Assert.Multiple(() =>
        {
            Assert.That(task.Status, Is.EqualTo(TaskStatus.Completed));
            Assert.That(Uploads.Single().ClipboardText, Is.EqualTo("https://early.test/early.txt"), "Copied before the upload.");
            Assert.That(_clipboard!.Text, Is.EqualTo("https://upload.test/early.txt"), "Copy URL to clipboard copies the result after.");
        });
    }

    [Test]
    public async Task CopyURLBeforeUpload_TakesTheURLOffTheClipboardWhenTheUploadFails()
    {
        SettingsManager.Settings.MaxUploadFailRetry = 0;
        var settings = Settings(AddInstance(UploaderCategory.File, "early-fail"));
        settings.AfterUploadJob = AfterUploadTasks.CopyURLToClipboard;
        settings.AdvancedSettings.EarlyCopyURL = true;
        string file = Path.Combine(_directory, "early.txt");
        await File.WriteAllTextAsync(file, "early");

        var task = await RunFileUploadAsync(settings, file);

        Assert.Multiple(() =>
        {
            Assert.That(task.Status, Is.EqualTo(TaskStatus.Failed));
            Assert.That(Uploads.Single().ClipboardText, Is.EqualTo("https://early.test/early.txt"));
            Assert.That(_clipboard!.Text, Is.Null);
        });
    }

    [TestCase(false, AfterUploadTasks.CopyURLToClipboard)]
    [TestCase(true, AfterUploadTasks.None)]
    public async Task CopyURLBeforeUpload_NeedsTheOptionAndCopyURLToClipboard(bool earlyCopy, AfterUploadTasks afterUpload)
    {
        _clipboard!.SetText("previous");
        var settings = Settings(AddInstance(UploaderCategory.File, "early"));
        settings.AfterUploadJob = afterUpload;
        settings.AdvancedSettings.EarlyCopyURL = earlyCopy;
        string file = Path.Combine(_directory, "early.txt");
        await File.WriteAllTextAsync(file, "early");

        await RunFileUploadAsync(settings, file);

        Assert.That(Uploads.Single().ClipboardText, Is.EqualTo("previous"));
    }

    [Test]
    public async Task AutomaticallyClearClipboard_ClearsItWhenAnUploadTaskStarts()
    {
        _clipboard!.SetText("previous");
        var settings = Settings(AddInstance(UploaderCategory.Text, "record"));
        settings.AdvancedSettings.AutoClearClipboard = true;

        await RunTextUploadAsync(settings, "hello");

        Assert.That(Uploads.Single().ClipboardText, Is.Null);
    }

    [Test]
    public async Task AutomaticallyClearClipboard_LeavesItForTasksThatDoNotUpload()
    {
        _clipboard!.SetText("previous");
        var settings = Settings(AddInstance(UploaderCategory.Image, "record"));
        settings.Job = WorkflowType.PrintScreen;
        settings.AfterCaptureJob = AfterCaptureTasks.SaveImageToFile;
        settings.AdvancedSettings.AutoClearClipboard = true;

        using var image = new SKBitmap(20, 10);
        await TaskManager.Instance.StartTask(settings, image.Copy());

        Assert.That(_clipboard.Text, Is.EqualTo("previous"));
        Assert.That(Uploads, Is.Empty);
    }

    [Test]
    public async Task TextUpload_IsAlsoSavedAsAFileWithoutAByteOrderMark()
    {
        const string text = "héllo wörld";
        var task = await RunTextUploadAsync(Settings(AddInstance(UploaderCategory.Text, "record")), text);

        string[] files = Directory.GetFiles(_directory, "*.txt");
        byte[] expected = new UTF8Encoding(false).GetBytes(text);
        Assert.Multiple(() =>
        {
            Assert.That(files, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllBytes(files[0]), Is.EqualTo(expected));
            Assert.That(task.Info.FilePath, Is.EqualTo(files[0]));
            Assert.That(Uploads.Single().Content, Is.EqualTo(expected), "The upload sends the text itself.");
            Assert.That(Uploads.Single().FileName, Is.EqualTo(Path.GetFileName(files[0])));
        });
    }

    [Test]
    public async Task TextUpload_IsNotSavedWithSaveTextTasksAsFilesOff()
    {
        var settings = Settings(AddInstance(UploaderCategory.Text, "record"));
        settings.AdvancedSettings.TextTaskSaveAsFile = false;

        var task = await RunTextUploadAsync(settings, "hello");

        Assert.That(Directory.GetFiles(_directory, "*.txt"), Is.Empty);
        Assert.That(task.Info.FilePath, Is.Empty);
        Assert.That(Uploads, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ProcessImagesDuringFileUpload_UploadsTheProcessedImage_WithoutTheQuickTaskMenu()
    {
        string source = WriteImage("photo.png");
        byte[] original = File.ReadAllBytes(source);
        int menus = 0;
        CaptureJobProcessor.ShowQuickTaskMenuCallback = (_, _) => { menus++; return Task.FromResult(QuickTaskMenuResult.Cancel); };
        var settings = Settings(AddInstance(UploaderCategory.Image, "record"));
        settings.AfterCaptureJob = AfterCaptureTasks.ShowQuickTaskMenu | AfterCaptureTasks.UploadImageToHost;
        settings.ImageSettings.ImageFormat = EImageFormat.JPEG;
        settings.AdvancedSettings.ProcessImagesDuringFileUpload = true;

        var task = await RunFileUploadAsync(settings, source);

        var upload = Uploads.Single();
        Assert.Multiple(() =>
        {
            Assert.That(task.Status, Is.EqualTo(TaskStatus.Completed));
            Assert.That(upload.FileName, Is.EqualTo("photo.jpg"), "The file's name, with the image format's extension.");
            Assert.That(upload.Content.Take(2), Is.EqualTo(new byte[] { 0xFF, 0xD8 }), "The image is encoded again, as JPEG.");
            Assert.That(menus, Is.Zero, "ShareX starts a processed file upload without the quick task menu.");
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(original));
            string shots = Path.Combine(_directory, "shots");
            Assert.That(Directory.Exists(shots) ? Directory.GetFiles(shots, "*", SearchOption.AllDirectories) : [], Is.Empty,
                "Without \"Save image to file\", nothing is saved.");
        });
    }

    [Test]
    public async Task ProcessImagesDuringFileUpload_SavesTheImageUnderTheFilesName()
    {
        string source = WriteImage("photo.png");
        var settings = Settings(AddInstance(UploaderCategory.Image, "record"));
        settings.AfterCaptureJob = AfterCaptureTasks.SaveImageToFile | AfterCaptureTasks.UploadImageToHost;
        settings.ImageSettings.ImageFormat = EImageFormat.JPEG;
        settings.AdvancedSettings.ProcessImagesDuringFileUpload = true;

        var task = await RunFileUploadAsync(settings, source);

        string saved = Path.Combine(_directory, "shots", "photo.jpg");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(saved), Is.True);
            Assert.That(task.Info.FilePath, Is.EqualTo(saved));
            Assert.That(Uploads.Single().FileName, Is.EqualTo("photo.jpg"));
        });
    }

    [Test]
    public async Task ProcessImagesDuringFileUpload_UploadsOnlyWithUploadImageToHost()
    {
        string source = WriteImage("photo.png");
        var settings = Settings(AddInstance(UploaderCategory.Image, "record"));
        settings.AfterCaptureJob = AfterCaptureTasks.SaveImageToFile;
        settings.AdvancedSettings.ProcessImagesDuringFileUpload = true;

        var task = await RunFileUploadAsync(settings, source);

        Assert.That(task.Status, Is.EqualTo(TaskStatus.Completed));
        Assert.That(Uploads, Is.Empty);
        Assert.That(File.Exists(Path.Combine(_directory, "shots", "photo.png")), Is.True);
    }

    [TestCase(false, "photo.png")]
    [TestCase(true, "notes.png")]
    public async Task FileUpload_SendsTheFileAsItIs_WithoutProcessingOrForAFileThatIsNotAnImage(bool process, string name)
    {
        string source = process ? Path.Combine(_directory, name) : WriteImage(name);
        if (process) await File.WriteAllTextAsync(source, "not an image");
        byte[] original = File.ReadAllBytes(source);
        var settings = Settings(AddInstance(UploaderCategory.File, "record"));
        settings.AfterCaptureJob = AfterCaptureTasks.UploadImageToHost;
        settings.ImageSettings.ImageFormat = EImageFormat.JPEG;
        settings.AdvancedSettings.ProcessImagesDuringFileUpload = process;

        var task = await RunFileUploadAsync(settings, source);

        Assert.That(task.Info.Job, Is.EqualTo(TaskJob.FileUpload));
        Assert.That(Uploads.Single().FileName, Is.EqualTo(name));
        Assert.That(Uploads.Single().Content, Is.EqualTo(original));
    }

    [Test]
    public async Task SimultaneousUploadLimit_KeepsTheNextTaskWaitingUntilARunningTaskEnds()
    {
        SettingsManager.Settings.UploadLimit = 1;
        string id = AddInstance(UploaderCategory.Text, "block");

        var first = TaskManager.Instance.StartTextTask(Settings(id), "first");
        await _blockEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = TaskManager.Instance.StartTextTask(Settings(id), "second");

        Assert.That(() => UploadQueue.Instance.WaitingCount, Is.EqualTo(1).After(5000, 10));
        Assert.That(Uploads, Has.Count.EqualTo(1), "The second task has not started.");

        _blockRelease.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(Uploads.Select(upload => Encoding.UTF8.GetString(upload.Content)), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(UploadQueue.Instance.RunningCount, Is.Zero);
    }

    [Test]
    public async Task SimultaneousUploadLimit_StartsWaitingUploadsInTheOrderTheyWereCreated()
    {
        SettingsManager.Settings.UploadLimit = 1;
        string id = AddInstance(UploaderCategory.Text, "block");

        var uploads = new List<Task> { TaskManager.Instance.StartTextTask(Settings(id), "first") };
        await _blockEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (string text in new[] { "second", "third", "fourth" })
        {
            uploads.Add(TaskManager.Instance.StartTextTask(Settings(id), text));
        }

        Assert.That(UploadQueue.Instance.WaitingCount, Is.EqualTo(3), "Each task takes its place in the queue when it is created.");
        _blockRelease.SetResult();
        await Task.WhenAll(uploads).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.That(Uploads.Select(upload => Encoding.UTF8.GetString(upload.Content)),
            Is.EqualTo(new[] { "first", "second", "third", "fourth" }), "As in ShareX, queued tasks start in order.");
        Assert.That(UploadQueue.Instance.RunningCount + UploadQueue.Instance.WaitingCount, Is.Zero);
    }

    [Test]
    public async Task SimultaneousUploadLimit_StopAllUploadsAlsoStopsWaitingTasks()
    {
        SettingsManager.Settings.UploadLimit = 1;
        string id = AddInstance(UploaderCategory.Text, "block");

        var first = TaskManager.Instance.StartTextTask(Settings(id), "first");
        await _blockEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        WorkerTask? waiting = null;
        var waitingSettings = Settings(id);
        void Started(object? sender, WorkerTask task) { if (ReferenceEquals(task.Info.TaskSettings, waitingSettings)) waiting = task; }
        TaskManager.Instance.TaskStarted += Started;
        Task second;
        try { second = TaskManager.Instance.StartTextTask(waitingSettings, "second"); }
        finally { TaskManager.Instance.TaskStarted -= Started; }
        Assert.That(() => UploadQueue.Instance.WaitingCount, Is.EqualTo(1).After(5000, 10));

        await TaskManager.Instance.StartTask(new TaskSettings { Job = WorkflowType.StopUploads });
        _blockRelease.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(waiting!.Status, Is.EqualTo(TaskStatus.Stopped));
        Assert.That(Uploads, Has.Count.EqualTo(1));
        Assert.That(UploadQueue.Instance.WaitingCount, Is.Zero);
    }

    private string WriteImage(string name)
    {
        string path = Path.Combine(_directory, "source", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(20, 10);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private TaskSettings Settings(string instanceId)
    {
        var settings = new TaskSettings
        {
            Job = WorkflowType.UploadText,
            DestinationInstanceId = instanceId,
            AfterCaptureJob = AfterCaptureTasks.None,
            AfterUploadJob = AfterUploadTasks.None,
            AllowCrossCategoryFallback = false,
            OverrideScreenshotsFolder = true,
            ScreenshotsFolder = _directory
        };
        settings.ImageSettings.FileExistAction = FileExistAction.UniqueName;
        return settings;
    }

    private static async Task<WorkerTask> RunTextUploadAsync(TaskSettings settings, string text)
    {
        settings.Job = WorkflowType.UploadText;
        return await RunAsync(settings, () => TaskManager.Instance.StartTextTask(settings, text));
    }

    private async Task<WorkerTask> RunFileUploadAsync(TaskSettings settings, string filePath)
    {
        settings.Job = WorkflowType.FileUpload;
        if (settings.ScreenshotsFolder == _directory) settings.ScreenshotsFolder = Path.Combine(_directory, "shots");
        return await RunAsync(settings, () => TaskManager.Instance.StartFileTask(settings, filePath));
    }

    private static async Task<WorkerTask> RunAsync(TaskSettings settings, Func<Task> start)
    {
        WorkerTask? started = null;
        void Started(object? sender, WorkerTask task) { if (ReferenceEquals(task.Info.TaskSettings, settings)) started = task; }
        TaskManager.Instance.TaskStarted += Started;
        try { await start().WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { TaskManager.Instance.TaskStarted -= Started; }
        return started ?? throw new AssertionException("The task did not start.");
    }

    private string AddInstance(UploaderCategory category, string behaviour)
    {
        var instance = new UploaderInstance
        {
            ProviderId = ProviderId, Category = category, DisplayName = "Upload features " + behaviour,
            SettingsJson = behaviour, IsAvailable = true
        };
        InstanceManager.Instance.AddInstance(instance);
        _instances.Add(instance.InstanceId);
        return instance.InstanceId;
    }

    private sealed class TestProvider : UploaderProviderBase
    {
        public override string ProviderId => UploadFeaturesTests.ProviderId;
        public override string Name => "Upload features tests";
        public override string Description => "Local tests";
        public override Version Version => new(1, 0);
        public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.Text, UploaderCategory.File];
        public override Type ConfigModelType => typeof(object);
        public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new()
        {
            [UploaderCategory.Image] = ["*"], [UploaderCategory.Text] = ["*"], [UploaderCategory.File] = ["*"]
        };
        public override Uploader CreateInstance(string settingsJson) => new TestUploader(settingsJson);
    }

    private sealed class TestUploader(string behaviour) : FileUploader
    {
        public override UploadResult Upload(Stream stream, string fileName)
        {
            if (behaviour.StartsWith("early", StringComparison.Ordinal)) OnEarlyURLCopyRequested("https://early.test/" + fileName);
            using var content = new MemoryStream();
            stream.CopyTo(content);
            int attempt;
            lock (Uploads)
            {
                Uploads.Add(new Upload(behaviour, fileName, content.ToArray(), BufferSize, _clipboard?.Text));
                attempt = Uploads.Count;
            }

            if (behaviour == "block")
            {
                _blockEntered.TrySetResult();
                _blockRelease.Task.Wait(TimeSpan.FromSeconds(10));
            }

            bool fail = behaviour is "fail" or "early-fail" || (behaviour == "fail-once" && attempt == 1);
            return fail
                ? new UploadResult { IsSuccess = false, Response = "upload rejected" }
                : new UploadResult { IsSuccess = true, URL = "https://upload.test/" + fileName };
        }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public string[]? Files { get; set; }
        public void Clear() { Text = null; Files = null; }
        public bool ContainsText() => Text != null;
        public bool ContainsImage() => false;
        public bool ContainsFileDropList() => Files != null;
        public string? GetText() => Text;
        public void SetText(string text) => Text = text;
        public SKBitmap? GetImage() => null;
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
