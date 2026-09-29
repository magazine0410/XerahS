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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using XerahS.Common;

namespace XerahS.Platform.Linux.Services;

internal enum AppImageDesktopEntryAction
{
    None,
    Created,
    Updated
}

/// <summary>
/// Installs <c>~/.local/share/applications/xerahs.desktop</c> when XerahS runs from an AppImage.
/// xdg-desktop-portal only accepts the <see cref="PortalHostRegistry"/> registration when it finds a
/// desktop entry for the app ID, and deb/rpm installs provide one in /usr/share/applications.
/// An entry written here carries <see cref="MarkerKey"/>; entries without it are never changed.
/// </summary>
internal static class AppImageDesktopIntegration
{
    internal const string DesktopFileName = PortalHostRegistry.AppId + ".desktop";
    internal const string MarkerKey = "X-XerahS-AppImage-Integration";
    private const string IconRelativePath = "usr/share/icons/hicolor/512x512/apps/xerahs.png";

    public static void EnsureDesktopEntry()
    {
        try
        {
            var action = EnsureDesktopEntry(
                Environment.GetEnvironmentVariable("APPIMAGE"),
                Environment.GetEnvironmentVariable("APPDIR"),
                LinuxXdgDirectories.Detect().DataHome,
                GetDataDirs(Environment.GetEnvironmentVariable("XDG_DATA_DIRS")));

            if (action != AppImageDesktopEntryAction.None)
            {
                DebugHelper.WriteLine($"AppImageDesktopIntegration: {action} {DesktopFileName} for the portal app ID.");
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "AppImageDesktopIntegration: Could not install the desktop entry");
        }
    }

    internal static AppImageDesktopEntryAction EnsureDesktopEntry(
        string? appImagePath,
        string? appDir,
        string dataHome,
        IReadOnlyList<string> dataDirs)
    {
        if (string.IsNullOrWhiteSpace(appImagePath) || !File.Exists(appImagePath))
            return AppImageDesktopEntryAction.None;

        string applicationsDir = Path.Combine(dataHome, "applications");
        string desktopPath = Path.Combine(applicationsDir, DesktopFileName);
        string entry = BuildDesktopEntry(appImagePath);
        var action = AppImageDesktopEntryAction.Created;

        if (File.Exists(desktopPath))
        {
            string existing = File.ReadAllText(desktopPath);
            if (!existing.Contains(MarkerKey, StringComparison.Ordinal) || existing == entry)
                return AppImageDesktopEntryAction.None;

            // Our own entry for an AppImage that was moved or replaced by another version.
            action = AppImageDesktopEntryAction.Updated;
        }
        else if (dataDirs.Any(dir => File.Exists(Path.Combine(dir, "applications", DesktopFileName))))
        {
            // A package already installed the entry the portal needs.
            return AppImageDesktopEntryAction.None;
        }

        Directory.CreateDirectory(applicationsDir);
        string tempPath = desktopPath + ".tmp";
        File.WriteAllText(tempPath, entry);
        File.Move(tempPath, desktopPath, overwrite: true);

        InstallIcon(appDir, dataHome);
        return action;
    }

    internal static string BuildDesktopEntry(string appImagePath)
    {
        var sb = new StringBuilder();
        sb.Append("[Desktop Entry]\n");
        sb.Append("Type=Application\n");
        sb.Append("Name=XerahS\n");
        sb.Append("Comment=Cross-platform screen capture and sharing tool\n");
        sb.Append("GenericName=Screen Capture\n");
        sb.Append("TryExec=").Append(EscapeValue(appImagePath)).Append('\n');
        sb.Append("Exec=").Append(EscapeValue(QuoteExecArgument(appImagePath))).Append(" %U\n");
        sb.Append("Icon=xerahs\n");
        sb.Append("Terminal=false\n");
        sb.Append("Categories=Utility;Graphics;GTK;\n");
        sb.Append("Keywords=screenshot;screen;capture;share;upload;\n");
        sb.Append("StartupWMClass=xerahs\n");
        sb.Append("X-GNOME-UsesNotifications=true\n");
        sb.Append("X-KDE-DBUS-Restricted-Interfaces=org.kde.KWin.ScreenShot2\n");
        sb.Append(MarkerKey).Append("=true\n");
        return sb.ToString();
    }

    internal static IReadOnlyList<string> GetDataDirs(string? xdgDataDirs)
    {
        string value = string.IsNullOrWhiteSpace(xdgDataDirs) ? "/usr/local/share:/usr/share" : xdgDataDirs;
        return value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Desktop Entry spec: reserved characters inside a quoted Exec argument need a backslash.</summary>
    private static string QuoteExecArgument(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in value)
        {
            if (c is '"' or '`' or '$' or '\\')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Desktop Entry spec: string values escape backslashes and control characters.</summary>
    private static string EscapeValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");

    private static void InstallIcon(string? appDir, string dataHome)
    {
        if (string.IsNullOrWhiteSpace(appDir))
            return;

        string source = Path.Combine(appDir, IconRelativePath);
        string target = Path.Combine(dataHome, "icons", "hicolor", "512x512", "apps", "xerahs.png");
        if (!File.Exists(source) || File.Exists(target))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }
}
