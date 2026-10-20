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

using System.Reflection;
using NUnit.Framework;
using XerahS.Common;

namespace XerahS.Tests.Helpers;

[TestFixture]
[NonParallelizable]
public class PortablePathsTests
{
    [Test]
    public void MarkerKeepsDataBesideExecutableAndExplicitOverrideWins()
    {
        string marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        byte[]? originalMarker = File.Exists(marker) ? File.ReadAllBytes(marker) : null;
        FieldInfo folderField = typeof(PathsManager).GetField("_personalFolder", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo overrideField = typeof(PathsManager).GetField("_personalFolderOverrideSet", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? originalFolder = folderField.GetValue(null);
        object? originalOverride = overrideField.GetValue(null);
        try
        {
            File.WriteAllText(marker, "");
            folderField.SetValue(null, "");
            overrideField.SetValue(null, false);
            string expected = Path.Combine(AppContext.BaseDirectory, AppResources.AppName);
            Assert.Multiple(() =>
            {
                Assert.That(PathsManager.IsPortable, Is.True);
                Assert.That(PathsManager.PersonalFolder, Is.EqualTo(expected));
                Assert.That(PathsManager.SettingsFolder, Does.StartWith(expected + Path.DirectorySeparatorChar));
                Assert.That(PathsManager.PluginsFolder, Does.StartWith(expected + Path.DirectorySeparatorChar));
            });

            string custom = Path.Combine(Path.GetTempPath(), "xerahs-explicit-personal-folder");
            PathsManager.PersonalFolder = custom;
            Assert.That(PathsManager.PersonalFolder, Is.EqualTo(custom));

            File.Delete(marker);
            Assert.That(PathsManager.IsPortable, Is.False);
            Assert.That(PathsManager.PersonalFolder, Is.EqualTo(custom));
        }
        finally
        {
            folderField.SetValue(null, originalFolder);
            overrideField.SetValue(null, originalOverride);
            if (originalMarker == null) File.Delete(marker);
            else File.WriteAllBytes(marker, originalMarker);
        }
    }

    [Test]
    public void PortableFlagKeepsDataBesideTheAppImageFile()
    {
        string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        FieldInfo portableField = typeof(PathsManager).GetField("_portableMode", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? originalPortable = portableField.GetValue(null);
        string folder = Path.Combine(Path.GetTempPath(), "xerahs-portable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string appImageFile = Path.Combine(folder, "XerahS.AppImage");
        File.WriteAllText(appImageFile, "");
        try
        {
            Environment.SetEnvironmentVariable("APPIMAGE", null);
            Assert.That(PathsManager.PortablePersonalFolder, Is.EqualTo(Path.Combine(AppContext.BaseDirectory, AppResources.AppName)));

            Environment.SetEnvironmentVariable("APPIMAGE", appImageFile);
            if (OperatingSystem.IsLinux())
            {
                Assert.That(PathsManager.PortablePersonalFolder, Is.EqualTo(Path.Combine(folder, AppResources.AppName)));
                Assert.That(PathsManager.PortableCheckFilePath, Is.EqualTo(Path.Combine(folder, "portable.txt")));
                Assert.That(PathsManager.IsPortable, Is.False);
                File.WriteAllText(Path.Combine(folder, "portable.txt"), "");
                Assert.That(PathsManager.IsPortable, Is.True);
                File.Delete(Path.Combine(folder, "portable.txt"));

                // An executable named after the application takes the folder's name.
                File.WriteAllText(Path.Combine(folder, AppResources.AppName), "");
                Assert.That(PathsManager.PortablePersonalFolder, Is.EqualTo(Path.Combine(folder, AppResources.AppName + "-Portable")));
            }

            PathsManager.SetPortableMode(true);
            Assert.That(PathsManager.IsPortable, Is.True);
            PathsManager.SetPortableMode(false);
            Assert.That(PathsManager.IsPortable, Is.False);
        }
        finally
        {
            portableField.SetValue(null, originalPortable);
            Environment.SetEnvironmentVariable("APPIMAGE", appImage);
            Directory.Delete(folder, recursive: true);
        }
    }
}
