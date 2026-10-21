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

namespace XerahS.Tests.History;

public sealed class HistoryItemAnnotationsTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-history-annotations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public void HasEditableAnnotations_TrueForAnImageWithItsDefaultSidecar()
    {
        string image = Path.Combine(_directory, "shot.png");
        File.WriteAllBytes(image, [1]);
        File.WriteAllText(Path.Combine(_directory, "shot.xann"), "{}");
        Assert.That(new HistoryItem { FilePath = image }.HasEditableAnnotations, Is.True);
    }

    [Test]
    public void HasEditableAnnotations_FalseForASidecarFileEntry()
    {
        // An imported folder can hold the sidecar itself; its "default sidecar" would be the file, so it must not count.
        string sidecar = Path.Combine(_directory, "shot.xann");
        File.WriteAllText(sidecar, "{}");
        var item = new HistoryItem { FileName = "shot.xann", FilePath = sidecar };
        Assert.Multiple(() =>
        {
            Assert.That(item.IsAnnotationSidecarFile, Is.True);
            Assert.That(item.HasEditableAnnotations, Is.False);
            Assert.That(new HistoryItem { FileName = "shot.XANN" }.IsAnnotationSidecarFile, Is.True);
            Assert.That(new HistoryItem { FilePath = Path.Combine(_directory, "shot.png") }.IsAnnotationSidecarFile, Is.False);
        });
    }
}
