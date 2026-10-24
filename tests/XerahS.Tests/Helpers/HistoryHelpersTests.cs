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

namespace XerahS.Tests.Helpers;

[TestFixture]
public class HistoryHelpersTests
{
    [Test]
    public void OutputStats_WithEmptyHistory_DoesNotEmitNaNPercentages()
    {
        string stats = HistoryHelpers.OutputStats(new List<HistoryItem>());

        Assert.That(stats, Does.Contain("Total: 0"));
        Assert.That(stats, Does.Not.Contain("NaN"));
        Assert.That(stats, Does.Not.Contain("∞"));
    }

    [Test]
    public void ParseTags_ReadsOneKeyValuePerLine_LikeShareX()
    {
        var tags = HistoryHelpers.ParseTags("WindowTitle=A=B\r\n Favorite \n\n=skipped\nEmpty=");
        Assert.That(tags, Is.EqualTo(new Dictionary<string, string?>
        {
            ["WindowTitle"] = "A=B",
            ["Favorite"] = null,
            ["Empty"] = string.Empty
        }));
        Assert.That(HistoryHelpers.ParseTags(null), Is.Empty);
    }

    [Test]
    public void FormatTags_KeepsLineBreaksAndBackslashesInValues_ThroughParseTags()
    {
        var tags = new Dictionary<string, string?>
        {
            ["OcrText"] = "Hello\nAnnotationSidecarPath=/home/user/notes.txt\r\nFavorite",
            ["Errors"] = "a \\n b\\",
            ["Plain"] = "value"
        };

        string text = HistoryHelpers.FormatTags(tags);

        Assert.That(text.Split(Environment.NewLine), Has.Length.EqualTo(3), "Every tag stays on its own line.");
        Assert.That(HistoryHelpers.ParseTags(text), Is.EqualTo(tags));
        Assert.That(HistoryHelpers.ParseTags("Path=C:\\temp\\x"), Is.EqualTo(new Dictionary<string, string?> { ["Path"] = "C:\\temp\\x" }),
            "A backslash before another character is kept as typed.");
    }

    [Test]
    public void FormatTags_ListsTagsByKey_OneKeyValuePerLine()
    {
        string text = HistoryHelpers.FormatTags(new Dictionary<string, string?> { ["b"] = "2", ["a"] = null });
        Assert.That(text, Is.EqualTo("a=" + Environment.NewLine + "b=2"));
        Assert.That(HistoryHelpers.FormatTags(null), Is.Empty);
    }
}
