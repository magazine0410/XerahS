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

using Tmds.DBus;
using XerahS.Common;

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>Runs Plasma shell scripts (the API behind the Plasma desktop scripting console).</summary>
public interface IPlasmaShellScripting
{
    /// <summary>Whether this is a KDE Plasma session that the shell can be reached in.</summary>
    bool IsAvailable { get; }

    /// <summary>Evaluates the script and returns what it printed, or null when the shell did not answer.</summary>
    Task<string?> EvaluateAsync(string script, CancellationToken token);
}

/// <summary>
/// Plasma shell scripting over D-Bus (<c>org.kde.plasmashell /PlasmaShell org.kde.PlasmaShell.evaluateScript</c>),
/// which is how the panels' hiding modes and a Folder View's filter can be changed while Plasma runs.
/// </summary>
internal sealed class PlasmaShellScripting : IPlasmaShellScripting
{
    private const string BusName = "org.kde.plasmashell";
    private static readonly ObjectPath ShellPath = new("/PlasmaShell");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>How long a failed connection is kept before the next call tries again (Plasma's shell may start later).</summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private readonly object _shellLock = new();
    private Task<IPlasmaShell?>? _shell;
    private DateTime _retryAfter;

    public bool IsAvailable =>
        OperatingSystem.IsLinux() && KdeTemporaryShortcut.IsKdeSession() && !LinuxRuntimeEnvironment.Detect().IsSandboxed;

    public async Task<string?> EvaluateAsync(string script, CancellationToken token)
    {
        if (!IsAvailable) return null;
        IPlasmaShell? shell = await GetShellAsync().ConfigureAwait(false);
        if (shell == null) return null;
        try
        {
            return await shell.evaluateScriptAsync(script).WaitAsync(Timeout, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DebugHelper.WriteLine($"PlasmaShellScripting: the script failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>The shell's proxy, connected once; a failed connection is tried again after <see cref="RetryInterval"/>.</summary>
    private Task<IPlasmaShell?> GetShellAsync()
    {
        lock (_shellLock)
        {
            bool failed = _shell is { IsCompletedSuccessfully: true, Result: null };
            if (_shell == null || (failed && DateTime.UtcNow >= _retryAfter))
            {
                _shell = CreateAsync();
                _retryAfter = DateTime.UtcNow + RetryInterval;
            }

            return _shell;
        }
    }

    private static async Task<IPlasmaShell?> CreateAsync()
    {
        Connection? connection = null;
        try
        {
            connection = new Connection(Address.Session);
            await connection.ConnectAsync().ConfigureAwait(false);
            var shell = connection.CreateProxy<IPlasmaShell>(BusName, ShellPath);
            await shell.evaluateScriptAsync("print('xerahs');").WaitAsync(Timeout).ConfigureAwait(false);
            DebugHelper.WriteLine("PlasmaShellScripting: Plasma shell scripting is available.");
            return shell;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"PlasmaShellScripting: Plasma shell scripting is unavailable: {ex.Message}");
            connection?.Dispose();
            return null;
        }
    }
}

[DBusInterface("org.kde.PlasmaShell")]
public interface IPlasmaShell : IDBusObject
{
    Task<string> evaluateScriptAsync(string script);
}
