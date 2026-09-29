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

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>
/// The parts of KDE's kglobalaccel registry that XerahS uses. Shortcut keys are Qt key combinations;
/// 0 means no shortcut.
/// </summary>
internal interface IKdeGlobalAccel
{
    Task<IReadOnlyList<string>> GetActionIdsAsync(string component);

    Task<int> GetKeyAsync(string component, string action);

    Task SetKeyAsync(string component, string action, string actionFriendlyName, int key);

    Task UnregisterAsync(string component, string action);
}

/// <summary>
/// Talks to org.kde.kglobalaccel, the registry behind KDE System Settings → Shortcuts. The portal
/// registers XerahS shortcuts there under the component named after the app ID, with the portal
/// shortcut IDs as action names. Changing keys through setForeignShortcutKeys is what System
/// Settings does; kglobalaccel then notifies the owner (the portal), which delivers the new keys.
/// </summary>
internal sealed class KdeGlobalAccel : IKdeGlobalAccel
{
    private const string BusName = "org.kde.kglobalaccel";
    private const string ComponentFriendlyName = "XerahS";
    private static readonly ObjectPath RegistryPath = new("/kglobalaccel");

    private readonly IKGlobalAccelRegistry _registry;

    private KdeGlobalAccel(IKGlobalAccelRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Returns a client when the session runs KDE and kglobalaccel answers; otherwise null.</summary>
    public static async Task<KdeGlobalAccel?> TryCreateAsync(Connection connection, string component)
    {
        string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
        if (!desktop.Split(':').Any(part => part.Equals("KDE", StringComparison.OrdinalIgnoreCase)))
            return null;

        try
        {
            var registry = connection.CreateProxy<IKGlobalAccelRegistry>(BusName, RegistryPath);
            await registry.allActionsForComponentAsync(ActionId(component, string.Empty, string.Empty)).ConfigureAwait(false);
            return new KdeGlobalAccel(registry);
        }
        catch (Exception ex)
        {
            Common.DebugHelper.WriteLine($"KdeGlobalAccel: kglobalaccel unavailable, KDE shortcut sync disabled: {ex.Message}");
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetActionIdsAsync(string component)
    {
        string[][] actions = await _registry.allActionsForComponentAsync(ActionId(component, string.Empty, string.Empty)).ConfigureAwait(false);
        return actions.Where(action => action.Length > 1).Select(action => action[1]).ToArray();
    }

    public async Task<int> GetKeyAsync(string component, string action)
    {
        var sequences = await _registry.shortcutKeysAsync(ActionId(component, action, string.Empty)).ConfigureAwait(false);
        // Each shortcut is a key sequence of up to four combinations; XerahS hotkeys are single combinations.
        return sequences.Length > 0 && sequences[0].Item1.Length > 0 ? sequences[0].Item1[0] : 0;
    }

    public Task SetKeyAsync(string component, string action, string actionFriendlyName, int key)
    {
        var sequences = key == 0
            ? Array.Empty<ValueTuple<int[]>>()
            : new[] { ValueTuple.Create(new[] { key, 0, 0, 0 }) };
        return _registry.setForeignShortcutKeysAsync(ActionId(component, action, actionFriendlyName), sequences);
    }

    public async Task UnregisterAsync(string component, string action)
    {
        await _registry.unregisterAsync(component, action).ConfigureAwait(false);
    }

    private static string[] ActionId(string component, string action, string actionFriendlyName) =>
        new[] { component, action, ComponentFriendlyName, actionFriendlyName };
}

// Member names match kglobalaccel's lower-camel-case D-Bus methods (Tmds.DBus uses the method name).
[DBusInterface("org.kde.KGlobalAccel")]
public interface IKGlobalAccelRegistry : IDBusObject
{
    Task<string[][]> allActionsForComponentAsync(string[] actionId);

    Task<ValueTuple<int[]>[]> shortcutKeysAsync(string[] actionId);

    Task setForeignShortcutKeysAsync(string[] actionId, ValueTuple<int[]>[] keys);

    Task<bool> unregisterAsync(string componentUnique, string shortcutUnique);
}
