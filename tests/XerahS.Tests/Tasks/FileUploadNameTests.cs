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

namespace XerahS.Tests.Tasks;

// ShareX's naming of uploaded files: a file keeps its own name unless "Use name pattern for file uploaders" is on, and
// every upload name loses its bidirectional control characters (and, with "Replace potentially problematic characters"
// on, its other URL-unsafe characters).
[TestFixture]
public class FileUploadNameTests
{
    [Test]
    public void FileUpload_KeepsItsOwnName_UnlessTheNamePatternIsOn()
    {
        var settings = new TaskSettings();
        settings.UploadSettings.NameFormatPattern = "renamed";
        var info = new TaskInfo(settings) { FilePath = "/home/user/XerahS-0.32.0-linux-x64.AppImage" };

        TaskHelpers.ApplyFileUploadName(info);
        Assert.That(info.FileName, Is.EqualTo("XerahS-0.32.0-linux-x64.AppImage"));

        settings.UploadSettings.FileUploadUseNamePattern = true;
        TaskHelpers.ApplyFileUploadName(info);
        Assert.That(info.FileName, Is.EqualTo("renamed.AppImage"));

        // As in ShareX, a ".tar" before the extension is kept.
        var archive = new TaskInfo(settings) { FilePath = "/home/user/backup.tar.gz" };
        TaskHelpers.ApplyFileUploadName(archive);
        Assert.That(archive.FileName, Is.EqualTo("renamed.tar.gz"));
    }

    [Test]
    public void UploadName_LosesBidiControlCharacters_AndOptionallyUrlUnsafeCharacters()
    {
        var settings = new TaskSettings();
        Assert.That(TaskHelpers.GetUploadFileName("my file (1)‮gnp.exe", settings), Is.EqualTo("my file (1)gnp.exe"));

        settings.UploadSettings.FileUploadReplaceProblematicCharacters = true;
        Assert.That(TaskHelpers.GetUploadFileName("my file (1).png", settings), Is.EqualTo("my_file_1_.png"));
        Assert.That(TaskHelpers.GetUploadFileName("a-b_c.d~e.png", settings), Is.EqualTo("a-b_c.d~e.png"));
    }

    [Test]
    public void ReplaceReservedCharacters_ReplacesEachRunOnce_LikeShareX()
    {
        Assert.That(URLHelpers.ReplaceReservedCharacters("a  b??c", "_"), Is.EqualTo("a_b_c"));
        Assert.That(URLHelpers.ReplaceReservedCharacters("ä.png", "_"), Is.EqualTo("_.png"));
        Assert.That(URLHelpers.RemoveBidiControlCharacters("‎abc‏"), Is.EqualTo("abc"));
    }
}
