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

using System.Diagnostics;
using System.IO;
using System.Text;
using XerahS.Common;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux.Services;

public sealed class LinuxStartupService : IStartupService
{
    private readonly string _desktopFilePath;
    private readonly string _executablePath;

    public LinuxStartupService()
        : this(Path.Combine(LinuxXdgDirectories.Detect().ConfigHome, "autostart"),
            ResolveExecutablePath(Environment.GetEnvironmentVariable("APPIMAGE"), GetProcessPath()))
    {
    }

    internal LinuxStartupService(string autostartFolder, string? executablePath)
    {
        Directory.CreateDirectory(autostartFolder);

        _desktopFilePath = Path.Combine(autostartFolder, $"{AppResources.AppName}.desktop");
        _executablePath = executablePath ?? string.Empty;
    }

    /// <summary>
    /// Rewrites an existing autostart entry that does not match this executable, so entries written by older
    /// versions (without <c>-silent</c>) or by an AppImage started from another place keep working.
    /// Returns true when the entry was rewritten.
    /// </summary>
    public bool RefreshEntry()
    {
        try
        {
            if (string.IsNullOrEmpty(_executablePath) || !File.Exists(_desktopFilePath))
            {
                return false;
            }

            string entry = BuildDesktopEntry(_executablePath);
            if (File.ReadAllText(_desktopFilePath, Encoding.UTF8) == entry)
            {
                return false;
            }

            File.WriteAllText(_desktopFilePath, entry, Encoding.UTF8);
            DebugHelper.WriteLine("LinuxStartupService: Updated the autostart entry.");
            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "LinuxStartupService: Failed to update the autostart entry");
            return false;
        }
    }

    public bool IsRunAtStartupEnabled()
    {
        return File.Exists(_desktopFilePath);
    }

    public bool SetRunAtStartup(bool enable)
    {
        try
        {
            if (enable)
            {
                if (string.IsNullOrEmpty(_executablePath))
                {
                    DebugHelper.WriteLine("LinuxStartupService: Executable path is empty.");
                    return false;
                }

                var directory = Path.GetDirectoryName(_desktopFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_desktopFilePath, BuildDesktopEntry(_executablePath), Encoding.UTF8);
                return true;
            }

            if (File.Exists(_desktopFilePath))
            {
                File.Delete(_desktopFilePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "LinuxStartupService: Failed to update autostart entry");
            return false;
        }
    }

    internal static string BuildDesktopEntry(string executablePath)
    {
        // As in ShareX, the startup entry starts XerahS in the tray.
        string exec = $"\"{EscapeQuotedDesktopEntryArgument(executablePath)}\" {AppContracts.Cli.SilentStartupFlag}";
        var builder = new StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine("Type=Application");
        builder.AppendLine($"Name={AppResources.AppName}");
        builder.AppendLine($"Exec={exec}");
        builder.AppendLine("Terminal=false");
        builder.AppendLine("Hidden=false");
        builder.AppendLine("X-GNOME-Autostart-enabled=true");
        builder.AppendLine("NoDisplay=false");
        builder.AppendLine("X-KDE-DBUS-Restricted-Interfaces=org.kde.KWin.ScreenShot2");
        builder.AppendLine($"Comment=Auto-start {AppResources.AppName}");
        return builder.ToString();
    }

    internal static string EscapeQuotedDesktopEntryArgument(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// The AppImage file itself when running from an AppImage: the running executable is inside the AppImage's
    /// temporary mount folder, which no longer exists after the AppImage exits.
    /// </summary>
    internal static string? ResolveExecutablePath(string? appImagePath, string? processPath)
    {
        return !string.IsNullOrWhiteSpace(appImagePath) && File.Exists(appImagePath) ? appImagePath : processPath;
    }

    private static string? GetProcessPath()
    {
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
    }
}
