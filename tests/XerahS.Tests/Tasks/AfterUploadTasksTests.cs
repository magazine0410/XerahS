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
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.Core.Tasks;
using XerahS.Core.Tasks.Processors;
using XerahS.History;
using XerahS.Platform.Abstractions;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Tasks;

[TestFixture, NonParallelizable]
public partial class AfterUploadTasksTests
{
    private const string ProviderId = "after-upload-test-provider";
    private const string OriginalUrl = "https://files.test/original.png";
    private const string ShortUrl = "https://short.test/a";
    private const AfterUploadTasks AllTasks = AfterUploadTasks.UseURLShortener | AfterUploadTasks.CopyURLToClipboard |
        AfterUploadTasks.OpenURL | AfterUploadTasks.ShowQRCode | AfterUploadTasks.ShowAfterUploadWindow;
    private static readonly FieldInfo PersonalFolder = typeof(PathsManager).GetField("_personalFolder", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FolderOverride = typeof(PathsManager).GetField("_personalFolderOverrideSet", BindingFlags.Static | BindingFlags.NonPublic)!;
    private object? _previousFolder, _previousOverride;
    private string _directory = null!;
    private readonly List<UploaderInstance> _instances = [];
    private readonly List<string> _events = [];
    private TestClipboard _clipboard = null!;
    private TestUI _ui = null!;
    private TestSystem _system = null!;
    private UploaderInstance _shortener = null!;
    private UploaderInstance _uploader = null!;
    private static Func<string, UploadResult> _shorten = null!;
    private static Func<UploadResult> _upload = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-after-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousFolder = PersonalFolder.GetValue(null);
        _previousOverride = FolderOverride.GetValue(null);
        PathsManager.PersonalFolder = _directory;
        PlatformServices.Reset();
        _events.Clear();
        PlatformServices.Clipboard = _clipboard = new TestClipboard(_events);
        PlatformServices.System = _system = new TestSystem(_events);
        PlatformServices.RegisterUIService(_ui = new TestUI(_events));
        ProviderCatalog.RegisterProvider(new TestProvider());
        _uploader = AddInstance(UploaderCategory.Text);
        _shortener = AddInstance(UploaderCategory.UrlShortener);
        _shorten = url => { _events.Add("shorten:" + url); return new() { URL = url, ShortenedURL = ShortUrl }; };
        _upload = () => { _events.Add("upload"); return SuccessfulUpload(); };
    }

