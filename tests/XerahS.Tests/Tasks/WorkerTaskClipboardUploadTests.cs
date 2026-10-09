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

using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Platform.Abstractions;
using XerahS.Services.Abstractions;

namespace XerahS.Tests.Tasks;

/// <summary>ShareX's clipboard upload: copied text, folder paths, and files.</summary>
[TestFixture]
[NonParallelizable]
public sealed class WorkerTaskClipboardUploadTests
{
    private string _directory = null!;
    private FakeClipboard _clipboard = null!;
    private ITaskManager _previousTaskManager = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-clipboard-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        PlatformServices.Reset();
        _clipboard = new FakeClipboard();
        PlatformServices.Clipboard = _clipboard;
        _previousTaskManager = XerahS.Core.Helpers.TaskHelpers.TaskManagerService;
    }

    [TearDown]
    public void TearDown()
    {
        XerahS.Core.Helpers.TaskHelpers.TaskManagerService = _previousTaskManager;
        WorkerTask.ConfirmMultiUploadCallback = null;
        PlatformServices.Reset();
        Directory.Delete(_directory, recursive: true);
    }

    private static TaskSettings Settings() => new() { Job = WorkflowType.ClipboardUpload, UseDefaultAdvancedSettings = false };

    private WorkerTask Load(TaskSettings settings)
    {
        var worker = WorkerTask.Create(settings);
        Assert.That(worker.TryLoadClipboardContent(worker.Info.TaskSettings, worker.Info.Metadata, out _), Is.True);
        return worker;
    }

    [Test]
    public void CopiedText_GoesThroughTheCustomTextTemplate()
    {
        var settings = Settings();
        settings.AdvancedSettings.TextCustom = "<b>%input</b>";
        _clipboard.SetText("a & b");

        using var worker = Load(settings);

        Assert.That(worker.Info.Job, Is.EqualTo(TaskJob.TextUpload));
        Assert.That(worker.Info.TextContent, Is.EqualTo("<b>a &amp; b</b>"), "HTML-encoded, as TextCustomEncodeInput is on by default.");
    }

    [Test]
    public void CopiedURL_HandledByAClipboardURLOption_SkipsTheTemplate()
    {
        var settings = Settings();
        settings.AdvancedSettings.TextCustom = "<b>%input</b>";
        settings.UploadSettings.ClipboardUploadShortenURL = true;
        _clipboard.SetText(" https://example.test/long ");

        using var worker = Load(settings);

        Assert.That(worker.Info.Job, Is.EqualTo(TaskJob.ShortenURL));
        Assert.That(worker.Info.TextContent, Is.EqualTo("https://example.test/long"));
    }

    [Test]
    public void CopiedFolderPath_IsIndexed_AndUploadedAsText()
    {
        File.WriteAllText(Path.Combine(_directory, "alpha.txt"), "a");
        var settings = Settings();
        settings.UploadSettings.ClipboardUploadAutoIndexFolder = true;
        settings.AdvancedSettings.TextCustom = "<b>%input</b>";
        _clipboard.SetText(_directory);

        using var worker = Load(settings);

        Assert.Multiple(() =>
        {
            Assert.That(worker.Info.Job, Is.EqualTo(TaskJob.TextUpload));
            Assert.That(worker.Info.DataType, Is.EqualTo(EDataType.Text));
            Assert.That(worker.Info.TextContent, Does.Contain("alpha.txt").And.Contain("<html"), "The index, in the default HTML format.");
            Assert.That(Path.GetExtension(worker.Info.FileName), Is.EqualTo(".html"));
        });
    }

    [Test]
    public void CopiedFolderPath_IsUploadedAsTextWithTheOptionOff()
    {
        _clipboard.SetText(_directory);

        using var worker = Load(Settings());

        Assert.That(worker.Info.TextContent, Is.EqualTo(_directory));
        Assert.That(Path.GetExtension(worker.Info.FileName), Is.EqualTo(".txt"));
    }

    [Test]
    public async Task CopiedFiles_AreUploadedByATaskEach_StartedTogether()
    {
        var manager = new PendingTaskManager();
        XerahS.Core.Helpers.TaskHelpers.TaskManagerService = manager;
        string[] files = CreateFiles(3);
        var settings = Settings();
        using var worker = WorkerTask.Create(settings);

        var upload = worker.UploadClipboardFilesAsync(settings, files, default);

        Assert.That(manager.Started.Select(start => start.FilePath), Is.EqualTo(files), "Every file starts before the first one ends.");
        Assert.That(upload.IsCompleted, Is.False);
        manager.CompleteAll();
        await upload.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(manager.Started.Select(start => start.Settings.Job), Is.All.EqualTo(WorkflowType.FileUpload));
            Assert.That(manager.Started.Select(start => start.Settings).Distinct().Count(), Is.EqualTo(3), "Each task has its own settings.");
            Assert.That(worker.Info.SuppressCompletionNotification, Is.True, "Only the file tasks notify.");
            Assert.That(worker.Info.FilePath, Is.Empty);
        });
    }

    [TestCase(false, 0)]
    [TestCase(true, 11)]
    public async Task CopiedFiles_AskBeforeUploadingMoreThanTen(bool confirmed, int expected)
    {
        var manager = new PendingTaskManager { CompleteImmediately = true };
        XerahS.Core.Helpers.TaskHelpers.TaskManagerService = manager;
        var counts = new List<int>();
        WorkerTask.ConfirmMultiUploadCallback = count => { counts.Add(count); return Task.FromResult(confirmed); };
        var settings = Settings();
        using var worker = WorkerTask.Create(settings);

        await worker.UploadClipboardFilesAsync(settings, CreateFiles(11), default);

        Assert.That(counts, Is.EqualTo(new[] { 11 }));
        Assert.That(manager.Started, Has.Count.EqualTo(expected));
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    public void CopiedImageFile_IsLoadedAsAnImage_WithProcessImagesDuringFileUpload(bool process, bool expected)
    {
        string image = Path.Combine(_directory, "copied.png");
        File.WriteAllBytes(image, [1]);
        var settings = Settings();
        settings.AdvancedSettings.ProcessImagesDuringFileUpload = process;
        _clipboard.SetFileDropList([image]);

        using var worker = Load(settings);

        Assert.That(worker.Info.Job, Is.EqualTo(TaskJob.FileUpload));
        Assert.That(worker.Info.LoadImageFromFile, Is.EqualTo(expected));
    }

    private string[] CreateFiles(int count)
    {
        return Enumerable.Range(1, count).Select(i =>
        {
            string path = Path.Combine(_directory, $"file{i:00}.txt");
            File.WriteAllText(path, i.ToString());
            return path;
        }).ToArray();
    }

    private sealed class PendingTaskManager : ITaskManager
    {
        private readonly List<TaskCompletionSource> _pending = [];
        public List<(TaskSettings Settings, string FilePath)> Started { get; } = [];
        public bool CompleteImmediately { get; init; }

        public Task StartFileTask(object? taskSettings, string filePath)
        {
            Started.Add(((TaskSettings)taskSettings!, filePath));
            if (CompleteImmediately) return Task.CompletedTask;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(completion);
            return completion.Task;
        }

        public void CompleteAll() => _pending.ForEach(completion => completion.TrySetResult());
        public Task StartTask(object? taskSettings, SKBitmap? inputImage = null) => throw new NotSupportedException();
        public Task StartImageUploadTask(object? taskSettings, SKBitmap image) => throw new NotSupportedException();
        public Task StartTextTask(object? taskSettings, string text) => throw new NotSupportedException();
        public void StopAllTasks() { }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        private string? _text;
        private string[]? _files;
        public void Clear() { _text = null; _files = null; }
        public bool ContainsText() => _text != null;
        public bool ContainsImage() => false;
        public bool ContainsFileDropList() => _files != null;
        public string? GetText() => _text;
        public void SetText(string text) => _text = text;
        public SKBitmap? GetImage() => null;
        public void SetImage(SKBitmap image) { }
        public string[]? GetFileDropList() => _files;
        public void SetFileDropList(string[] files) => _files = files;
        public object? GetData(string format) => null;
        public void SetData(string format, object data) { }
        public bool ContainsData(string format) => false;
        public Task<string?> GetTextAsync() => Task.FromResult(_text);
        public Task SetTextAsync(string text) { _text = text; return Task.CompletedTask; }
    }
}
