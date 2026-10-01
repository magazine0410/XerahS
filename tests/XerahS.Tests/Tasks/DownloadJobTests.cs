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

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using NUnit.Framework;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks.Processors;
using XerahS.Platform.Abstractions;
using XerahS.Uploaders;

namespace XerahS.Tests.Tasks;

[TestFixture]
[NonParallelizable]
public class DownloadJobTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => Directory.CreateDirectory(_directory = Path.Combine(Path.GetTempPath(), "xerahs-download-tests-" + Guid.NewGuid().ToString("N")));

    [TearDown]
    public void TearDown()
    {
        UploadCancellationScope.CancelAll();
        PlatformServices.Reset();
        Directory.Delete(_directory, true);
    }

    [TestCase("photo.png", EDataType.Image)]
    [TestCase("note.txt", EDataType.Text)]
    [TestCase("archive.zip", EDataType.File)]
    public async Task Download_RetainsServerNamedFileAndClassifiesIt(string name, EDataType type)
    {
        var info = CreateInfo();
        using var response = Response(name);
        using var client = new HttpClient(new Handler(response));
        Assert.That(await new DownloadJobProcessor(client).ProcessAsync(info, CancellationToken.None), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(info.FilePath, Is.EqualTo(Path.Combine(_directory, name)));
            Assert.That(info.FileName, Is.EqualTo(name));
            Assert.That(File.ReadAllText(info.FilePath), Is.EqualTo("downloaded content"));
            Assert.That(info.DataType, Is.EqualTo(type));
            Assert.That(info.TextContent, Is.Null, "The source URL must never be uploaded as text.");
            Assert.That(Directory.GetFiles(_directory, "*.partial"), Is.Empty);
        });
    }

    [TestCase(FileExistAction.Overwrite)]
    [TestCase(FileExistAction.UniqueName)]
    [TestCase(FileExistAction.Cancel)]
    public async Task Download_UsesWorkflowCollisionPolicy(FileExistAction action)
    {
        string path = Path.Combine(_directory, "existing.txt");
        File.WriteAllText(path, "original");
        var info = CreateInfo(action);
        using var response = Response("existing.txt");
        using var client = new HttpClient(new Handler(response));
        bool downloaded = await new DownloadJobProcessor(client).ProcessAsync(info, CancellationToken.None);
        Assert.That(downloaded, Is.EqualTo(action != FileExistAction.Cancel));
        Assert.That(File.ReadAllText(path), Is.EqualTo(action == FileExistAction.Overwrite ? "downloaded content" : "original"));
        if (action == FileExistAction.UniqueName)
        {
            Assert.That(info.FilePath, Is.Not.EqualTo(path));
            Assert.That(File.ReadAllText(info.FilePath), Is.EqualTo("downloaded content"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AskPolicy_UsesTheDialogResolutionOrCancels(bool accept)
    {
        var ui = new ConflictUI { Resolution = accept ? new(Path.Combine(_directory, "chosen.txt"), false) : null };
        PlatformServices.RegisterUIService(ui);
        string existing = Path.Combine(_directory, "existing.txt");
        File.WriteAllText(existing, "original");
        using var response = Response("existing.txt");
        using var client = new HttpClient(new Handler(response));
        var info = CreateInfo(FileExistAction.Ask);
        Assert.That(await new DownloadJobProcessor(client).ProcessAsync(info, CancellationToken.None), Is.EqualTo(accept));
        Assert.That(ui.RequestedPath, Is.EqualTo(existing));
        Assert.That(File.ReadAllText(existing), Is.EqualTo("original"));
        if (accept) Assert.That(File.ReadAllText(info.FilePath), Is.EqualTo("downloaded content"));
    }

    [Test]
    public void FailedOrTruncatedResponse_PreservesExistingFileAndRemovesPartial()
    {
        string existing = Path.Combine(_directory, "existing.txt");
        File.WriteAllText(existing, "original");
        using var response = Response("existing.txt");
        response.Content.Headers.ContentLength = 1000;
        using var client = new HttpClient(new Handler(response));
        var info = CreateInfo(FileExistAction.Overwrite);
        Assert.ThrowsAsync<IOException>(() => new DownloadJobProcessor(client).ProcessAsync(info, CancellationToken.None));
        Assert.That(File.ReadAllText(existing), Is.EqualTo("original"));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        Assert.That(info.FilePath, Is.Empty);
    }

    [Test]
    public void HttpError_DoesNotSaveOrUploadErrorPage()
    {
        using var response = Response("error.html");
        response.StatusCode = HttpStatusCode.NotFound;
        using var client = new HttpClient(new Handler(response));
        Assert.ThrowsAsync<HttpRequestException>(() => new DownloadJobProcessor(client).ProcessAsync(CreateInfo(), CancellationToken.None));
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [TestCase("ftp://example.test/file.txt")]
    [TestCase("file:///etc/hosts")]
    [TestCase("not a URL")]
    public void InvalidUrl_IsRejectedBeforeSending(string url)
    {
        var info = CreateInfo();
        info.TextContent = url;
        using var response = Response("file.txt");
        using var client = new HttpClient(new Handler(response));
        Assert.ThrowsAsync<ArgumentException>(() => new DownloadJobProcessor(client).ProcessAsync(info, CancellationToken.None));
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public void FileName_UsesUtf8HeaderAndRemovesServerPath_OrUsesWorkflowPattern()
    {
        using var response = Response("fallback.txt");
        response.Content.Headers.ContentDisposition!.FileNameStar = "../../café.txt";
        var uri = new Uri("https://example.test/original.png?ignored=yes");
        var settings = CreateInfo().TaskSettings;
        Assert.That(DownloadJobProcessor.GetFileName(uri, response, settings), Is.EqualTo("café.txt"));
        settings.UploadSettings.FileUploadUseNamePattern = true;
        settings.UploadSettings.NameFormatPattern = "custom-download";
        Assert.That(DownloadJobProcessor.GetFileName(uri, response, settings), Is.EqualTo("custom-download.png"));
        Assert.That(DownloadJobProcessor.GetFileName(new Uri("https://example.test/archive.tar.gz"), response, settings), Is.EqualTo("custom-download.tar.gz"));
        Assert.That(DownloadJobProcessor.GetFileName(new Uri("https://example.test/"), response, settings), Is.EqualTo("custom-download"));
    }

    private TaskInfo CreateInfo(FileExistAction action = FileExistAction.UniqueName)
    {
        var settings = new TaskSettings { Job = WorkflowType.UploadURL, OverrideScreenshotsFolder = true, ScreenshotsFolder = _directory };
        settings.ImageSettings.FileExistAction = action;
        return new TaskInfo(settings) { Job = TaskJob.DownloadUpload, TextContent = "https://example.test/download" };
    }

    private static HttpResponseMessage Response(string fileName) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("downloaded content")
        {
            Headers = { ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName } }
        }
    };

    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class ConflictUI : XerahS.CLI.Services.HeadlessUIService, IUIService
    {
        public FileConflictResolution? Resolution { get; init; }
        public string? RequestedPath { get; private set; }
        public Task<FileConflictResolution?> ResolveFileConflictAsync(string filePath, CancellationToken cancellationToken = default)
        {
            RequestedPath = filePath;
            return Task.FromResult(Resolution);
        }
    }
}
