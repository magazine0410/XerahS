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

namespace XerahS.Tests.Helpers;

[NonParallelizable]
public class TaskHelpersScreenshotsFolderTests
{
    private string _originalSecondaryPath = string.Empty;
    private bool _originalUseCustomScreenshotsPath;
    private string _originalCustomScreenshotsPath = string.Empty;
    private bool _originalUseSaveImageSubFolderPattern;
    private string _originalSaveImageSubFolderPattern = string.Empty;
    private string _originalSaveImageSubFolderPatternWindow = string.Empty;

    [SetUp]
    public void SetUp()
    {
        var settings = SettingsManager.Settings;
        _originalSecondaryPath = settings.CustomScreenshotsPath2;
        settings.CustomScreenshotsPath2 = string.Empty;
        _originalUseCustomScreenshotsPath = settings.UseCustomScreenshotsPath;
        _originalCustomScreenshotsPath = settings.CustomScreenshotsPath;
        _originalUseSaveImageSubFolderPattern = settings.UseSaveImageSubFolderPattern;
        _originalSaveImageSubFolderPattern = settings.SaveImageSubFolderPattern;
        _originalSaveImageSubFolderPatternWindow = settings.SaveImageSubFolderPatternWindow;
    }

    [TearDown]
    public void TearDown()
    {
        var settings = SettingsManager.Settings;
        settings.CustomScreenshotsPath2 = _originalSecondaryPath;
        settings.UseCustomScreenshotsPath = _originalUseCustomScreenshotsPath;
        settings.CustomScreenshotsPath = _originalCustomScreenshotsPath;
        settings.UseSaveImageSubFolderPattern = _originalUseSaveImageSubFolderPattern;
        settings.SaveImageSubFolderPattern = _originalSaveImageSubFolderPattern;
        settings.SaveImageSubFolderPatternWindow = _originalSaveImageSubFolderPatternWindow;
    }

    [Test]
    public void GetScreenshotsFolder_ExpandsCustomPath_WhenEnabled()
    {
        var settings = SettingsManager.Settings;
        settings.UseCustomScreenshotsPath = true;
        settings.CustomScreenshotsPath = Path.Combine("%TEMP%", "XerahS-CustomShots");
        settings.UseSaveImageSubFolderPattern = false;
        settings.SaveImageSubFolderPattern = string.Empty;
        settings.SaveImageSubFolderPatternWindow = string.Empty;

        string folder = TaskHelpers.GetScreenshotsFolder();

        string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "XerahS-CustomShots"));
        Assert.That(folder, Is.EqualTo(expected));
    }

    [Test]
    public void GetScreenshotsFolder_UsesCustomPathWithSubfolderPattern_WhenEnabled()
    {
        var settings = SettingsManager.Settings;
        settings.UseCustomScreenshotsPath = true;
        settings.CustomScreenshotsPath = Path.Combine("%TEMP%", "XerahS-CustomShots");
        settings.UseSaveImageSubFolderPattern = true;
        settings.SaveImageSubFolderPattern = "captures";
        settings.SaveImageSubFolderPatternWindow = string.Empty;

        string folder = TaskHelpers.GetScreenshotsFolder();

        string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "XerahS-CustomShots", "captures"));
        Assert.That(folder, Is.EqualTo(expected));
    }

    [Test]
    public void GetScreenshotsFolder_IgnoresPattern_WhenSubfolderDisabled()
    {
        var settings = SettingsManager.Settings;
        settings.UseCustomScreenshotsPath = false;
        settings.CustomScreenshotsPath = string.Empty;
        settings.UseSaveImageSubFolderPattern = false;
        settings.SaveImageSubFolderPattern = "%y-%mo";

        string folder = TaskHelpers.GetScreenshotsFolder();
        string expected = FileHelpers.GetAbsolutePath(XerahS.Common.PathsManager.ScreenshotsFolder);

        Assert.That(folder, Is.EqualTo(expected));
    }

    [Test]
    public void GetScreenshotsParentFolder_ExpandsCustomPath_WhenEnabled()
    {
        var settings = SettingsManager.Settings;
        settings.UseCustomScreenshotsPath = true;
        settings.CustomScreenshotsPath = Path.Combine("%TEMP%", "XerahS-ParentShots");

        string folder = TaskHelpers.GetScreenshotsParentFolder();

        string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "XerahS-ParentShots"));
        Assert.That(folder, Is.EqualTo(expected));
    }

    [Test]
    public void GetScreenshotsParentFolder_UsesScreencastsFolder_ForRecording_WhenCustomPathDisabled()
    {
        var settings = SettingsManager.Settings;
        settings.UseCustomScreenshotsPath = false;
        settings.CustomScreenshotsPath = string.Empty;

        var taskSettings = new TaskSettings
        {
            Job = WorkflowType.ScreenRecorder
        };

        string folder = TaskHelpers.GetScreenshotsParentFolder(taskSettings);

        Assert.That(folder, Is.EqualTo(PathsManager.ScreencastsFolder));
    }
    [Test]
    public void SecondaryFolder_PreservesPatternAndPrimaryPrecedence()
    {
        string root = Path.Combine(Path.GetTempPath(), "xerahs-folders-" + Guid.NewGuid().ToString("N"));
        string primary = Path.Combine(root, "primary"), secondary = Path.Combine(root, "secondary");
        Directory.CreateDirectory(secondary);
        try
        {
            var config = SettingsManager.Settings;
            config.UseCustomScreenshotsPath = true;
            config.CustomScreenshotsPath = primary;
            config.CustomScreenshotsPath2 = secondary;
            config.UseSaveImageSubFolderPattern = true;
            config.SaveImageSubFolderPattern = "captures";
            Assert.That(TaskHelpers.GetScreenshotsFolder(), Is.EqualTo(Path.Combine(secondary, "captures")));
            Directory.CreateDirectory(primary);
            Assert.That(TaskHelpers.GetScreenshotsFolder(), Is.EqualTo(Path.Combine(primary, "captures")));
            Directory.Delete(primary);
            Directory.Delete(secondary);
            Assert.That(TaskHelpers.GetScreenshotsParentFolder(), Is.EqualTo(PathsManager.ScreenshotsFolder));
            config.CustomScreenshotsPath2 = "";
            Assert.That(TaskHelpers.GetScreenshotsParentFolder(), Is.EqualTo(primary), "Without a fallback, ShareX allows a new primary folder.");
        }
        finally { Directory.Delete(root, true); }
    }
}
