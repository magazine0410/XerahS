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
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Managers;
using XerahS.Core.Tasks;
using XerahS.Core.Tasks.Pipeline;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;
using TaskStatus = XerahS.Core.TaskStatus;

namespace XerahS.Tests.Tasks;

[TestFixture]
[NonParallelizable]
public class UploadWorkflowTests
{
    private const string ProviderId = "hotkey-upload-tests";
    private static readonly FieldInfo PersonalFolder = typeof(PathsManager).GetField("_personalFolder", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FolderOverride = typeof(PathsManager).GetField("_personalFolderOverrideSet", BindingFlags.Static | BindingFlags.NonPublic)!;
    private object? _previousFolder;
    private object? _previousOverride;
    private string _directory = null!;
    private readonly List<UploaderInstance> _instances = [];
    private static string? _uploadedText;
    private static string? _shortenedInput;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-hotkey-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousFolder = PersonalFolder.GetValue(null);
        _previousOverride = FolderOverride.GetValue(null);
        PathsManager.PersonalFolder = _directory;
        ProviderCatalog.RegisterProvider(new TestProvider());
        _uploadedText = _shortenedInput = null;
    }

    [TearDown]
    public void TearDown()
    {
        UploadCancellationScope.CancelAll();
        foreach (var instance in _instances) InstanceManager.Instance.RemoveInstance(instance.InstanceId);
        _instances.Clear();
        PersonalFolder.SetValue(null, _previousFolder);
        FolderOverride.SetValue(null, _previousOverride);
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task ShortenJob_UsesSelectedShortener_AndReturnsShortLink()
    {
        var instance = AddInstance(UploaderCategory.UrlShortener, "shorten");
        var settings = new TaskSettings { Job = WorkflowType.ShortenURL, UrlShortenerDestinationInstanceId = instance.InstanceId, AfterUploadJob = AfterUploadTasks.None };
        WorkerTask? observed = null;
        void Started(object? sender, WorkerTask task) { if (ReferenceEquals(settings, task.Info.TaskSettings)) observed = task; }
        TaskManager.Instance.TaskStarted += Started;
        try { await TaskManager.Instance.StartTextTask(settings, "https://example.test/long-link"); }
        finally { TaskManager.Instance.TaskStarted -= Started; }

        Assert.Multiple(() =>
        {
            Assert.That(observed!.Info.Job, Is.EqualTo(TaskJob.ShortenURL));
            Assert.That(observed.Info.DataType, Is.EqualTo(EDataType.URL));
            Assert.That(observed.IsSuccessful, Is.True);
            Assert.That(observed.Info.Result.URL, Is.EqualTo("https://short.test/a"));
            Assert.That(observed.Info.Result.ShortenedURL, Is.EqualTo("https://short.test/a"));
            Assert.That(observed.Info.UploaderHost, Is.EqualTo(instance.DisplayName));
            Assert.That(_shortenedInput, Is.EqualTo("https://example.test/long-link"));
            Assert.That(_uploadedText, Is.Null);
        });
    }

    [TestCase("https://example.test/long", "fail", "shortener failed")]
    [TestCase("not a URL", "shorten", "valid HTTP")]
    [TestCase("file:///etc/hosts", "shorten", "valid HTTP")]
    public async Task ShortenJob_FailsWithoutUploadingInputAsText(string input, string outcome, string error)
    {
        var instance = AddInstance(UploaderCategory.UrlShortener, outcome);
        using var worker = CreateTextWorker(WorkflowType.ShortenURL, input, instance.InstanceId);
        await worker.StartAsync();
        Assert.Multiple(() =>
        {
            Assert.That(worker.Status, Is.EqualTo(TaskStatus.Failed));
            Assert.That(worker.IsSuccessful, Is.False);
            Assert.That(worker.Error!.Message, Does.Contain(error));
            Assert.That(worker.Info.Metadata.UploadURL, Is.Null.Or.Empty);
            Assert.That(_uploadedText, Is.Null);
        });
    }

    [Test]
    public async Task ShortenJob_RejectsMissingOrWrongCategoryDestination()
    {
        var instance = AddInstance(UploaderCategory.Text, "text");
        using var worker = CreateTextWorker(WorkflowType.ShortenURL, "https://example.test", instance.InstanceId);
        await worker.StartAsync();
        Assert.That(worker.Error!.Message, Does.Contain("No URL shortener configured"));
        Assert.That(_uploadedText, Is.Null);
    }

    [Test]
    public async Task UploadText_DoesNotShortenUrlLookingTextOrReopenDialog()
    {
        var instance = AddInstance(UploaderCategory.Text, "text");
        const string input = "https://example.test/a\nSecond line";
        using var worker = CreateTextWorker(WorkflowType.UploadText, input, instance.InstanceId);
        await worker.StartAsync();
        Assert.Multiple(() =>
        {
            Assert.That(worker.IsSuccessful, Is.True);
            Assert.That(_uploadedText, Is.EqualTo(input));
            Assert.That(_shortenedInput, Is.Null);
        });
    }

    [Test]
    public async Task StopJob_CancelsActiveTransfersAndBatches_AllowsFutureUploads()
    {
        using var batch = new UploadCancellationScope();
        using var content = new MemoryStream([1, 2, 3]);
        var handler = new BlockingHandler();
        var request = new UploadRequest { Content = content, FileName = "test.bin", Category = UploaderCategory.File };
        var transfer = UploaderUploadAdapter.UploadAsync(handler, request, CancellationToken.None);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = WorkerTask.Create(new TaskSettings { Job = WorkflowType.StopUploads });
        await stop.StartAsync();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await transfer);
        Assert.That(batch.Token.IsCancellationRequested, Is.True);
        using var next = new UploadCancellationScope();
        Assert.That(next.Token.IsCancellationRequested, Is.False);
    }

