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
using XerahS.History;
using XerahS.Tests.Xip0052;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.History;

[TestFixture]
public sealed class HistoryCopyTests
{
    [TestCase(HistoryCopyFormat.HtmlLink, "<a href=\"https://example.test/image.png\">https://example.test/image.png</a>")]
    [TestCase(HistoryCopyFormat.HtmlImage, "<img src=\"https://example.test/image.png\"/>")]
    [TestCase(HistoryCopyFormat.HtmlLinkedImage, "<a href=\"https://example.test/image.png\"><img src=\"https://example.test/thumb.png\"/></a>")]
    [TestCase(HistoryCopyFormat.ForumLink, "[url]https://example.test/image.png[/url]")]
    [TestCase(HistoryCopyFormat.ForumImage, "[img]https://example.test/image.png[/img]")]
    [TestCase(HistoryCopyFormat.ForumLinkedImage, "[url=https://example.test/image.png][img]https://example.test/thumb.png[/img][/url]")]
    [TestCase(HistoryCopyFormat.MarkdownLink, "[image.png](https://example.test/image.png)")]
    [TestCase(HistoryCopyFormat.MarkdownImage, "![image.png](https://example.test/image.png)")]
    [TestCase(HistoryCopyFormat.MarkdownLinkedImage, "[![image.png](https://example.test/thumb.png)](https://example.test/image.png)")]
    public void CopyFormats_MatchShareXTemplates(HistoryCopyFormat format, string expected)
    {
        var item = new HistoryItem { FileName = "image.png", URL = "https://example.test/image.png", ThumbnailURL = "https://example.test/thumb.png" };
        Assert.That(HistoryCopyText.GetValue(item, format), Is.EqualTo(expected));
    }

    [Test]
    public void LinkedImageFormats_RequireBothUrls()
    {
        foreach (var format in new[] { HistoryCopyFormat.HtmlLinkedImage, HistoryCopyFormat.ForumLinkedImage, HistoryCopyFormat.MarkdownLinkedImage })
        {
            Assert.That(HistoryCopyText.GetValue(new HistoryItem { URL = "https://example.test/image.png" }, format), Is.Null);
            Assert.That(HistoryCopyText.GetValue(new HistoryItem { ThumbnailURL = "https://example.test/thumb.png" }, format), Is.Null);
        }
    }

    [Test]
    public void CopyFormats_EscapeHtmlAndMarkdownMetacharacters()
    {
        var item = new HistoryItem { FileName = "[capture].png", Type = "Image", URL = "https://example.test/a(1).png?x=1&y=2" };
        Assert.Multiple(() =>
        {
            Assert.That(HistoryCopyText.GetValue(item, HistoryCopyFormat.HtmlImage), Is.EqualTo("<img src=\"https://example.test/a(1).png?x=1&amp;y=2\"/>"));
            Assert.That(HistoryCopyText.GetValue(item, HistoryCopyFormat.MarkdownImage), Is.EqualTo("![\\[capture\\].png](<https://example.test/a(1).png?x=1&y=2>)"));
            Assert.That(HistoryCopyText.GetValue(new HistoryItem { URL = "https://example.test/\"quoted\"" }, HistoryCopyFormat.HtmlLink), Does.Contain("&quot;"));
        });
    }

    [Test]
    public void FileNames_KeepIntermediateExtensions_AndDoNotRequireAnExistingFile()
    {
        var item = new HistoryItem { FilePath = Path.Combine(Path.GetTempPath(), "report.final.png") };
        Assert.Multiple(() =>
        {
            Assert.That(HistoryCopyText.GetValue(item, HistoryCopyFormat.FileName), Is.EqualTo("report.final"));
            Assert.That(HistoryCopyText.GetValue(item, HistoryCopyFormat.FileNameWithExtension), Is.EqualTo("report.final.png"));
            Assert.That(HistoryCopyText.GetValue(item, HistoryCopyFormat.Folder), Is.EqualTo(Path.GetDirectoryName(item.FilePath)));
        });
    }
}
