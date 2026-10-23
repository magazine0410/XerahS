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

namespace XerahS.Platform.Linux.Services;

/// <summary>
/// Progress on XerahS's taskbar button (ShareX's TaskbarManager), through the Unity launcher API that KDE Plasma's
/// task manager, Dash to Dock, and Plank show: a com.canonical.Unity.LauncherEntry.Update signal naming the
/// application's desktop file.
/// </summary>
internal sealed class LinuxTaskbarProgress
{
    private readonly object _lock = new();
    private readonly string _appUri;
    private Task<LauncherEntry?>? _entry;

    public LinuxTaskbarProgress(string desktopFileId)
    {
        _appUri = "application://" + desktopFileId;
    }

    /// <summary>The desktop file the taskbar knows XerahS by: the Flatpak id inside Flatpak, otherwise xerahs.desktop.</summary>
    public static string GetDesktopFileId()
    {
        LinuxRuntimeEnvironment environment = LinuxRuntimeEnvironment.Detect();
        string appId = environment.SandboxKind == LinuxSandboxKind.Flatpak && !string.IsNullOrWhiteSpace(environment.AppId)
            ? environment.AppId
            : PortalHostRegistry.AppId;
        return appId + ".desktop";
    }

    /// <summary>Shows <paramref name="progress"/> percent, or no progress when it is null.</summary>
    public void SetProgress(int? progress)
    {
        var properties = new Dictionary<string, object>
        {
            ["progress"] = Math.Clamp(progress ?? 0, 0, 100) / 100d,
            ["progress-visible"] = progress.HasValue
        };
        _ = SendAsync(properties);
    }

    private async Task SendAsync(IDictionary<string, object> properties)
    {
        try
        {
            Task<LauncherEntry?> entryTask;
            lock (_lock)
            {
                entryTask = _entry ??= CreateAsync();
            }

            LauncherEntry? entry = await entryTask.ConfigureAwait(false);
            entry?.Update(_appUri, properties);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Unable to show the progress on the taskbar");
        }
    }

    private static async Task<LauncherEntry?> CreateAsync()
    {
        try
        {
            var connection = new Connection(Address.Session);
            await connection.ConnectAsync().ConfigureAwait(false);
            var entry = new LauncherEntry();
            await connection.RegisterObjectAsync(entry).ConfigureAwait(false);
            return entry;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Unable to connect to the session bus for taskbar progress");
            return null;
        }
    }

    private sealed class LauncherEntry : ILauncherEntry
    {
        public ObjectPath ObjectPath { get; } = new("/com/canonical/unity/launcherentry/xerahs");

        public event Action<(string AppUri, IDictionary<string, object> Properties)>? OnUpdate;

        public Task<IDisposable> WatchUpdateAsync(Action<(string AppUri, IDictionary<string, object> Properties)> handler, Action<Exception>? onError = null) =>
            SignalWatcher.AddAsync(this, nameof(OnUpdate), handler);

        public void Update(string appUri, IDictionary<string, object> properties) => OnUpdate?.Invoke((appUri, properties));
    }
}

[DBusInterface("com.canonical.Unity.LauncherEntry")]
public interface ILauncherEntry : IDBusObject
{
    Task<IDisposable> WatchUpdateAsync(Action<(string AppUri, IDictionary<string, object> Properties)> handler, Action<Exception>? onError = null);
}