    [TearDown]
    public void TearDown()
    {
        PlatformServices.Reset();
        foreach (var instance in _instances) InstanceManager.Instance.RemoveInstance(instance.InstanceId);
        _instances.Clear();
        PersonalFolder.SetValue(null, _previousFolder);
        FolderOverride.SetValue(null, _previousOverride);
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task Upload_ShortenThenCopyOpenAndEncode_PreservesOriginalAndDeletionMetadata()
    {
        var info = NewUpload();
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(_events, Is.EqualTo(new[] { "upload", "shorten:" + OriginalUrl, "copy:" + ShortUrl,
            "open:" + ShortUrl, "qr:" + ShortUrl, "window" }));
        using var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var row = history.GetHistoryItem(info.HistoryItemId!.Value)!;
        Assert.Multiple(() =>
        {
            Assert.That(info.Result.URL, Is.EqualTo(OriginalUrl));
            Assert.That(info.Result.ShortenedURL, Is.EqualTo(ShortUrl));
            Assert.That(info.ResolvedUploaderInstanceId, Is.EqualTo(_uploader.InstanceId));
            Assert.That(info.UploaderHost, Is.EqualTo(_uploader.DisplayName));
            Assert.That(row.URL, Is.EqualTo(OriginalUrl));
            Assert.That(row.ShortenedURL, Is.EqualTo(ShortUrl));
            Assert.That(row.DeletionURL, Is.EqualTo("https://files.test/delete"));
            Assert.That(row.Tags[UploadRemoteDeletionService.UploaderInstanceIdTag], Is.EqualTo(_uploader.InstanceId));
            Assert.That(row.Tags[UploadRemoteDeletionService.UploadResultTagPrefix + "delete-token"], Is.EqualTo("token"));
            Assert.That(_ui.WindowInfo!.ShortenedUrl, Is.EqualTo(ShortUrl));
        });
    }

    [TestCase("empty")]
    [TestCase("error")]
    [TestCase("exception")]
    [TestCase("missing destination")]
    [TestCase("wrong category")]
    public async Task ShortenerFailure_KeepsSuccessfulUpload_AndContinuesWithOriginalUrl(string failure)
    {
        _shorten = url => failure switch
        {
            "exception" => throw new InvalidOperationException("shortener unavailable"),
            "error" => FailedShortening(url),
            _ => new UploadResult { URL = url, Response = "shortener unavailable" }
        };
        var info = NewUpload();
        if (failure == "missing destination") info.TaskSettings.UrlShortenerDestinationInstanceId = "does-not-exist";
        if (failure == "wrong category") info.TaskSettings.UrlShortenerDestinationInstanceId = _uploader.InstanceId;
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.Multiple(() =>
        {
            Assert.That(info.Result.IsSuccess, Is.True);
            Assert.That(info.Result.IsError, Is.False);
            Assert.That(info.Result.URL, Is.EqualTo(OriginalUrl));
            Assert.That(info.Result.ShortenedURL, Is.Null.Or.Empty);
            Assert.That(info.Result.Errors.Count, Is.EqualTo(1));
            Assert.That(_clipboard.Text, Is.EqualTo(OriginalUrl));
            Assert.That(_system.Url, Is.EqualTo(OriginalUrl));
            Assert.That(_ui.QrText, Is.EqualTo(OriginalUrl));
            Assert.That(_ui.WindowInfo!.ErrorDetails, Is.Not.Empty);
            Assert.That(_events.Count(e => e == "upload"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AutomaticTasks_RespectClipboardAndOpenFormats_QrEncodesResult()
    {
        var info = NewUpload();
        info.TaskSettings.AdvancedSettings.ClipboardContentFormat = "[$filenamenoext]($result) $thumbnailurl $deletionurl";
        info.TaskSettings.AdvancedSettings.OpenURLFormat = "https://viewer.test/?image=$url";
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(_clipboard.Text, Is.EqualTo($"[capture]({ShortUrl}) https://files.test/thumb.png https://files.test/delete"));
        Assert.That(_system.Url, Is.EqualTo("https://viewer.test/?image=" + OriginalUrl));
        Assert.That(_ui.QrText, Is.EqualTo(ShortUrl));
    }

    [TestCase(0, false)]
    [TestCase(31, false)]
    [TestCase(30, true)]
    public async Task AutomaticLengthThreshold_UsesStrictlyLongerThan(int limit, bool shortened)
    {
        var info = NewUpload(AllTasks & ~AfterUploadTasks.UseURLShortener);
        info.TaskSettings.AdvancedSettings.AutoShortenURLLength = limit;
        info.TaskSettings.AdvancedSettings.ClipboardContentFormat = string.Empty;
        info.TaskSettings.AdvancedSettings.OpenURLFormat = string.Empty;
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(info.Result.ShortenedURL, shortened ? Is.EqualTo(ShortUrl) : Is.Null.Or.Empty);
        Assert.That(_clipboard.Text, Is.EqualTo(shortened ? ShortUrl : OriginalUrl));
    }

    [Test]
    public async Task AfterUpload_PassesFtpResultsToTheShortenerLikeShareX()
    {
        const string url = "ftp://files.test/capture.zip";
        _upload = () => new() { URL = url, IsSuccess = true };
        var info = NewUpload();
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(_events, Does.Contain("shorten:" + url));
        Assert.That(info.Result.URL, Is.EqualTo(url));
        Assert.That(_clipboard.Text, Is.EqualTo(ShortUrl));
    }

    [Test]
    public async Task NoWorkflowShortener_UsesDefaultShortener()
    {
        var manager = InstanceManager.Instance;
        var previous = manager.GetDefaultInstance(UploaderCategory.UrlShortener);
        try
        {
            manager.SetDefaultInstance(UploaderCategory.UrlShortener, _shortener.InstanceId);
            var info = NewUpload();
            info.TaskSettings.UrlShortenerDestinationInstanceId = null;
            await new UploadJobProcessor().ProcessAsync(info, default);
            Assert.That(info.Result.ShortenedURL, Is.EqualTo(ShortUrl));
            Assert.That(_clipboard.Text, Is.EqualTo(ShortUrl));
        }
        finally
        {
            if (previous != null) manager.SetDefaultInstance(UploaderCategory.UrlShortener, previous.InstanceId);
        }
    }

    [Test]
    public async Task NoTasks_DoesNotShortenOrPerformSideEffects()
    {
        await new UploadJobProcessor().ProcessAsync(NewUpload(AfterUploadTasks.None), default);
        Assert.That(_events, Is.EqualTo(new[] { "upload" }));
    }

    [Test]
    public async Task StandaloneShortenJob_IsNotShortenedTwice()
    {
        var info = NewUpload();
        info.Job = TaskJob.ShortenURL;
        info.DataType = EDataType.URL;
        info.TaskSettings.Job = WorkflowType.ShortenURL;
        info.TaskSettings.AdvancedSettings.AutoShortenURLLength = 1;
        info.TextContent = OriginalUrl;
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(_events, Is.EqualTo(new[] { "shorten:" + OriginalUrl, "copy:" + ShortUrl,
            "open:" + ShortUrl, "qr:" + ShortUrl, "window" }));
    }

    [Test]
    public async Task CaptureHistory_IsUpdatedWithoutDuplicatingOrLosingAnnotations()
    {
        var info = NewUpload();
        info.Job = TaskJob.Job;
        info.DataType = EDataType.Image;
        info.TaskSettings.Job = WorkflowType.RectangleRegion;
        info.TaskSettings.AfterCaptureJob = AfterCaptureTasks.UploadImageToHost;
        info.Result = SuccessfulUpload();
        info.ResolvedUploaderInstanceId = _uploader.InstanceId;
        info.ResolvedUploaderHost = _uploader.DisplayName;
        info.Metadata.UploadURL = OriginalUrl;
        using var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var row = UploadJobProcessor.CreateHistoryItem(info, OriginalUrl);
        row.AnnotationSidecarPath = "/capture/image.xann";
        var date = row.DateTime;
        history.AppendHistoryItem(row);
        info.HistoryItemId = row.Id;
        await new UploadJobProcessor().ProcessAsync(info, default);
        var saved = history.GetHistoryItem(row.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(history.GetTotalCount(), Is.EqualTo(1));
            Assert.That(saved.ShortenedURL, Is.EqualTo(ShortUrl));
            Assert.That(saved.AnnotationSidecarPath, Is.EqualTo(row.AnnotationSidecarPath));
            Assert.That(saved.DateTime, Is.EqualTo(date));
            Assert.That(_events, Does.Not.Contain("upload"));
        });
    }

    [Test]
    public async Task BrowserFailure_DoesNotLoseUploadOrHistory_AndAppearsInAfterUploadWindow()
    {
        _system.Succeeds = false;
        var info = NewUpload();
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.Multiple(() =>
        {
            Assert.That(info.Result.IsSuccess, Is.True);
            Assert.That(info.HistoryItemId, Is.GreaterThan(0));
            Assert.That(info.Result.Errors.ToString(), Does.Contain("could not be opened"));
            Assert.That(_ui.WindowInfo!.ErrorDetails, Does.Contain("could not be opened"));
            Assert.That(_ui.QrText, Is.EqualTo(ShortUrl), "ShareX's browser helper does not stop later tasks.");
        });
    }

    [Test]
    public async Task InvalidOpenUrlFormat_IsNotSentToTheShell_AndStillShowsQrCode()
    {
        var info = NewUpload();
        info.TaskSettings.AdvancedSettings.OpenURLFormat = "/tmp/script.sh";
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(_system.Url, Is.Null);
        Assert.That(_ui.QrText, Is.EqualTo(ShortUrl));
        Assert.That(info.Result.Errors.ToString(), Does.Contain("Invalid URL"));
    }

    [Test]
    public void CancelDuringShortening_StopsFollowingActions_AndKeepsUploadedFileInHistory()
    {
        using var cancellation = new CancellationTokenSource();
        _shorten = url => { cancellation.Cancel(); return new() { URL = url, ShortenedURL = ShortUrl }; };
        var info = NewUpload();
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new UploadJobProcessor().ProcessAsync(info, cancellation.Token));
        Assert.That(_events, Is.EqualTo(new[] { "upload" }));
        Assert.That(info.Result.URL, Is.EqualTo(OriginalUrl));
        using var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.That(history.GetHistoryItem(info.HistoryItemId!.Value)!.URL, Is.EqualTo(OriginalUrl));
    }

    [Test]
    public async Task CancelledBeforeUpload_DoesNotRunAfterUploadTasks()
    {
        var info = NewUpload();
        info.UploadCancelled = true;
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(_events, Is.Empty);
    }

    [Test]
    public async Task FailedPrimaryUpload_DoesNotRunAfterUploadTasks()
    {
        _upload = () => { _events.Add("upload"); return new() { Response = "upload rejected" }; };
        var info = NewUpload();
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(info.Result.URL, Is.Null.Or.Empty);
        Assert.That(_events, Is.EqualTo(new[] { "upload" }));
    }

    [Test]
    public void UploadFormats_SupportShareXTokensAndExistingAliases_WithoutReparsingResults()
    {
        var result = SuccessfulUpload();
        result.ShortenedURL = "https://short.test/$filename";
        string pattern = "$result|$url|$shorturl|$thumbnailurl|$deletionurl|$filenamenoext|$filename|$filepath|$folderpath|$foldername|$thumbnailfilenamenoext|$thumbnailfilename|$uploadtime";
        Assert.That(UploadInfoParser.Parse(pattern, result, "image.png", "/captures/image.png", "/captures/image-thumb.png", 125),
            Is.EqualTo($"https://short.test/$filename|{OriginalUrl}|https://short.test/$filename|https://files.test/thumb.png|https://files.test/delete|image|image.png|/captures/image.png|/captures|captures|image-thumb|image-thumb.png|125"));
        Assert.That(UploadInfoParser.Parse("$SHORTENED|$thumbnail|$deletion", result, null, null),
            Is.EqualTo("https://short.test/$filename|https://files.test/thumb.png|https://files.test/delete"));
        Assert.That(UploadInfoParser.Parse("$result", new(), null, "/captures/local.png"), Is.EqualTo("/captures/local.png"));
    }

    [Test]
    public void RecordingHistory_PreservesShortenedUrlAndRemoteDeletionMetadata()
    {
        var info = NewUpload();
        info.Result = SuccessfulUpload();
        info.Result.ShortenedURL = ShortUrl;
        info.Metadata.UploadURL = OriginalUrl;
        info.ResolvedUploaderInstanceId = _uploader.InstanceId;
        var history = WorkerTask.CreateRecordingHistoryItem(info, "/recordings/capture.mp4");
        Assert.Multiple(() =>
        {
            Assert.That(history.Type, Is.EqualTo("Video"));
            Assert.That(history.URL, Is.EqualTo(OriginalUrl));
            Assert.That(history.ShortenedURL, Is.EqualTo(ShortUrl));
            Assert.That(history.DeletionURL, Is.EqualTo("https://files.test/delete"));
            Assert.That(history.Tags[UploadRemoteDeletionService.UploaderInstanceIdTag], Is.EqualTo(_uploader.InstanceId));
        });
    }

    private TaskInfo NewUpload(AfterUploadTasks tasks = AllTasks)
    {
        var settings = new TaskSettings
        {
            Job = WorkflowType.UploadText, AfterCaptureJob = AfterCaptureTasks.None, AfterUploadJob = tasks,
            DestinationInstanceId = _uploader.InstanceId, UrlShortenerDestinationInstanceId = _shortener.InstanceId
        };
        settings.AdvancedSettings.TextTaskSaveAsFile = false;
        settings.AdvancedSettings.UseAfterCaptureTasksDuringFileUpload = false;
        var info = new TaskInfo(settings) { Job = TaskJob.TextUpload, DataType = EDataType.Text, TextContent = "example" };
        info.SetFileName("capture.txt");
        return info;
    }

    private UploaderInstance AddInstance(UploaderCategory category)
    {
        var instance = new UploaderInstance { ProviderId = ProviderId, Category = category,
            DisplayName = "Test " + category, SettingsJson = category.ToString() };
        InstanceManager.Instance.AddInstance(instance);
        _instances.Add(instance);
        return instance;
    }

    private static UploadResult SuccessfulUpload() => new()
    {
        URL = OriginalUrl, IsSuccess = true, ThumbnailURL = "https://files.test/thumb.png", DeletionURL = "https://files.test/delete",
        Metadata = new() { ["delete-token"] = "token" }
    };

    private static UploadResult FailedShortening(string url)
    {
        var result = new UploadResult { URL = url, ShortenedURL = ShortUrl };
        result.Errors.Add("shortener unavailable");
        return result;
    }

    private sealed class TestProvider : UploaderProviderBase
    {
        public override string ProviderId => AfterUploadTasksTests.ProviderId;
        public override string Name => "After upload tests";
        public override string Description => "Test provider";
        public override Version Version => new(1, 0);
        public override UploaderCategory[] SupportedCategories => [UploaderCategory.Text, UploaderCategory.UrlShortener];
        public override Type ConfigModelType => typeof(object);
        public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new()
            { [UploaderCategory.Text] = ["*"], [UploaderCategory.UrlShortener] = [] };
        public override Uploader CreateInstance(string settingsJson) => settingsJson == nameof(UploaderCategory.UrlShortener)
            ? new TestShortener() : new TestUploader();
    }
    private sealed class TestShortener : UrlShortener
    {
        public override UploadResult ShortenURL(string url) => _shorten(url);
    }
    private sealed class TestUploader : FileUploader
    {
        public override UploadResult Upload(Stream stream, string fileName) => _upload();
    }
    private sealed class TestUI(List<string> events) : XerahS.CLI.Services.HeadlessUIService, IUIService
    {
        public string? QrText { get; private set; }
        public AfterUploadWindowInfo? WindowInfo { get; private set; }
        public Task ShowQrCodeAsync(string text, CancellationToken cancellationToken = default)
        { QrText = text; events.Add("qr:" + text); return Task.CompletedTask; }
        public new Task ShowAfterUploadWindowAsync(AfterUploadWindowInfo info)
        { WindowInfo = info; events.Add("window"); return Task.CompletedTask; }
    }
    private sealed class TestSystem(List<string> events) : ISystemService
    {
        public string? Url { get; private set; }
        public bool Succeeds { get; set; } = true;
        public bool OpenUrl(string url) { Url = url; events.Add("open:" + url); return Succeeds; }
        public bool IsDesktopWallpaperSupported => false;
        public bool ShowFileInExplorer(string filePath) => false;
        public bool OpenFile(string filePath) => false;
        public bool TryGetDesktopWallpaper(out DesktopWallpaperInfo? wallpaper) { wallpaper = null; return false; }
        public bool TryGetDesktopWallpaperPath(out string? path) { path = null; return false; }
    }
    private sealed class TestClipboard(List<string> events) : IClipboardService
    {
        public string? Text { get; private set; }
        public void Clear() => Text = null;
        public bool ContainsText() => Text != null;
        public bool ContainsImage() => false;
        public bool ContainsFileDropList() => false;
        public string? GetText() => Text;
        public void SetText(string text) { Text = text; events.Add("copy:" + text); }
        public SKBitmap? GetImage() => null;
        public void SetImage(SKBitmap image) { }
        public string[]? GetFileDropList() => null;
        public void SetFileDropList(string[] files) { }
        public object? GetData(string format) => null;
        public void SetData(string format, object data) { }
        public bool ContainsData(string format) => false;
        public Task<string?> GetTextAsync() => Task.FromResult(Text);
        public Task SetTextAsync(string text) { SetText(text); return Task.CompletedTask; }
    }
}
