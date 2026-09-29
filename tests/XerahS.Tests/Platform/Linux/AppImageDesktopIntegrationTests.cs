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
using XerahS.Platform.Linux.Services;

namespace XerahS.Tests.Platform.Linux;

public class AppImageDesktopIntegrationTests
{
    private string _root = null!;
    private string _dataHome = null!;
    private string _systemData = null!;
    private string _appDir = null!;
    private string _appImage = null!;

    private string DesktopPath => Path.Combine(_dataHome, "applications", "xerahs.desktop");
    private string IconPath => Path.Combine(_dataHome, "icons", "hicolor", "512x512", "apps", "xerahs.png");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "xerahs-appimage-" + Guid.NewGuid().ToString("N"));
        _dataHome = Path.Combine(_root, "home", ".local", "share");
        _systemData = Path.Combine(_root, "usr", "share");
        _appDir = Path.Combine(_root, "mount");
        Directory.CreateDirectory(Path.Combine(_appDir, "usr", "share", "icons", "hicolor", "512x512", "apps"));
        File.WriteAllText(Path.Combine(_appDir, "usr", "share", "icons", "hicolor", "512x512", "apps", "xerahs.png"), "png");
        _appImage = Path.Combine(_root, "My Apps", "XerahS-0.32.0-linux-x64.AppImage");
        Directory.CreateDirectory(Path.GetDirectoryName(_appImage)!);
        File.WriteAllText(_appImage, "");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private AppImageDesktopEntryAction Ensure(string? appImage = null) =>
        AppImageDesktopIntegration.EnsureDesktopEntry(appImage ?? _appImage, _appDir, _dataHome, [_systemData]);

    [Test]
    public void CreatesEntryAndIcon_ForAppImageWithoutInstalledEntry()
    {
        Assert.That(Ensure(), Is.EqualTo(AppImageDesktopEntryAction.Created));

        string entry = File.ReadAllText(DesktopPath);
        Assert.Multiple(() =>
        {
            Assert.That(entry, Does.Contain($"Exec=\"{_appImage}\" %U\n"));
            Assert.That(entry, Does.Contain($"TryExec={_appImage}\n"));
            Assert.That(entry, Does.Contain(AppImageDesktopIntegration.MarkerKey + "=true"));
            Assert.That(File.Exists(IconPath), Is.True);
            Assert.That(Ensure(), Is.EqualTo(AppImageDesktopEntryAction.None));
        });
    }

    [Test]
    public void UpdatesOwnEntry_WhenAppImageMoves()
    {
        Ensure();
        string moved = Path.Combine(_root, "XerahS-0.33.0-linux-x64.AppImage");
        File.WriteAllText(moved, "");

        Assert.That(Ensure(moved), Is.EqualTo(AppImageDesktopEntryAction.Updated));
        Assert.That(File.ReadAllText(DesktopPath), Does.Contain($"Exec=\"{moved}\" %U"));
    }

    [Test]
    public void LeavesEntriesItDidNotWrite()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopPath)!);
        File.WriteAllText(DesktopPath, "[Desktop Entry]\nExec=/opt/xerahs/xerahs\n");

        Assert.That(Ensure(), Is.EqualTo(AppImageDesktopEntryAction.None));
        Assert.That(File.ReadAllText(DesktopPath), Is.EqualTo("[Desktop Entry]\nExec=/opt/xerahs/xerahs\n"));
    }

    [Test]
    public void SkipsWhenPackageInstalledTheEntry()
    {
        Directory.CreateDirectory(Path.Combine(_systemData, "applications"));
        File.WriteAllText(Path.Combine(_systemData, "applications", "xerahs.desktop"), "[Desktop Entry]\n");

        Assert.That(Ensure(), Is.EqualTo(AppImageDesktopEntryAction.None));
        Assert.That(File.Exists(DesktopPath), Is.False);
    }

    [Test]
    public void SkipsWhenNotRunningFromAppImage()
    {
        Assert.That(AppImageDesktopIntegration.EnsureDesktopEntry(null, null, _dataHome, [_systemData]),
            Is.EqualTo(AppImageDesktopEntryAction.None));
        Assert.That(File.Exists(DesktopPath), Is.False);
    }

    [Test]
    public void EscapesReservedCharactersInExec()
    {
        string entry = AppImageDesktopIntegration.BuildDesktopEntry("/home/u/$bin/X\"S.AppImage");

        // Quoted argument escapes $ and " with a backslash; the string value then doubles each backslash.
        Assert.That(entry, Does.Contain("Exec=\"/home/u/\\\\$bin/X\\\\\"S.AppImage\" %U\n"));
    }
}
