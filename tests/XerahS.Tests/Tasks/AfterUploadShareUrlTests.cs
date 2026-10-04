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
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks.Processors;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;
using XerahS.Uploaders.SharingServices;

namespace XerahS.Tests.Tasks;

// ShareX's Share URL after-upload task and Share URL job (DoAfterUploadJobs and CreateShareURLTask).
public partial class AfterUploadTasksTests
{
    private const string SharingProviderId = "after-upload-test-sharing";
    private static Func<string, UploadResult> _share = null!;
    private Func<string, bool> _previousOpenUrl = null!;

    [SetUp]
    public void SetUpSharing()
    {
        _previousOpenUrl = UrlSharingHost.OpenUrl;
        UrlSharingHost.OpenUrl = url => { _events.Add("browser:" + url); return true; };
        ProviderCatalog.RegisterProvider(new TestSharingProvider());
        _share = url => { _events.Add("share:" + url); return new UploadResult { URL = url, IsURLExpected = false }; };
    }

    [TearDown]
    public void TearDownSharing() => UrlSharingHost.OpenUrl = _previousOpenUrl;

    [Test]
    public async Task ShareURL_SharesTheShortenedUrl_AfterShorteningAndBeforeCopying()
    {
        var info = NewUpload(AfterUploadTasks.UseURLShortener | AfterUploadTasks.ShareURL | AfterUploadTasks.CopyURLToClipboard);
        info.TaskSettings.UrlSharingDestinationInstanceId = AddSharingInstance().InstanceId;
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.Multiple(() =>
        {
            Assert.That(_events, Is.EqualTo(new[] { "upload", "shorten:" + OriginalUrl, "share:" + ShortUrl, "copy:" + ShortUrl }));
            Assert.That(info.Result.Errors.Count, Is.Zero);
            Assert.That(info.Result.IsURLExpected, Is.True, "Only a Share URL job stops expecting a URL.");
        });
    }

