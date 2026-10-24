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
using XerahS.Media.Metadata;
using XerahS.Platform.Linux.Services;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Tests.Platform.Linux;

// Temporary copies of the user's files, and the folder KWin loads scripts from, are not readable by other users.
[TestFixture]
[Platform(Include = "Linux")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public class PrivateTemporaryFilesTests
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnly = OwnerReadWrite | UnixFileMode.UserExecute;
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("xerahs-private-files-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void CreatePrivateFile_IsReadableOnlyByTheUser_AndDoesNotOpenAnExistingFile()
    {
        string path = Path.Combine(_directory, "copy.bin");
        using (FileStream stream = FileHelpers.CreatePrivateFile(path))
        {
            stream.WriteByte(1);
        }

        Assert.That(File.GetUnixFileMode(path), Is.EqualTo(OwnerReadWrite));
        Assert.Throws<IOException>(() => FileHelpers.CreatePrivateFile(path).Dispose());
    }

    [Test]
    public void ThePrintedPdf_IsReadableOnlyByTheUser()
    {
        string path = LinuxPrintService.WriteTemporaryPdf([0x25, 0x50, 0x44, 0x46]);
        try
        {
            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(OwnerReadWrite));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 0x25, 0x50, 0x44, 0x46 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void WithoutARuntimeFolder_TheKWinScriptFolderHasARandomName()
    {
        string first = KWinScriptBridge.CreateScriptDirectory(null);
        string second = KWinScriptBridge.CreateScriptDirectory(Path.Combine(_directory, "missing"));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(Path.GetFileName(first), Does.StartWith("xerahs-kwin-").And.Not.EqualTo($"xerahs-kwin-{Environment.ProcessId}"));
                Assert.That(File.GetUnixFileMode(first), Is.EqualTo(OwnerOnly));
            });
        }
        finally
        {
            Directory.Delete(first);
            Directory.Delete(second);
        }
    }

    [Test]
    public void WithARuntimeFolder_TheKWinScriptFolderIsInsideIt()
    {
        string directory = KWinScriptBridge.CreateScriptDirectory(_directory);
        Assert.That(Path.GetDirectoryName(directory), Is.EqualTo(_directory));
        Assert.That(File.GetUnixFileMode(directory), Is.EqualTo(OwnerOnly));
    }

    [Test]
    public async Task StrippingMetadata_KeepsTheFilesMode()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.Teal);
        using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        string path = Path.Combine(_directory, "shot.png");
        await File.WriteAllBytesAsync(path, encoded.ToArray());
        File.SetUnixFileMode(path, OwnerReadWrite | UnixFileMode.GroupRead);

        await MetadataService.StripMetadataAsync(path);

        Assert.That(File.GetUnixFileMode(path), Is.EqualTo(OwnerReadWrite | UnixFileMode.GroupRead));
        Assert.That(Directory.GetFiles(_directory), Is.EqualTo(new[] { path }), "no temporary copy is left behind");
    }
}
