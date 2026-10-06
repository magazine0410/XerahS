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

using System.Text.RegularExpressions;
using XerahS.Common;

namespace XerahS.Core.Managers;

/// <summary>ShareX's opt-in, count-based cleanup, including XerahS's monthly archive layout.</summary>
public static class SettingsCleanupService
{
    public static void Cleanup(ApplicationConfig settings, string backupFolder, string logsFolder, string? activeLog = null)
    {
        int keep = Math.Max(0, settings.CleanupKeepFileCount);
        if (settings.AutoCleanupBackupFiles)
        {
            Prune(backupFolder, keep, name => Regex.IsMatch(name, @"^backup-\d{4}-\d{2}-\d{2}-.+\.zip$"));
            Prune(backupFolder, keep, name => Regex.IsMatch(name, @"^backup-\d{4}-W\d{2}-.+\.zip$"));
            foreach (string prefix in new[] { "ApplicationConfig", "WorkflowsConfig", "HotkeysConfig", "UploadersConfig", "History" })
                Prune(backupFolder, keep, name => name.StartsWith(prefix + "-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal));
        }
        if (settings.AutoCleanupLogFiles)
        {
            Prune(logsFolder, keep, name => Regex.IsMatch(name, @"^XerahS-\d{8}\.log$"), activeLog);
            Prune(logsFolder, keep, name => Regex.IsMatch(name, @"^XerahS-errors-\d{8}\.log$"), PathsManager.GetErrorLogFilePath());
            Prune(logsFolder, keep, name => Regex.IsMatch(name, @"^XerahS-Log-.*\.txt$"), activeLog);
            // The network monitor writes its own daily log next to the main one.
            Prune(logsFolder, keep, name => Regex.IsMatch(name, @"^NetworkMonitor-\d{8}\.log$"),
                XerahS.Common.NetworkMonitor.NetworkMonitorEventLog.GetDefaultPath());
        }
    }

    private static void Prune(string folder, int keep, Func<string, bool> matches, string? activeFile = null)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            // Only the root and month folders belong to the settings/logger archive layout.
            var directories = new[] { new DirectoryInfo(folder) }.Concat(new DirectoryInfo(folder).EnumerateDirectories()
                .Where(d => Regex.IsMatch(d.Name, @"^\d{4}-\d{2}$") && !d.Attributes.HasFlag(FileAttributes.ReparsePoint)));
            var files = directories.SelectMany(d => d.EnumerateFiles())
                .Where(f => matches(f.Name) && !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .Skip(keep).ToArray();
            foreach (FileInfo file in files)
            {
                if (string.Equals(file.FullName, activeFile, StringComparison.Ordinal)) continue;
                try { file.Delete(); }
                catch (Exception ex) { DebugHelper.WriteException(ex, "Settings cleanup: " + file.Name); }
            }
        }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Settings cleanup"); }
    }
}
