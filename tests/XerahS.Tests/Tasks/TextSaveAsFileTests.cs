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
using XerahS.Core.Tasks;
using XerahS.Platform.Abstractions;

namespace XerahS.Tests.Tasks;

// ShareX's "Save text as file" for text uploads (Task settings → Advanced → TextTaskSaveAsFile).
[TestFixture]
[NonParallelizable]
public class TextSaveAsFileTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("xerahs-text-save-").FullName;

    [TearDown]
    public void TearDown()
    {
        PlatformServices.Reset();
        Directory.Delete(_directory, true);
    }

    [Test]
    public async Task ANameChosenWithoutOverwrite_DoesNotReplaceAFileWithThatName()
    {
        string existing = Path.Combine(_directory, "note.txt");
        string chosen = Path.Combine(_directory, "chosen.txt");
        File.WriteAllText(existing, "first");
        // The chosen file appears after the dialog checked the name.
        File.WriteAllText(chosen, "second");
        PlatformServices.RegisterUIService(new ConflictUI(new FileConflictResolution(chosen, Overwrite: false)));
        TaskInfo info = CreateInfo(FileExistAction.Ask, "note");

        await TaskStart.SaveTextAsFileAsync(info, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(existing), Is.EqualTo("first"));
            Assert.That(File.ReadAllText(chosen), Is.EqualTo("second"));
            Assert.That(info.FilePath, Is.Empty, "the text is uploaded as text instead");
        });
    }

    [TestCase(FileExistAction.Overwrite, "uploaded text")]
    [TestCase(FileExistAction.UniqueName, "first")]
    public async Task TheWorkflowsFileExistsAction_DecidesWhetherTheFileIsReplaced(FileExistAction action, string expected)
    {
        string existing = Path.Combine(_directory, "note.txt");
        File.WriteAllText(existing, "first");
        TaskInfo info = CreateInfo(action, "note");

        await TaskStart.SaveTextAsFileAsync(info, CancellationToken.None);

        Assert.That(File.ReadAllText(existing), Is.EqualTo(expected));
        Assert.That(File.ReadAllText(info.FilePath), Is.EqualTo("uploaded text"));
    }

    private TaskInfo CreateInfo(FileExistAction action, string fileName)
    {
        var settings = new TaskSettings { Job = WorkflowType.ClipboardUpload, OverrideScreenshotsFolder = true, ScreenshotsFolder = _directory };
        settings.ImageSettings.FileExistAction = action;
        settings.AdvancedSettings.TextTaskSaveAsFile = true;
        settings.AdvancedSettings.TextFileExtension = "txt";
        var info = new TaskInfo(settings) { Job = TaskJob.TextUpload, TextContent = "uploaded text" };
        info.SetFileName(fileName + ".txt");
        return info;
    }

    private sealed class ConflictUI(FileConflictResolution resolution) : XerahS.CLI.Services.HeadlessUIService, IUIService
    {
        public Task<FileConflictResolution?> ResolveFileConflictAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileConflictResolution?>(resolution);
    }
}
