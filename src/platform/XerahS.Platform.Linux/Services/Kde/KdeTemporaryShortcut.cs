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

/// <summary>The kglobalaccel calls that a temporary shortcut needs.</summary>
internal interface IKdeShortcutRegistry : IDisposable
{
    /// <summary>Returns false when KDE did not assign the key, for example because another shortcut uses it.</summary>
    Task<bool> RegisterAsync(string component, string componentFriendlyName, string action, string actionFriendlyName, int qtKey);

    Task<IDisposable> WatchPressedAsync(string component, Action<string> pressed);

    Task<IReadOnlyList<string>> GetActionIdsAsync(string component);

    Task UnregisterAsync(string component, string action);

    Task CleanUpAsync(string component);
}

/// <summary>
/// Binds a key in KDE's global shortcut service only while an operation runs, such as Escape to stop a
/// scrolling capture, when hotkeys go through the GlobalShortcuts portal (where a binding would stay in
/// the desktop's settings). The binding is in its own component, apart from the portal's XerahS
/// shortcuts, and each one has a unique action name, so removing one never removes the next. KDE keeps
/// a binding after its owner exits, so bindings left by a crash are removed at the next start, before
/// a new one is made.
/// </summary>
internal sealed class KdeTemporaryShortcut : IDisposable
{
    internal const string Component = "xerahs_temporary_shortcuts";
    private const string ComponentFriendlyName = "XerahS temporary shortcuts";
    private static readonly TimeSpan RemovalWait = TimeSpan.FromSeconds(2);
    private static readonly Lazy<Task> StaleRemoval = new(() => Task.Run(RemoveStaleAsync));

    private readonly IKdeShortcutRegistry _registry;
    private readonly string _action;
    private readonly IDisposable _subscription;
    private int _disposed;

    private KdeTemporaryShortcut(IKdeShortcutRegistry registry, string action, IDisposable subscription)
    {
        _registry = registry;
        _action = action;
        _subscription = subscription;
    }

    public static bool IsKdeSession() =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
            .Split(':').Any(part => part.Equals("KDE", StringComparison.OrdinalIgnoreCase));

    /// <summary>Starts removing bindings left by an earlier process; later registrations wait for it.</summary>
    public static Task EnsureStaleRemovedAsync() => StaleRemoval.Value;

    public static async Task<KdeTemporaryShortcut?> TryRegisterAsync(int qtKey, string friendlyName, Action pressed)
    {
        if (qtKey == 0 || !IsKdeSession())
        {
            return null;
        }

        await EnsureStaleRemovedAsync().ConfigureAwait(false);
        var registry = await DBusKdeShortcutRegistry.ConnectAsync().ConfigureAwait(false);
        if (registry == null)
        {
            return null;
        }

        var shortcut = await TryRegisterAsync(registry, qtKey, friendlyName, pressed).ConfigureAwait(false);
        if (shortcut == null)
        {
            registry.Dispose();
        }

        return shortcut;
    }

