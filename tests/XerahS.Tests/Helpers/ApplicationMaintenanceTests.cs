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
using XerahS.Core.Managers;

namespace XerahS.Tests.Helpers;

[TestFixture, NonParallelizable]
public class ApplicationMaintenanceTests
{
    [Test]
    public void Cleanup_IsOptIn_KeepsNewestAndActiveLog_AndLeavesUnrelatedFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "xerahs-cleanup-" + Guid.NewGuid().ToString("N"));
        string backups = Path.Combine(root, "backups"), logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(backups); Directory.CreateDirectory(logs);
        try
        {
            for (int i = 1; i <= 4; i++)
            {
                string backup = Path.Combine(backups, $"backup-2026-10-0{i}-host.zip");
                string log = Path.Combine(logs, $"XerahS-2026100{i}.log");
                File.WriteAllText(backup, "test"); File.WriteAllText(log, "test");
                File.SetLastWriteTimeUtc(backup, DateTime.UtcNow.AddDays(i - 10));
                File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(i - 10));
            }
            File.WriteAllText(Path.Combine(backups, "personal.zip"), "keep");
            string active = Path.Combine(logs, "XerahS-20261001.log");
            var config = new ApplicationConfig { CleanupKeepFileCount = 2 };
            SettingsCleanupService.Cleanup(config, backups, logs, active);
            Assert.That(Directory.GetFiles(backups).Length, Is.EqualTo(5));
            config.AutoCleanupBackupFiles = config.AutoCleanupLogFiles = true;
            SettingsCleanupService.Cleanup(config, backups, logs, active);
            Assert.That(Directory.GetFiles(backups).Select(Path.GetFileName), Is.EquivalentTo(new[] { "backup-2026-10-03-host.zip", "backup-2026-10-04-host.zip", "personal.zip" }));
            Assert.That(Directory.GetFiles(logs).Length, Is.EqualTo(3));
            config.CleanupKeepFileCount = 0;
            SettingsCleanupService.Cleanup(config, backups, logs, active);
            Assert.That(Directory.GetFiles(logs), Is.EqualTo(new[] { active }));
            Assert.That(File.Exists(Path.Combine(backups, "personal.zip")), Is.True);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void LogCleanup_IncludesTheNetworkMonitorLogs()
    {
        string root = Path.Combine(Path.GetTempPath(), "xerahs-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 1; i <= 3; i++)
            {
                string log = Path.Combine(root, $"NetworkMonitor-2026090{i}.log");
                File.WriteAllText(log, "test");
                File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(i - 10));
            }
            SettingsCleanupService.Cleanup(new ApplicationConfig { AutoCleanupLogFiles = true, CleanupKeepFileCount = 1 }, root, root);
            Assert.That(Directory.GetFiles(root).Select(Path.GetFileName), Is.EqualTo(new[] { "NetworkMonitor-20260903.log" }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Cleanup_CountsBackupTypesAcrossMonthsAndDoesNotFollowSymlinks()
    {
        string root = Path.Combine(Path.GetTempPath(), "xerahs-cleanup-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int month = 1; month <= 3; month++)
            {
                string folder = Path.Combine(root, $"2026-{month:00}");
                Directory.CreateDirectory(folder);
                foreach (string name in new[] { $"backup-2026-{month:00}-01-host.zip", $"backup-2026-W{month:00}-host.zip", $"ApplicationConfig-{month}.json" })
                {
                    string file = Path.Combine(folder, name);
                    File.WriteAllText(file, "backup");
                    File.SetLastWriteTimeUtc(file, new DateTime(2026, month, 1));
                }
            }
            string outside = Path.Combine(root, "unrelated");
            Directory.CreateDirectory(outside);
            string unrelated = Path.Combine(outside, "backup-2025-01-01-host.zip");
            File.WriteAllText(unrelated, "keep");
            if (OperatingSystem.IsLinux()) Directory.CreateSymbolicLink(Path.Combine(root, "2025-01"), outside);
            SettingsCleanupService.Cleanup(new ApplicationConfig { AutoCleanupBackupFiles = true, CleanupKeepFileCount = 1 }, root, root);
            Assert.That(Directory.GetFiles(Path.Combine(root, "2026-01")), Is.Empty);
            Assert.That(Directory.GetFiles(Path.Combine(root, "2026-02")), Is.Empty);
            Assert.That(Directory.GetFiles(Path.Combine(root, "2026-03")).Length, Is.EqualTo(3));
            Assert.That(File.Exists(unrelated), Is.True);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void CustomBrowser_PassesUrlAsOneArgument_AndEmptyPathUsesDefault()
    {
        string old = HelpersOptions.BrowserPath;
        bool supported = HelpersOptions.SupportsCustomBrowser;
        HelpersOptions.SupportsCustomBrowser = true;
        try
        {
            HelpersOptions.BrowserPath = "/path/browser with spaces";
            const string url = "https://example.com/?q=a%20b&x=$value";
            var info = URLHelpers.CreateBrowserStartInfo(url);
            Assert.That(info.FileName, Is.EqualTo(HelpersOptions.BrowserPath));
            Assert.That(info.ArgumentList, Is.EqualTo(new[] { url }));
            HelpersOptions.SupportsCustomBrowser = false;
            Assert.That(URLHelpers.CreateBrowserStartInfo(url).FileName, Is.EqualTo(url));
            HelpersOptions.SupportsCustomBrowser = true;
            HelpersOptions.BrowserPath = "";
            Assert.That(URLHelpers.CreateBrowserStartInfo(url).FileName, Is.EqualTo(url));

            // Local web addresses go to the custom browser too; other links stay with the desktop's opener.
            HelpersOptions.BrowserPath = "/path/browser";
            Assert.That(URLHelpers.UsesCustomBrowser("http://192.168.1.10:8080/file.png"), Is.True);
            Assert.That(URLHelpers.UsesCustomBrowser("http://localhost:3000/"), Is.True);
            Assert.That(URLHelpers.UsesCustomBrowser("mailto:someone@example.com"), Is.False);
            Assert.That(URLHelpers.UsesCustomBrowser("--invalid"), Is.False);
            Assert.That(URLHelpers.CreateBrowserStartInfo("mailto:someone@example.com").FileName, Is.EqualTo("mailto:someone@example.com"));
        }
        finally { HelpersOptions.BrowserPath = old; HelpersOptions.SupportsCustomBrowser = supported; }
    }
}
