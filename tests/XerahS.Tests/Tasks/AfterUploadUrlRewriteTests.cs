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
using XerahS.History;
using XerahS.Uploaders;

namespace XerahS.Tests.Tasks;

// ShareX's "Perform regex replace on URL" and ResultForceHTTPS, applied at the start of DoAfterUploadJobs.
public partial class AfterUploadTasksTests
{
    private const string RewrittenUrl = "https://cdn.test/original.png";

    [Test]
    public async Task RegexReplace_ChangesTheUrlBeforeShorteningCopyingAndHistory()
    {
        var info = NewUpload(AfterUploadTasks.UseURLShortener | AfterUploadTasks.CopyURLToClipboard);
        UseRegexReplace(info, @"^https://files\.test/(.+)$", "https://cdn.test/$1");

        await new UploadJobProcessor().ProcessAsync(info, default);

        using var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.Multiple(() =>
        {
            Assert.That(_events, Is.EqualTo(new[] { "upload", "shorten:" + RewrittenUrl, "copy:" + ShortUrl }));
            Assert.That(info.Result.URL, Is.EqualTo(RewrittenUrl));
            Assert.That(info.Metadata.UploadURL, Is.EqualTo(RewrittenUrl));
            Assert.That(history.GetHistoryItem(info.HistoryItemId!.Value)!.URL, Is.EqualTo(RewrittenUrl));
        });
    }

    [Test]
    public async Task RegexReplace_IsOffByDefault()
    {
        var info = NewUpload(AfterUploadTasks.CopyURLToClipboard);
        info.TaskSettings.UploadSettings.URLRegexReplacePattern = ".+";
        info.TaskSettings.UploadSettings.URLRegexReplaceReplacement = "replaced";

        await new UploadJobProcessor().ProcessAsync(info, default);

        Assert.That(_clipboard.Text, Is.EqualTo(OriginalUrl));
    }

    [Test]
    public async Task InvalidRegex_SkipsTheRemainingTasksLikeShareX_AndKeepsTheUpload()
    {
        var info = NewUpload();
        UseRegexReplace(info, "(", "x");

        await new UploadJobProcessor().ProcessAsync(info, default);

        using var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.Multiple(() =>
        {
            Assert.That(_events, Is.EqualTo(new[] { "upload", "window" }));
            Assert.That(info.Result.URL, Is.EqualTo(OriginalUrl));
            Assert.That(info.Result.IsSuccess, Is.True);
            Assert.That(_ui.WindowInfo!.ErrorDetails, Is.Not.Empty);
            Assert.That(history.GetHistoryItem(info.HistoryItemId!.Value)!.URL, Is.EqualTo(OriginalUrl));
        });
    }

    [Test]
    public async Task ForceHttps_ChangesTheResultUrlsBeforeShortening()
    {
        _upload = () =>
        {
            _events.Add("upload");
            return new UploadResult
            {
                URL = "http://files.test/original.png", IsSuccess = true,
                ThumbnailURL = "http://files.test/thumb.png", DeletionURL = "HTTP://files.test/delete"
            };
        };
        var info = NewUpload(AfterUploadTasks.UseURLShortener | AfterUploadTasks.CopyURLToClipboard);
        info.TaskSettings.AdvancedSettings.ResultForceHTTPS = true;

        await new UploadJobProcessor().ProcessAsync(info, default);

        using var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var row = history.GetHistoryItem(info.HistoryItemId!.Value)!;
        Assert.Multiple(() =>
        {
            Assert.That(_events, Does.Contain("shorten:" + OriginalUrl));
            Assert.That(info.Result.URL, Is.EqualTo(OriginalUrl));
            Assert.That(info.Result.ThumbnailURL, Is.EqualTo("https://files.test/thumb.png"));
            Assert.That(info.Result.DeletionURL, Is.EqualTo("https://files.test/delete"));
            Assert.That(row.URL, Is.EqualTo(OriginalUrl));
            Assert.That(row.ThumbnailURL, Is.EqualTo("https://files.test/thumb.png"));
            Assert.That(row.DeletionURL, Is.EqualTo("https://files.test/delete"));
        });
    }

    [Test]
    public async Task RegexReplaceRunsBeforeForceHttps()
    {
        var info = NewUpload(AfterUploadTasks.CopyURLToClipboard);
        UseRegexReplace(info, @"^https://files\.test/(.+)$", "http://cdn.test/$1");
        info.TaskSettings.AdvancedSettings.ResultForceHTTPS = true;

        await new UploadJobProcessor().ProcessAsync(info, default);

        Assert.That(_clipboard.Text, Is.EqualTo(RewrittenUrl));
    }

    [Test]
    public async Task CaptureHistory_GetsTheRewrittenUrl()
    {
        var info = NewUpload(AfterUploadTasks.CopyURLToClipboard);
        info.Job = TaskJob.Job;
        info.DataType = EDataType.Image;
        info.TaskSettings.Job = WorkflowType.RectangleRegion;
        info.TaskSettings.AfterCaptureJob = AfterCaptureTasks.UploadImageToHost;
        UseRegexReplace(info, @"^https://files\.test/(.+)$", "https://cdn.test/$1");
        info.Result = SuccessfulUpload();
        info.Metadata.UploadURL = OriginalUrl;
        using var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var row = UploadJobProcessor.CreateHistoryItem(info, OriginalUrl);
        history.AppendHistoryItem(row);
        info.HistoryItemId = row.Id;

        await new UploadJobProcessor().ProcessAsync(info, default);

        Assert.That(history.GetHistoryItem(row.Id)!.URL, Is.EqualTo(RewrittenUrl));
        Assert.That(_clipboard.Text, Is.EqualTo(RewrittenUrl));
    }

    private static void UseRegexReplace(TaskInfo info, string pattern, string replacement)
    {
        info.TaskSettings.UploadSettings.URLRegexReplace = true;
        info.TaskSettings.UploadSettings.URLRegexReplacePattern = pattern;
        info.TaskSettings.UploadSettings.URLRegexReplaceReplacement = replacement;
    }
}