    internal static async Task<KdeTemporaryShortcut?> TryRegisterAsync(IKdeShortcutRegistry registry, int qtKey, string friendlyName, Action pressed)
    {
        string action = "key-" + Guid.NewGuid().ToString("N");
        try
        {
            IDisposable subscription = await registry.WatchPressedAsync(Component, pressedAction =>
            {
                if (pressedAction == action) pressed();
            }).ConfigureAwait(false);

            if (await registry.RegisterAsync(Component, ComponentFriendlyName, action, friendlyName, qtKey).ConfigureAwait(false))
            {
                return new KdeTemporaryShortcut(registry, action, subscription);
            }

            DebugHelper.WriteLine($"KdeTemporaryShortcut: KDE did not assign {friendlyName}; it may be used by another global shortcut.");
            subscription.Dispose();
            await RemoveAsync(registry, action).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"KdeTemporaryShortcut: Could not bind {friendlyName}: {ex.Message}");
            await RemoveAsync(registry, action).ConfigureAwait(false);
            return null;
        }
    }

    internal static async Task RemoveStaleAsync(IKdeShortcutRegistry registry)
    {
        IReadOnlyList<string> actions = await registry.GetActionIdsAsync(Component).ConfigureAwait(false);
        foreach (string action in actions)
        {
            await registry.UnregisterAsync(Component, action).ConfigureAwait(false);
        }

        if (actions.Count > 0)
        {
            DebugHelper.WriteLine($"KdeTemporaryShortcut: Removed {actions.Count} binding(s) left by an earlier XerahS process.");
            await registry.CleanUpAsync(Component).ConfigureAwait(false);
        }
    }

    private static async Task RemoveStaleAsync()
    {
        if (!IsKdeSession())
        {
            return;
        }

        try
        {
            using var registry = await DBusKdeShortcutRegistry.ConnectAsync().ConfigureAwait(false);
            if (registry != null)
            {
                await RemoveStaleAsync(registry).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"KdeTemporaryShortcut: Could not remove earlier bindings: {ex.Message}");
        }
    }

    private static async Task RemoveAsync(IKdeShortcutRegistry registry, string action)
    {
        try
        {
            await registry.UnregisterAsync(Component, action).ConfigureAwait(false);
            await registry.CleanUpAsync(Component).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"KdeTemporaryShortcut: Could not remove a binding: {ex.Message}");
        }
    }

    /// <summary>
    /// Waits briefly for the removal, so the key is free again when this returns, even if the
    /// process exits next. The D-Bus calls do not need the calling thread.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _subscription.Dispose();
        var removal = RemoveAsync(_registry, _action).ContinueWith(_ => _registry.Dispose(), TaskScheduler.Default);
        if (!removal.Wait(RemovalWait))
        {
            DebugHelper.WriteLine("KdeTemporaryShortcut: Removing the binding is taking long; it continues in the background.");
        }
    }

    private sealed class DBusKdeShortcutRegistry(Connection connection, IKGlobalAccelRegistry registry) : IKdeShortcutRegistry
    {
        private const string BusName = "org.kde.kglobalaccel";
        private const uint SetPresent = 2;
        private const uint NoAutoloading = 4;
        private static readonly ObjectPath RegistryPath = new("/kglobalaccel");

        public static async Task<DBusKdeShortcutRegistry?> ConnectAsync()
        {
            var connection = new Connection(Address.Session);
            try
            {
                await connection.ConnectAsync().ConfigureAwait(false);
                return new DBusKdeShortcutRegistry(connection, connection.CreateProxy<IKGlobalAccelRegistry>(BusName, RegistryPath));
            }
            catch (Exception ex)
            {
                DebugHelper.WriteLine($"KdeTemporaryShortcut: kglobalaccel unavailable: {ex.Message}");
                connection.Dispose();
                return null;
            }
        }

        public async Task<bool> RegisterAsync(string component, string componentFriendlyName, string action, string actionFriendlyName, int qtKey)
        {
            string[] actionId = [component, action, componentFriendlyName, actionFriendlyName];
            await registry.doRegisterAsync(actionId).ConfigureAwait(false);
            // NoAutoloading: use this key, not one stored for the action.
            var assigned = await registry.setShortcutKeysAsync(actionId, [ValueTuple.Create(new[] { qtKey, 0, 0, 0 })], SetPresent | NoAutoloading).ConfigureAwait(false);
            return assigned.Length > 0 && assigned[0].Item1.Length > 0 && assigned[0].Item1[0] == qtKey;
        }

        public Task<IDisposable> WatchPressedAsync(string component, Action<string> pressed) =>
            // A signal can be watched before the component object exists, so no press is missed.
            connection.CreateProxy<IKGlobalAccelComponent>(BusName, ComponentPath(component))
                .WatchglobalShortcutPressedAsync(args => pressed(args.shortcutUnique));

        public async Task<IReadOnlyList<string>> GetActionIdsAsync(string component)
        {
            string[][] actions = await registry.allActionsForComponentAsync([component, string.Empty, string.Empty, string.Empty]).ConfigureAwait(false);
            return actions.Where(action => action.Length > 1).Select(action => action[1]).ToArray();
        }

        public Task UnregisterAsync(string component, string action) => registry.unregisterAsync(component, action);

        public async Task CleanUpAsync(string component)
        {
            try
            {
                await connection.CreateProxy<IKGlobalAccelComponent>(BusName, ComponentPath(component)).cleanUpAsync().ConfigureAwait(false);
            }
            catch (DBusException)
            {
                // The component was already removed with its last action.
            }
        }

        public void Dispose() => connection.Dispose();

        // kglobalaccel names a component's object "/component/" plus its name, with characters other than
        // letters, digits, and underscores replaced. XerahS's component name has none of them.
        private static ObjectPath ComponentPath(string component) => new("/component/" + component);
    }
}
