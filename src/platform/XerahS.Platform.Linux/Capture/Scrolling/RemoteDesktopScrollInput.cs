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

using System.Drawing;
using Tmds.DBus;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using Point = System.Drawing.Point;
using XerahS.Platform.Linux.Capture.Detection;
using XerahS.Platform.Linux.Services;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Platform.Linux.Capture.Scrolling;

/// <summary>
/// Wayland: input through the RemoteDesktop portal. The first session asks for permission; when the
/// user allows restoring, the portal returns a token that later sessions pass back to skip the dialog.
/// </summary>
internal sealed class RemoteDesktopScrollInput : IScrollInput
{
    internal const string InterfaceName = "org.freedesktop.portal.RemoteDesktop";
    private const string PortalBusName = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath PortalObjectPath = new("/org/freedesktop/portal/desktop");
    private const uint KeyboardDevice = 1;
    private const uint PointerDevice = 2;
    private const uint PersistUntilRevoked = 2;
    private const uint VerticalAxis = 0;
    private const uint Released = 0;
    private const uint Pressed = 1;

    // KDE's portal passes a discrete scroll's step count to KWin as the raw scroll value, and one
    // wheel notch is 15 there. Other portals count steps as notches, as the portal specification says.
    private readonly int _wheelStepsPerNotch = string.Equals(DesktopEnvironmentDetector.Detect(), "KDE", StringComparison.Ordinal) ? 15 : 1;
    private readonly Func<string?> _loadRestoreToken;
    private readonly Action<string?> _saveRestoreToken;
    private Connection? _connection;
    private IRemoteDesktopPortal? _portal;
    private ObjectPath _session;
    private bool _hasSession;
    private IDisposable? _closedSubscription;
    private volatile bool _closed;

    public RemoteDesktopScrollInput(Func<string?> loadRestoreToken, Action<string?> saveRestoreToken)
    {
        _loadRestoreToken = loadRestoreToken;
        _saveRestoreToken = saveRestoreToken;
    }

