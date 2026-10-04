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
using System.Runtime.InteropServices;

namespace XerahS.App;

/// <summary>
/// Avalonia on Linux renders through X11 (XWayland on Wayland sessions). On a pure Wayland
/// session started from autostart, XWayland may not be up yet, or <c>DISPLAY</c> may be unset,
/// and Avalonia fails with <c>XOpenDisplay failed</c>. This class waits briefly for an X display that
/// accepts a connection (from <c>DISPLAY</c>, the systemd user manager's environment, or an X server
/// socket with its authority file), sets it in the process environment, and turns a final failure into a
/// readable message and desktop notification instead of a stack trace (XIP0088 Phase 0 item 6).
/// </summary>
public static class LinuxDisplayBootstrap
{
    public const string X11SocketDirectory = "/tmp/.X11-unix";
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(15);

    public sealed record Result(bool Usable, string? Display, bool DisplayAdopted, string Message);

    /// <summary>
    /// Resolves a usable X display. Pure function over injected environment/file-system access so
    /// every branch is unit tested.
    /// </summary>
    public static Result Resolve(
        Func<string, string?> getEnvironment,
        Func<IEnumerable<string>> listX11Sockets,
        Func<string, bool> socketExists)
    {
        string? display = getEnvironment("DISPLAY");
        if (!string.IsNullOrWhiteSpace(display))
        {
            // Always let Avalonia try an explicit DISPLAY: TCP displays and abstract-namespace
            // sockets have no file under /tmp/.X11-unix, so a missing file is only a hint.
            string? socketPath = GetX11SocketPath(display);
            string note = socketPath != null && !socketExists(socketPath)
                ? $" ({socketPath} not found; the server may use an abstract socket)"
                : string.Empty;
            return new Result(true, display, false, $"Using DISPLAY={display}{note}.");
        }

        string? adopted = GetSocketDisplays(listX11Sockets()).FirstOrDefault();

        if (adopted != null)
        {
            return new Result(true, adopted, true, $"DISPLAY was unset; using X server socket {adopted}.");
        }

        return new Result(false, null, false, "DISPLAY is unset and no X server (XWayland) socket was found.");
    }

    /// <summary>A display to try, with the X authority file it needs (null: none), and where it came from.</summary>
    public sealed record DisplayCandidate(string Display, string? XAuthority, string Source);

    /// <summary>
    /// Finds an X display that accepts a connection and sets <c>DISPLAY</c> and <c>XAUTHORITY</c> for it, also
    /// in the native process environment that Xlib reads. On Wayland sessions it waits up to
    /// <paramref name="maxWait"/>: an autostart entry can start before the session's variables are set, and on
    /// KDE Plasma the XWayland socket alone is not enough, because XWayland requires its authority file.
    /// </summary>
    public static Result EnsureDisplay(TimeSpan? maxWait = null)
    {
        bool isWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
            string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);
        string? originalXAuthority = Environment.GetEnvironmentVariable("XAUTHORITY");
        IReadOnlyDictionary<string, string>? sessionEnvironment = null;