    [Test]
    public async Task ShareURL_Failure_KeepsTheUploadAndRunsTheRemainingTasks()
    {
        _share = url =>
        {
            var failed = new UploadResult { URL = url };
            failed.Errors.Add("sharing service unavailable");
            return failed;
        };
        var info = NewUpload(AfterUploadTasks.ShareURL | AfterUploadTasks.CopyURLToClipboard | AfterUploadTasks.ShowAfterUploadWindow);
        info.TaskSettings.UrlSharingDestinationInstanceId = AddSharingInstance().InstanceId;
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.Multiple(() =>
        {
            Assert.That(info.Result.IsError, Is.False);
            Assert.That(info.Result.Errors.ToString(), Does.Contain("sharing service unavailable"));
            Assert.That(_clipboard.Text, Is.EqualTo(OriginalUrl));
            Assert.That(_ui.WindowInfo!.ErrorDetails, Does.Contain("sharing service unavailable"));
            Assert.That(info.HistoryItemId, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ShareURL_WithoutAService_SaysWhereToChooseOne()
    {
        var info = NewUpload(AfterUploadTasks.ShareURL);
        info.TaskSettings.UrlSharingDestinationInstanceId = null;
        Assume.That(InstanceManager.Instance.GetDefaultInstance(UploaderCategory.UrlSharing), Is.Null);
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(info.Result.Errors.ToString(), Does.Contain("No URL sharing service is selected"));
    }

    [Test]
    public async Task ShareURL_UsesTheDefaultServiceWhenTheWorkflowHasNone()
    {
        var manager = InstanceManager.Instance;
        var previous = manager.GetDefaultInstance(UploaderCategory.UrlSharing);
        try
        {
            manager.SetDefaultInstance(UploaderCategory.UrlSharing, AddSharingInstance().InstanceId);
            var info = NewUpload(AfterUploadTasks.ShareURL);
            await new UploadJobProcessor().ProcessAsync(info, default);
            Assert.That(_events, Does.Contain("share:" + OriginalUrl));
        }
        finally
        {
            if (previous != null) manager.SetDefaultInstance(UploaderCategory.UrlSharing, previous.InstanceId);
        }
    }

    [Test]
    public async Task ShareURL_BuiltInLinkService_OpensItsSharePageWithTheEncodedUrl()
    {
        ProviderCatalog.InitializeBuiltInProviders();
        var info = NewUpload(AfterUploadTasks.ShareURL);
        info.TaskSettings.UrlSharingDestinationInstanceId = AddSharingInstance("share-reddit").InstanceId;
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.That(_events, Does.Contain("browser:https://www.reddit.com/submit?url=" + URLHelpers.URLEncode(OriginalUrl)));
        Assert.That(info.Result.Errors.Count, Is.Zero);
    }

    [Test]
    public async Task ShareJob_SharesTheUrl_WithoutTheAfterUploadWindow_AndIsSavedToHistory()
    {
        var info = NewShareJob(AfterUploadTasks.CopyURLToClipboard | AfterUploadTasks.ShowAfterUploadWindow | AfterUploadTasks.UseURLShortener);
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.Multiple(() =>
        {
            Assert.That(_events, Is.EqualTo(new[] { "share:https://example.test/page", "copy:https://example.test/page" }),
                "As in ShareX, a Share URL job is not shortened and shows no after-upload window.");
            Assert.That(info.Result.IsURLExpected, Is.False);
            Assert.That(info.Result.IsError, Is.False);
            Assert.That(info.HistoryItemId, Is.GreaterThan(0));
            Assert.That(info.UploaderHost, Is.EqualTo("Test sharing"));
        });
    }

    [Test]
    public async Task ShareJob_Failure_IsAFailedTask_AndIsNotSavedToHistory()
    {
        _share = url =>
        {
            var failed = new UploadResult { URL = url };
            failed.Errors.Add("rejected");
            return failed;
        };
        var info = NewShareJob(AfterUploadTasks.None);
        await new UploadJobProcessor().ProcessAsync(info, default);
        Assert.Multiple(() =>
        {
            Assert.That(info.Result.IsError, Is.True);
            Assert.That(info.Result.Errors.ToString(), Does.Contain("rejected"));
            Assert.That(info.HistoryItemId, Is.Null);
        });
    }

    [Test]
    public void ShareJob_ShowsNoCompletionNotification()
    {
        var info = NewShareJob(AfterUploadTasks.None);
        info.TaskSettings.GeneralSettings.ShowToastNotificationAfterTaskCompleted = true;
        Assert.That(XerahS.UI.Services.WorkflowOrchestrator.ShouldShowCompletionNotification(info), Is.False);
        info.Job = TaskJob.TextUpload;
        Assert.That(XerahS.UI.Services.WorkflowOrchestrator.ShouldShowCompletionNotification(info), Is.True);
    }

    private TaskInfo NewShareJob(AfterUploadTasks tasks)
    {
        var settings = new TaskSettings
        {
            Job = WorkflowType.ClipboardUpload, AfterCaptureJob = AfterCaptureTasks.None, AfterUploadJob = tasks,
            UrlShortenerDestinationInstanceId = _shortener.InstanceId,
            UrlSharingDestinationInstanceId = AddSharingInstance().InstanceId
        };
        return new TaskInfo(settings) { Job = TaskJob.ShareURL, DataType = EDataType.URL, TextContent = " https://example.test/page " };
    }

    private UploaderInstance AddSharingInstance(string providerId = SharingProviderId)
    {
        var instance = new UploaderInstance
        {
            ProviderId = providerId, Category = UploaderCategory.UrlSharing, DisplayName = "Test sharing", SettingsJson = "{}"
        };
        InstanceManager.Instance.AddInstance(instance);
        _instances.Add(instance);
        return instance;
    }

    private sealed class TestSharingProvider : UploaderProviderBase
    {
        public override string ProviderId => SharingProviderId;
        public override string Name => "Sharing tests";
        public override string Description => "Test sharing service";
        public override Version Version => new(1, 0);
        public override UploaderCategory[] SupportedCategories => [UploaderCategory.UrlSharing];
        public override Type ConfigModelType => typeof(object);
        public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new() { [UploaderCategory.UrlSharing] = [] };
        public override Uploader CreateInstance(string settingsJson) => new TestSharer();
    }

    private sealed class TestSharer : UrlSharer
    {
        public override Task<UploadResult> ShareURLAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult(_share(url));
    }
}
