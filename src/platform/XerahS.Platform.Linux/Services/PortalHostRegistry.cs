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
using System.Threading.Tasks;
using Tmds.DBus;
using XerahS.Common;

namespace XerahS.Platform.Linux.Services;

/// <summary>
/// Registers the XerahS app ID with xdg-desktop-portal for unsandboxed (host) builds.
/// Since xdg-desktop-portal 1.20 a host app only has an app ID when its systemd unit name carries
/// one (for example when launched from a desktop entry). An AppImage started from a file manager
/// runs in a unit named after its path, so portals such as KDE's GlobalShortcuts reject it with
/// "An app id is required". Registration is per D-Bus connection and must happen before any other
/// portal call on that connection.
/// </summary>
internal static class PortalHostRegistry
{
    /// <summary>
    /// Matches the desktop entry name used by the deb, rpm and AppImage packages (xerahs.desktop), the
    /// X11 WM_CLASS and the Wayland app_id. The portal rejects the registration unless a desktop entry
    /// with this name exists; see <see cref="AppImageDesktopIntegration"/>.
    /// </summary>
    internal const string AppId = "xerahs";

    private const string PortalBusName = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath PortalObjectPath = new("/org/freedesktop/portal/desktop");
    private static readonly Lazy<bool> IsSandboxed = new(() => LinuxRuntimeEnvironment.Detect().IsSandboxed);
    private static bool _failureLogged;

    /// <summary>
    /// Registers <see cref="AppId"/> on the connection. Does nothing inside Flatpak or Snap, where the
    /// sandbox supplies the app ID. Failures are logged and ignored: older portals do not have the
    /// Registry interface, the portal refuses IDs without a desktop entry, and a unit that already
    /// carries a different app ID cannot re-register.
    /// </summary>
    public static async Task RegisterAsync(Connection connection)
    {
        if (IsSandboxed.Value)
            return;

        try
        {
            var registry = connection.CreateProxy<IHostPortalRegistry>(PortalBusName, PortalObjectPath);
            await registry.RegisterAsync(AppId, new Dictionary<string, object>()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                DebugHelper.WriteLine($"PortalHostRegistry: Could not register app ID '{AppId}' with the portal: {ex.Message}");
            }
        }
    }

    public static void Register(Connection connection) => RegisterAsync(connection).GetAwaiter().GetResult();
}

[DBusInterface("org.freedesktop.host.portal.Registry")]
public interface IHostPortalRegistry : IDBusObject
{
    Task RegisterAsync(string appId, IDictionary<string, object> options);
}
