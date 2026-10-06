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

using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using SkiaSharp;
using XerahS.App;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.Services.Abstractions;

namespace XerahS.Tests.Helpers;

[TestFixture, NonParallelizable]
public class DesktopIntegrationTests
{
    private static byte[] Frame(string json)
    {
        byte[] data = Encoding.UTF8.GetBytes(json);
        return BitConverter.GetBytes(data.Length).Concat(data).ToArray();
    }

    [Test]
    public void NativeProtocol_HandlesFragmentedHeaderAndUnicode()
    {
        const string json = "{\"Text\":\"hei ä 世界\"}";
        using var input = new FragmentedStream(Frame(json));
        Assert.That(NativeMessagingHost.Read(input), Is.EqualTo(json));
        Assert.That(NativeMessagingHost.Read(input), Is.Null);
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(67_108_865)]
    public void NativeProtocol_RejectsInvalidSizeWithoutReadingPayload(int size)
    {
        using var input = new MemoryStream(BitConverter.GetBytes(size));
        Assert.Throws<InvalidDataException>(() => NativeMessagingHost.Read(input));
    }

    [Test]
    public void NativeProtocol_RejectsTruncatedInput()
    {
        using var input = new MemoryStream(new byte[] { 5, 0, 0, 0, 65 });
        Assert.Throws<EndOfStreamException>(() => NativeMessagingHost.Read(input));
    }

    [Test]
    public void NativeHost_ForwardsMultipleRequestsAndBoundsLargeReply()
    {
        string large = JsonConvert.SerializeObject(new { Text = new string('x', NativeMessagingHost.MaximumOutputBytes) });
        using var input = new MemoryStream(Frame("{\"Action\":4,\"Text\":\"test\"}").Concat(Frame(large)).ToArray());
        using var output = new MemoryStream();
        var forwarded = new List<string>();
        BrowserNativeMessagingHost.Run(input, output, forwarded.Add);
        Assert.That(forwarded.Count, Is.EqualTo(2));
        output.Position = 0;
        Assert.That(NativeMessagingHost.Read(output), Does.Contain("test"));
        Assert.That(NativeMessagingHost.Read(output), Is.EqualTo("{\"success\":true}"));
        Assert.That(NativeMessagingHost.Read(output), Is.Null);
    }

    [Test]
    public void NativeHost_ReportsInvalidJsonAndForwardingFailure()
    {
        using var input = new MemoryStream(Frame("bad json").Concat(Frame("{}" )).ToArray());
        using var output = new MemoryStream();
        BrowserNativeMessagingHost.Run(input, output, _ => throw new IOException("Cannot start"));
        output.Position = 0;
        Assert.That(NativeMessagingHost.Read(output), Does.Contain("error"));
        Assert.That(NativeMessagingHost.Read(output), Does.Contain("Cannot start"));
    }

    [Test]
    public void Arguments_ImportDefinitionsAndEditImagesInsteadOfUploadingThem()
    {
        string[] left = IntegrationArguments.Extract(new[] { "/tmp/plain.txt", "/tmp/a.sxcu", "file:///tmp/effect%20one.sxie", "-ImageEditor", "/tmp/one.png", "/tmp/two.png" }, out var requests);
        Assert.That(left, Is.EqualTo(new[] { "/tmp/plain.txt" }));
        Assert.That(requests.Select(r => r.Action), Is.EqualTo(new[] { IntegrationAction.CustomUploader, IntegrationAction.ImageEffect, IntegrationAction.ImageEditor, IntegrationAction.ImageEditor }));
        Assert.That(requests[1].Path, Is.EqualTo("/tmp/effect one.sxie"));
        Assert.That(IntegrationArguments.Extract(new[] { "-CustomUploader", "/tmp/missing.json" }, out var invalid), Is.Empty);
        Assert.That(invalid.Single().Action, Is.EqualTo(IntegrationAction.CustomUploader));
    }

    [Test]
    public void FolderUploads_RecurseAndDeduplicate_WithoutFollowingDirectorySymlinks()
    {
        string root = Path.Combine(Path.GetTempPath(), "xerahs-upload-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        string file = Path.Combine(root, "nested", "file.txt");
        File.WriteAllText(file, "test");
        try
        {
            if (OperatingSystem.IsLinux()) Directory.CreateSymbolicLink(Path.Combine(root, "loop"), root);
            Assert.That(IntegrationArguments.ExpandUploadPaths(new[] { file }, new[] { root }), Is.EqualTo(new[] { file }));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase(BrowserExtensionAction.UploadText, WorkflowType.UploadText)]
    [TestCase(BrowserExtensionAction.None, WorkflowType.UploadURL)]
    [TestCase(BrowserExtensionAction.UploadVideo, WorkflowType.UploadURL)]
    [TestCase(BrowserExtensionAction.UploadAudio, WorkflowType.UploadURL)]
    [TestCase(BrowserExtensionAction.ShortenURL, WorkflowType.ShortenURL)]
    public async Task BrowserActions_SelectTheRightWorkflow(BrowserExtensionAction action, WorkflowType expected)
    {
        var manager = new RecordingTaskManager();
        await BrowserExtensionService.ProcessAsync(JsonConvert.SerializeObject(new { Action = action, URL = "https://example.com/a", Text = "hello" }), manager, new TaskSettings());
        Assert.That(manager.Job, Is.EqualTo(expected));
        Assert.That(manager.Text, Is.EqualTo(action == BrowserExtensionAction.UploadText ? "hello" : "https://example.com/a"));
    }

    [Test]
    public async Task BrowserImage_DecodesDataUrlAndRunsAfterCaptureWorkflow()
    {
        using var bitmap = new SKBitmap(12, 8);
        bitmap.Erase(SKColors.Red);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var manager = new RecordingTaskManager();
        await BrowserExtensionService.ProcessAsync(JsonConvert.SerializeObject(new { Action = 1, URL = "data:image/png;base64," + Convert.ToBase64String(data.ToArray()) }), manager, new TaskSettings());
        Assert.That(manager.Job, Is.EqualTo(WorkflowType.PrintScreen));
        Assert.That(manager.ImageSize, Is.EqualTo(new SKSizeI(12, 8)));
    }

    [TestCase("file:///tmp/private-file")]
    [TestCase("javascript:alert(1)")]
    public void BrowserActions_RejectNonWebUrls(string url)
    {
        var manager = new RecordingTaskManager();
        Assert.ThrowsAsync<InvalidDataException>(() => BrowserExtensionService.ProcessAsync(JsonConvert.SerializeObject(new { Action = 2, URL = url }), manager, new TaskSettings()));
        Assert.That(manager.Job, Is.Null);
    }

    private sealed class RecordingTaskManager : ITaskManager
    {
        public WorkflowType? Job { get; private set; }
        public string? Text { get; private set; }
        public SKSizeI ImageSize { get; private set; }
        public Task StartTask(object? taskSettings, SKBitmap? inputImage = null)
        {
            Job = ((TaskSettings)taskSettings!).Job;
            if (inputImage != null) { ImageSize = new SKSizeI(inputImage.Width, inputImage.Height); inputImage.Dispose(); }
            return Task.CompletedTask;
        }
        public Task StartTextTask(object? settings, string text) { Text = text; return StartTask(settings); }
        public Task StartFileTask(object? settings, string path) => throw new NotSupportedException();
        public Task StartImageUploadTask(object? settings, SKBitmap image) => StartTask(settings, image);
        public void StopAllTasks() { }
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);
    }
}