        try
        {
            var (result, chosen) = FindDisplay(maxWait ?? DefaultWait, isWayland, Environment.GetEnvironmentVariable,
                () => sessionEnvironment = ReadSystemdUserEnvironment(), ListSockets,
                () => ListXAuthorityFiles(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")), TryConnect, Thread.Sleep);

            if (chosen != null)
            {
                SetEnvironmentVariable("DISPLAY", chosen.Display);
                SetEnvironmentVariable("XAUTHORITY", chosen.XAuthority);
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) &&
                    sessionEnvironment != null && sessionEnvironment.TryGetValue("WAYLAND_DISPLAY", out string? waylandDisplay))
                {
                    SetEnvironmentVariable("WAYLAND_DISPLAY", waylandDisplay);
                }
            }
            else
            {
                SetNativeEnvironmentVariable("XAUTHORITY", originalXAuthority);
            }

            return result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Without libX11 nothing can be tested; let Avalonia report it.
            SetNativeEnvironmentVariable("XAUTHORITY", originalXAuthority);
            return Resolve(Environment.GetEnvironmentVariable, ListSockets, File.Exists);
        }
    }

    /// <summary>
    /// Tries the candidates until one accepts a connection, repeating every 250 ms on Wayland sessions until
    /// <paramref name="wait"/> has passed. When none does, an explicit <c>DISPLAY</c> is still returned as usable,
    /// so Avalonia reports its own error (as for a remote display).
    /// </summary>
    public static (Result Result, DisplayCandidate? Chosen) FindDisplay(
        TimeSpan wait,
        bool isWayland,
        Func<string, string?> getEnvironment,
        Func<IReadOnlyDictionary<string, string>?> readSessionEnvironment,
        Func<IEnumerable<string>> listX11Sockets,
        Func<IEnumerable<string>> listXAuthorityFiles,
        Func<DisplayCandidate, bool> tryConnect,
        Action<TimeSpan> sleep)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var tried = new HashSet<(string, string?)>();
            foreach (DisplayCandidate candidate in GetCandidates(getEnvironment, readSessionEnvironment, listX11Sockets, listXAuthorityFiles))
            {
                if (!tried.Add((candidate.Display, candidate.XAuthority)) || !tryConnect(candidate))
                {
                    continue;
                }

                string authority = candidate.XAuthority != null ? $" and XAUTHORITY={candidate.XAuthority}" : string.Empty;
                return (new Result(true, candidate.Display, candidate.Source != "DISPLAY",
                    $"Using DISPLAY={candidate.Display}{authority} from {candidate.Source}."), candidate);
            }

            if (!isWayland || stopwatch.Elapsed >= wait)
            {
                break;
            }

            sleep(TimeSpan.FromMilliseconds(250));
        }

        string? display = getEnvironment("DISPLAY");
        if (!string.IsNullOrWhiteSpace(display))
        {
            return (new Result(true, display, false, $"Using DISPLAY={display}; a test connection to it failed."), null);
        }

        return (new Result(false, null, false, isWayland
            ? $"DISPLAY is unset and no X server (XWayland) accepted a connection within {wait.TotalSeconds:0} seconds."
            : "DISPLAY is unset and no X server accepted a connection."), null);
    }

    /// <summary>
    /// The displays to try, in order: the process's <c>DISPLAY</c>; the session's <c>DISPLAY</c> and
    /// <c>XAUTHORITY</c> from the systemd user manager (read only when needed); then each X server socket, without
    /// and with each X authority file in the runtime folder (KWin's <c>xauth_*</c>, Mutter's
    /// <c>.mutter-Xwaylandauth.*</c>), newest first.
    /// </summary>
    public static IEnumerable<DisplayCandidate> GetCandidates(
        Func<string, string?> getEnvironment,
        Func<IReadOnlyDictionary<string, string>?> readSessionEnvironment,
        Func<IEnumerable<string>> listX11Sockets,
        Func<IEnumerable<string>> listXAuthorityFiles)
    {
        string? display = getEnvironment("DISPLAY");
        string? xauthority = NullIfEmpty(getEnvironment("XAUTHORITY"));
        if (!string.IsNullOrWhiteSpace(display))
        {
            yield return new DisplayCandidate(display, xauthority, "DISPLAY");
        }

        IReadOnlyDictionary<string, string>? session = readSessionEnvironment();
        if (session != null && session.TryGetValue("DISPLAY", out string? sessionDisplay) && !string.IsNullOrWhiteSpace(sessionDisplay))
        {
            session.TryGetValue("XAUTHORITY", out string? sessionXAuthority);
            yield return new DisplayCandidate(sessionDisplay, NullIfEmpty(sessionXAuthority), "the systemd user manager's environment");
        }

        List<string>? authorityFiles = null;
        foreach (string socketDisplay in GetSocketDisplays(listX11Sockets()))
        {
            yield return new DisplayCandidate(socketDisplay, xauthority, "the X server socket");
            authorityFiles ??= listXAuthorityFiles().ToList();
            foreach (string file in authorityFiles)
            {
                yield return new DisplayCandidate(socketDisplay, file, "the X server socket");
            }
        }
    }

    /// <summary>Parses <c>systemctl --user show-environment</c> output (KEY=VALUE lines).</summary>
    public static Dictionary<string, string> ParseEnvironment(string output)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n'))
        {
            int separator = line.IndexOf('=');
            if (separator > 0)
            {
                environment[line[..separator]] = line[(separator + 1)..].TrimEnd('\r');
            }
        }

        return environment;
    }

    private static IEnumerable<string> GetSocketDisplays(IEnumerable<string> sockets)
    {
        return sockets
            .Select(Path.GetFileName)
            .Where(name => name != null && name.Length > 1 && name[0] == 'X' && int.TryParse(name.AsSpan(1), out _))
            .OrderBy(name => int.Parse(name!.AsSpan(1)))
            .Select(name => ":" + name![1..]);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static bool _systemctlUnavailable;

    /// <summary>
    /// The systemd user manager's environment, where the session puts <c>DISPLAY</c> and <c>XAUTHORITY</c> for
    /// the applications it starts. Null when systemd is not used.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ReadSystemdUserEnvironment()
    {
        if (_systemctlUnavailable)
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo("systemctl")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--user");
            startInfo.ArgumentList.Add("show-environment");
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(); } catch { }
                return null;
            }

            return process.ExitCode == 0 ? ParseEnvironment(output.GetAwaiter().GetResult()) : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _systemctlUnavailable = true;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> ListXAuthorityFiles(string? runtimeDirectory)
    {
        try
        {
            if (string.IsNullOrEmpty(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            {
                return [];
            }

            return Directory.GetFiles(runtimeDirectory, "xauth_*")
                .Concat(Directory.GetFiles(runtimeDirectory, ".mutter-Xwaylandauth.*"))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool _xlibThreadsInitialized;

    private static bool TryConnect(DisplayCandidate candidate)
    {
        // Avalonia calls XInitThreads, which must come before any other Xlib call; calling it again is harmless.
        if (!_xlibThreadsInitialized)
        {
            XInitThreads();
            _xlibThreadsInitialized = true;
        }

        // Xlib reads XAUTHORITY from the native process environment.
        SetNativeEnvironmentVariable("XAUTHORITY", candidate.XAuthority);
        IntPtr display = XOpenDisplay(candidate.Display);
        if (display == IntPtr.Zero)
        {
            return false;
        }

        XCloseDisplay(display);
        return true;
    }

    private static void SetEnvironmentVariable(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
        SetNativeEnvironmentVariable(name, value);
    }

    /// <summary>
    /// On Linux, Environment.SetEnvironmentVariable changes only .NET's copy of the environment; native libraries
    /// such as Xlib read the process environment, so set it there too.
    /// </summary>
    private static void SetNativeEnvironmentVariable(string name, string? value)
    {
        try
        {
            int status = value == null ? unsetenv(name) : setenv(name, value, 1);
            if (status != 0)
            {
                XerahS.Common.DebugHelper.WriteLine($"Linux display: could not set {name} in the process environment.");
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    [DllImport("libc", SetLastError = true)]
    private static extern int unsetenv(string name);

    [DllImport("libX11.so.6")]
    private static extern int XInitThreads();

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(string? displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    public static string BuildUserMessage(Result result)
    {
        return "XerahS needs an X11 display (XWayland on Wayland sessions) and none is available. " +
            result.Message +
            " Enable XWayland in your compositor (Hyprland: xwayland { enabled = true }) and start XerahS again.";
    }

    /// <summary>Best-effort desktop notification; the tray does not exist yet at this point.</summary>
    public static void NotifyStartupFailure(string message)
    {
        try
        {
            var startInfo = new ProcessStartInfo("notify-send")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add("--app-name=XerahS");
            startInfo.ArgumentList.Add("--urgency=critical");
            startInfo.ArgumentList.Add("XerahS could not start");
            startInfo.ArgumentList.Add(message);
            using var process = Process.Start(startInfo);
            process?.WaitForExit(2000);
        }
        catch
        {
            // notify-send is optional; the log and stderr already carry the message.
        }
    }

    public static string? GetX11SocketPath(string display)
    {
        string value = display.Trim();
        int colonIndex = value.LastIndexOf(':');
        if (colonIndex < 0 || colonIndex == value.Length - 1)
        {
            return null;
        }

        if (colonIndex > 0 && !value.StartsWith("unix", StringComparison.Ordinal))
        {
            // host:N is a TCP display.
            return null;
        }

        string token = value[(colonIndex + 1)..];
        int dotIndex = token.IndexOf('.');
        if (dotIndex >= 0)
        {
            token = token[..dotIndex];
        }

        return int.TryParse(token, out int number) ? $"{X11SocketDirectory}/X{number}" : null;
    }

    private static IEnumerable<string> ListSockets()
    {
        try
        {
            return Directory.Exists(X11SocketDirectory)
                ? Directory.GetFileSystemEntries(X11SocketDirectory)
                : [];
        }
        catch
        {
            return [];
        }
    }
}