    public async Task<bool> BeginAsync(CancellationToken cancellationToken)
    {
        try
        {
            _connection = new Connection(Address.Session);
            ConnectionInfo info = await _connection.ConnectAsync().ConfigureAwait(false);
            await PortalHostRegistry.RegisterAsync(_connection).ConfigureAwait(false);
            PortalRequestExtensions.CacheLocalConnectionName(_connection, info);
            _portal = _connection.CreateProxy<IRemoteDesktopPortal>(PortalBusName, PortalObjectPath);

            var properties = _connection.CreateProxy<IDBusProperties>(PortalBusName, PortalObjectPath);
            uint version = Convert.ToUInt32(await properties.GetAsync(InterfaceName, "version").ConfigureAwait(false));
            uint available = Convert.ToUInt32(await properties.GetAsync(InterfaceName, "AvailableDeviceTypes").ConfigureAwait(false));
            if (!HasRequiredDevices(available)) return false;

            var createOptions = new Dictionary<string, object> { ["session_handle_token"] = $"xerahs_scroll_{Guid.NewGuid():N}" };
            var (createResponse, createResults) = await _connection.SendPortalRequestAsync(PortalBusName, createOptions,
                () => _portal.CreateSessionAsync(createOptions), cancellationToken).ConfigureAwait(false);
            if (createResponse != 0 || !createResults.TryGetResult("session_handle", out string? sessionPath) || string.IsNullOrEmpty(sessionPath))
            {
                DebugHelper.WriteLine($"RemoteDesktopScrollInput: CreateSession failed (response={createResponse}).");
                return false;
            }

            _session = new ObjectPath(sessionPath);
            _hasSession = true;
            _closedSubscription = await _connection.CreateProxy<IPortalSession>(PortalBusName, _session)
                .WatchClosedAsync(_ => _closed = true).ConfigureAwait(false);
            string? restoreToken = version >= 2 ? _loadRestoreToken() : null;
            var selectOptions = CreateSelectOptions(version, restoreToken);
            if (!string.IsNullOrEmpty(restoreToken)) _saveRestoreToken(null);

            var (selectResponse, _) = await _connection.SendPortalRequestAsync(PortalBusName, selectOptions,
                () => _portal.SelectDevicesAsync(_session, selectOptions), cancellationToken).ConfigureAwait(false);
            if (selectResponse != 0)
            {
                DebugHelper.WriteLine($"RemoteDesktopScrollInput: SelectDevices failed (response={selectResponse}).");
                return false;
            }

            var startOptions = new Dictionary<string, object>();
            var (startResponse, startResults) = await _connection.SendPortalRequestAsync(PortalBusName, startOptions,
                () => _portal.StartAsync(_session, string.Empty, startOptions), cancellationToken).ConfigureAwait(false);
            if (startResponse != 0)
            {
                DebugHelper.WriteLine($"RemoteDesktopScrollInput: Start was refused or cancelled (response={startResponse}).");
                return false;
            }

            if (!startResults.TryGetResult("devices", out uint granted) || !HasRequiredDevices(granted) || _closed)
            {
                DebugHelper.WriteLine("RemoteDesktopScrollInput: The session did not grant both keyboard and pointer access.");
                return false;
            }

            // A token is single use; the portal returns a new one for the next session, or none
            // when the user did not allow restoring.
            _saveRestoreToken(startResults.TryGetResult("restore_token", out string? newToken) ? newToken : null);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"RemoteDesktopScrollInput: Could not start a remote desktop session: {ex.Message}");
            return false;
        }
    }

    internal static bool HasRequiredDevices(uint devices) =>
        (devices & (KeyboardDevice | PointerDevice)) == (KeyboardDevice | PointerDevice);

    internal static Dictionary<string, object> CreateSelectOptions(uint version, string? restoreToken)
    {
        var options = new Dictionary<string, object> { ["types"] = KeyboardDevice | PointerDevice };
        if (version >= 2)
        {
            options["persist_mode"] = PersistUntilRevoked;
            if (!string.IsNullOrEmpty(restoreToken)) options["restore_token"] = restoreToken;
        }
        return options;
    }

    private void EnsureSessionOpen()
    {
        if (_closed || !_hasSession || _portal == null)
            throw new ScrollInputUnavailableException("The remote desktop input session has closed.");
    }

    public async Task MovePointerAsync(Point target)
    {
        EnsureSessionOpen();
        // The portal only moves the pointer relative to where it is, so this needs the position, which
        // KWin reports. Elsewhere the wheel turns wherever the pointer is, as in ShareX.
        if (_portal == null || KWinWindowManager.Shared is not { } kwin)
            return;

        double scale = KWinWindowManager.X11Scale;
        double targetX = target.X / scale, targetY = target.Y / scale;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (kwin.QueryCursorPosition() is not { } cursor)
                return;

            double dx = targetX - cursor.X;
            double dy = targetY - cursor.Y;
            if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2)
                return;

            await _portal.NotifyPointerMotionAsync(_session, new Dictionary<string, object>(), dx, dy).ConfigureAwait(false);
            await Task.Delay(30).ConfigureAwait(false);
        }
    }

    public async Task ScrollWheelAsync(int notches)
    {
        EnsureSessionOpen();
        if (_portal != null && notches > 0)
        {
            await _portal.NotifyPointerAxisDiscreteAsync(_session, new Dictionary<string, object>(), VerticalAxis, notches * _wheelStepsPerNotch).ConfigureAwait(false);
        }
    }

    public Task PressKeyAsync(int keysym)
    {
        EnsureSessionOpen();
        var portal = _portal!;
        var session = _session;
        return SendKeyPressAsync(state => portal.NotifyKeyboardKeysymAsync(session, new Dictionary<string, object>(), keysym, state));
    }

    internal static async Task SendKeyPressAsync(Func<uint, Task> notifyKey)
    {
        try
        {
            await notifyKey(Pressed).ConfigureAwait(false);
        }
        catch
        {
            // The press may have reached the compositor before the error, so the key is still released.
            // A release failure is only logged: the press's error is the one that explains the problem.
            try { await notifyKey(Released).ConfigureAwait(false); }
            catch (Exception releaseError) { DebugHelper.WriteLine($"RemoteDesktopScrollInput: Could not release the key after a failed press: {releaseError.Message}"); }
            throw;
        }

        await notifyKey(Released).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_connection != null && _hasSession)
            {
                await _connection.CreateProxy<IPortalSession>(PortalBusName, _session).CloseAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"RemoteDesktopScrollInput: Could not close the session: {ex.Message}");
        }
        finally
        {
            _closed = true;
            _closedSubscription?.Dispose();
            _closedSubscription = null;
            _hasSession = false;
            _portal = null;
            _connection?.Dispose();
            _connection = null;
        }
    }
}

[DBusInterface("org.freedesktop.portal.RemoteDesktop")]
public interface IRemoteDesktopPortal : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> SelectDevicesAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);
    Task NotifyPointerMotionAsync(ObjectPath sessionHandle, IDictionary<string, object> options, double dx, double dy);
    Task NotifyPointerAxisDiscreteAsync(ObjectPath sessionHandle, IDictionary<string, object> options, uint axis, int steps);
    Task NotifyKeyboardKeysymAsync(ObjectPath sessionHandle, IDictionary<string, object> options, int keysym, uint state);
}