    [Test]
    public async Task StopJob_AlsoCancelsAfterCaptureUploads()
    {
        var instance = AddInstance(UploaderCategory.Image, "blocking");
        var handler = new BlockingHandler();
        TestProvider.Blocking = handler;
        string path = Path.Combine(_directory, "capture.png");
        using var image = new SkiaSharp.SKBitmap(10, 10);
        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
        using (var file = File.Create(path)) data.SaveTo(file);
        var info = new TaskInfo(new TaskSettings
        {
            Job = WorkflowType.PrintScreen,
            AfterCaptureJob = AfterCaptureTasks.UploadImageToHost,
            DestinationInstanceId = instance.InstanceId
        }) { FilePath = path, Metadata = new TaskMetadata(image) };
        var operation = new XerahS.Core.Tasks.Processors.CaptureJobProcessor().ProcessAsync(info, CancellationToken.None);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        UploadCancellationScope.CancelAll();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await operation);
    }

    [Test]
    public async Task StopJob_StopsTasksThatHaveNotReachedTheirUpload()
    {
        // As in ShareX (TaskManager.StopAllTasks), a capture that is still in its annotation editor is
        // stopped too, so its upload never starts when the editor closes.
        var instance = AddInstance(UploaderCategory.Image, "blocking");
        var handler = new BlockingHandler();
        TestProvider.Blocking = handler;
        var editor = new HoldingEditorUIService();
        XerahS.Platform.Abstractions.PlatformServices.RegisterUIService(editor);
        try
        {
            var capture = TaskManager.Instance.StartTask(new TaskSettings
            {
                Job = WorkflowType.PrintScreen,
                AfterCaptureJob = AfterCaptureTasks.AnnotateMedia | AfterCaptureTasks.UploadImageToHost,
                AfterUploadJob = AfterUploadTasks.None,
                DestinationInstanceId = instance.InstanceId
            }, new SkiaSharp.SKBitmap(10, 10));
            await editor.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await TaskManager.Instance.StartTask(new TaskSettings { Job = WorkflowType.StopUploads });
            editor.Close.SetResult();
            await capture.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(handler.Entered.Task.IsCompleted, Is.False, "The stopped capture must not start its upload.");
        }
        finally
        {
            editor.Close.TrySetResult();
            XerahS.Platform.Abstractions.PlatformServices.Reset();
        }
    }

    [Test]
    public async Task StopBeforeWorkerStarts_FinalizesWithoutExecuting()
    {
        using var worker = WorkerTask.Create(new TaskSettings { Job = WorkflowType.FileUpload });
        worker.Stop();
        await worker.StartAsync();
        Assert.That(worker.Status, Is.EqualTo(TaskStatus.Stopped));
        Assert.That(worker.HasFinished, Is.True);
    }

    [Test]
    public void ShortenerDestination_DoesNotOverwriteUploadDestination()
    {
        var settings = new TaskSettings { DestinationInstanceId = Guid.NewGuid().ToString() };
        string upload = settings.DestinationInstanceId;
        string shorten = Guid.NewGuid().ToString();
        settings.SetDestinationInstanceId(WorkflowType.ShortenURL, shorten);
        Assert.That(settings.GetDestinationInstanceId(WorkflowType.ShortenURL), Is.EqualTo(shorten));
        Assert.That(settings.GetDestinationInstanceId(WorkflowType.UploadText), Is.EqualTo(upload));
    }

    private UploaderInstance AddInstance(UploaderCategory category, string outcome)
    {
        var instance = new UploaderInstance { ProviderId = ProviderId, Category = category, DisplayName = "Test " + outcome, SettingsJson = outcome, IsAvailable = true };
        InstanceManager.Instance.AddInstance(instance);
        _instances.Add(instance);
        return instance;
    }

    private static WorkerTask CreateTextWorker(WorkflowType job, string text, string destination)
    {
        var settings = new TaskSettings { Job = job, DestinationInstanceId = destination, UrlShortenerDestinationInstanceId = destination, AfterUploadJob = AfterUploadTasks.None };
        var worker = WorkerTask.Create(settings);
        worker.Info.Job = job == WorkflowType.ShortenURL ? TaskJob.ShortenURL : TaskJob.TextUpload;
        worker.Info.DataType = job == WorkflowType.ShortenURL ? EDataType.URL : EDataType.Text;
        worker.Info.TextContent = text;
        return worker;
    }

    private sealed class TestProvider : UploaderProviderBase
    {
        public static BlockingHandler? Blocking { get; set; }
        public override string ProviderId => UploadWorkflowTests.ProviderId;
        public override string Name => "Hotkey upload tests";
        public override string Description => "Test provider";
        public override Version Version => new(1, 0);
        public override UploaderCategory[] SupportedCategories => [UploaderCategory.Text, UploaderCategory.Image, UploaderCategory.UrlShortener];
        public override Type ConfigModelType => typeof(object);
        public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new() { [UploaderCategory.Text] = ["*"], [UploaderCategory.UrlShortener] = [] };
        public override Uploader CreateInstance(string settingsJson) => settingsJson switch
        {
            "text" => new TextSink(),
            "blocking" => Blocking!,
            _ => new Shortener(settingsJson == "fail")
        };
    }

    private sealed class Shortener(bool fail) : UrlShortener
    {
        public override UploadResult ShortenURL(string url)
        {
            _shortenedInput = url;
            return new UploadResult { URL = url, ShortenedURL = fail ? null : "https://short.test/a", Response = fail ? "shortener failed" : "ok" };
        }
    }

    private sealed class TextSink : FileUploader
    {
        public override UploadResult Upload(Stream stream, string fileName)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            _uploadedText = reader.ReadToEnd();
            return new UploadResult { URL = "https://paste.test/a", IsSuccess = true };
        }
    }

    /// <summary>Holds the annotation editor open until the test closes it.</summary>
    private sealed class HoldingEditorUIService : XerahS.CLI.Services.HeadlessUIService, XerahS.Platform.Abstractions.IUIService
    {
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Close { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ShareX.ImageEditor.Hosting.ImageEditorSessionResult?> ShowEditorSessionAsync(SkiaSharp.SKBitmap image,
            ShareX.ImageEditor.Hosting.ImageEditorOptions editorOptions, string? sourceFilePath = null, bool taskMode = false,
            IReadOnlyList<ShareX.ImageEditor.Core.Annotations.Annotation>? annotations = null, bool restoredAnnotations = false,
            bool openBackgroundPanel = false)
        {
            Opened.TrySetResult();
            await Close.Task;
            return null;
        }
    }

    private sealed class BlockingHandler : FileUploader, IUploadHandler
    {
        public override UploadResult Upload(Stream stream, string fileName) => throw new NotSupportedException();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<UploadOutcome> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
        {
            Entered.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return UploadOutcome.Failed("unreachable");
        }
    }
}
