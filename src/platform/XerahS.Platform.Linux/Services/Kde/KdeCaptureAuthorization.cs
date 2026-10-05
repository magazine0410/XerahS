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
using System.Security.Cryptography;
using System.Text;
using XerahS.Common;

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>
/// KWin matches a caller's executable against desktop entries. An AppImage launcher names the
/// persistent image, while the process runs inside a changing mount. Keep a hidden entry for
/// that executable for the lifetime of this process, independently of the visible launcher.
/// </summary>
internal static class KdeCaptureAuthorization
{
    private const string Marker = "X-XerahS-Capture-Authorization=true";
    private const string Prefix = "xerahs-capture-";
    private static readonly Lazy<Task> Initialization = new(InitializeAsync);

    public static bool HasRegistration { get; private set; }

    public static Task EnsureAsync() => Initialization.Value;

    private static async Task InitializeAsync()
    {
        try
        {
            var registration = Register(Environment.GetEnvironmentVariable("APPIMAGE"),
                Environment.GetEnvironmentVariable("APPDIR"), Environment.ProcessPath,
                LinuxXdgDirectories.Detect().DataHome, Environment.ProcessId);
            if (registration == null) return;
            HasRegistration = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => registration.Dispose();

            // KWin uses KService's cache. Wait for the new entry to be indexed before the first
            // capture, rather than racing the filesystem watcher and falling back without alpha.
            using var process = Process.Start(new ProcessStartInfo("kbuildsycoca6")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            if (process != null)
            {
                Task output = process.StandardOutput.ReadToEndAsync();
                Task error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { process.Kill(entireProcessTree: true); }
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "KdeCaptureAuthorization: Could not register the mounted AppImage executable");
        }
    }

    internal static Registration? Register(string? appImage, string? appDir, string? executable,
        string dataHome, int processId)
    {
        if (string.IsNullOrWhiteSpace(appImage) || !File.Exists(appImage) ||
            string.IsNullOrWhiteSpace(appDir) || string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return null;

        string directory = Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        executable = Path.GetFullPath(executable);
        // Never grant capture access to a shared dotnet host or an executable outside this image.
        if (!executable.StartsWith(directory, StringComparison.Ordinal)) return null;

        string applications = Path.Combine(dataHome, "applications");
        Directory.CreateDirectory(applications);
        foreach (string stale in Directory.EnumerateFiles(applications, Prefix + "*.desktop"))
        {
            string content = File.ReadAllText(stale);
            string? mountedPath = content.Split('\n').FirstOrDefault(line => line.StartsWith("X-XerahS-Executable=", StringComparison.Ordinal))?[20..];
            if (content.Split('\n').Contains(Marker) && mountedPath != null && !File.Exists(mountedPath))
                File.Delete(stale);
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable)))[..16];
        string path = Path.Combine(applications, $"{Prefix}{processId}-{hash}.desktop");
        string entry = AppImageDesktopIntegration.BuildCaptureAuthorizationEntry(executable);
        // The raw path is only used to identify expired mounts; paths with control characters are
        // valid Exec arguments but are deliberately excluded from stale-entry cleanup metadata.
        if (!executable.Any(char.IsControl)) entry += "X-XerahS-Executable=" + executable + "\n";
        if (File.Exists(path) && !File.ReadAllLines(path).Contains(Marker)) return null;
        string temp = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, entry);
            File.Move(temp, path, overwrite: true);
        }
        finally { File.Delete(temp); }
        return new Registration(path, entry);
    }

    internal sealed class Registration(string path, string content) : IDisposable
    {
        internal string Path { get; } = path;
        public void Dispose()
        {
            try
            {
                if (File.Exists(Path) && File.ReadAllText(Path) == content) File.Delete(Path);
            }
            catch (Exception ex) { DebugHelper.WriteLine($"KdeCaptureAuthorization: Cleanup failed: {ex.Message}"); }
        }
    }
}

/// <summary>
/// KWin can refuse the first captures after registration, until its service cache includes the new entry.
/// Refusals are retried only until one capture is authorized or one capture has used every retry, so a
/// desktop that never accepts the entry does not delay each later capture before its fallback.
/// </summary>
internal sealed class KdeAuthorizationRetry(int maxRetries = 4)
{
    private volatile bool _settled;

    public bool ShouldRetry(bool hasRegistration, int attempt)
    {
        if (!hasRegistration || _settled) return false;
        if (attempt < maxRetries) return true;
        _settled = true;
        return false;
    }

    public void ReportAuthorized() => _settled = true;

    public static TimeSpan GetDelay(int attempt) => TimeSpan.FromMilliseconds(100 << attempt);
}
