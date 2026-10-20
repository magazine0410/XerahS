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
using XerahS.Core.Security;

namespace XerahS.Tests.Helpers;

[TestFixture]
[NonParallelizable]
public class SandboxPathsTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "xerahs-sandbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        PathsManager.SetSandboxFolder(null);
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public void SettingsAndHistoryMoveToTheSandboxFolder_LogsAndScreenshotsDoNot()
    {
        string settings = PathsManager.SettingsFolder;
        string history = PathsManager.HistoryFolder;
        string screenshots = PathsManager.ScreenshotsFolder;
        string logs = PathsManager.LogsFolderBase;
        string personal = PathsManager.PersonalFolder;
        Assert.That(PathsManager.IsSandbox, Is.False);

        PathsManager.SetSandboxFolder(_folder);

        Assert.Multiple(() =>
        {
            Assert.That(PathsManager.IsSandbox, Is.True);
            Assert.That(PathsManager.SettingsFolder, Is.EqualTo(Path.Combine(_folder, AppResources.SettingsFolderName)));
            Assert.That(PathsManager.HistoryFolder, Is.EqualTo(Path.Combine(_folder, AppResources.HistoryFolderName)));
            Assert.That(PathsManager.BackupFolder, Does.StartWith(_folder + Path.DirectorySeparatorChar));
            Assert.That(PathsManager.HistoryBackupFolder, Does.StartWith(_folder + Path.DirectorySeparatorChar));
            Assert.That(SettingsManager.ApplicationConfigFilePath, Does.StartWith(_folder + Path.DirectorySeparatorChar));
            Assert.That(SettingsManager.WorkflowsConfigFilePath, Does.StartWith(_folder + Path.DirectorySeparatorChar));
            Assert.That(SettingsManager.UploadersConfigFilePath, Does.StartWith(_folder + Path.DirectorySeparatorChar));
            Assert.That(SettingsManager.SecretsStoreFilePath, Does.StartWith(_folder + Path.DirectorySeparatorChar));
            Assert.That(SettingsManager.GetHistoryFilePath(), Does.StartWith(_folder + Path.DirectorySeparatorChar));
            Assert.That(PathsManager.ScreenshotsFolder, Is.EqualTo(screenshots));
            Assert.That(PathsManager.LogsFolderBase, Is.EqualTo(logs));
            Assert.That(PathsManager.PersonalFolder, Is.EqualTo(personal));
        });

        PathsManager.SetSandboxFolder(null);
        Assert.That(PathsManager.SettingsFolder, Is.EqualTo(settings));
        Assert.That(PathsManager.HistoryFolder, Is.EqualTo(history));
    }

    [Test]
    public void CredentialsStayInTheSandboxFolder()
    {
        PathsManager.SetSandboxFolder(_folder);
        string path = SettingsManager.SecretsStoreFilePath;
        var store = new SecretStore(path);

        if (OperatingSystem.IsWindows())
        {
            // The DPAPI file store already keeps them in the settings folder.
            Assert.That(store.BackendName, Is.EqualTo("Windows DPAPI file store"));
            return;
        }

        Assert.That(store.BackendName, Is.EqualTo("AES file store (sandbox)"));
        Assert.That(store.IsFallback, Is.True);
        store.SetSecret("provider", "instance", "password", "sandbox-secret");
        Assert.That(store.GetSecret("provider", "instance", "password"), Is.EqualTo("sandbox-secret"));
        Assert.That(File.Exists(path), Is.True);
        Assert.That(Path.GetDirectoryName(path), Is.EqualTo(PathsManager.SettingsFolder));
    }
}
