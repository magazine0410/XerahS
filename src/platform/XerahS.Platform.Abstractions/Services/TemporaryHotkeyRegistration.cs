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

namespace XerahS.Platform.Abstractions;

/// <summary>
/// A temporary hotkey for backends whose registrations end with the process, such as Windows
/// RegisterHotKey, X11 key grabs, and the evdev listener: it is registered like any other hotkey
/// and unregistered when disposed.
/// </summary>
public sealed class TemporaryHotkeyRegistration : IDisposable
{
    private readonly IHotkeyService _service;
    private readonly HotkeyInfo _hotkey;
    private readonly Action _pressed;
    private bool _disposed;

    private TemporaryHotkeyRegistration(IHotkeyService service, HotkeyInfo hotkey, Action pressed)
    {
        _service = service;
        _hotkey = hotkey;
        _pressed = pressed;
    }

    /// <summary>Returns the registration, or null when the service refused the hotkey.</summary>
    public static IDisposable? TryRegister(IHotkeyService service, HotkeyInfo hotkey, Action pressed)
    {
        var registration = new TemporaryHotkeyRegistration(service, hotkey, pressed);
        service.HotkeyTriggered += registration.OnHotkeyTriggered;
        if (service.RegisterHotkey(hotkey))
        {
            return registration;
        }

        service.HotkeyTriggered -= registration.OnHotkeyTriggered;
        return null;
    }

    private void OnHotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        if (!_disposed && (ReferenceEquals(e.HotkeyInfo, _hotkey) || (_hotkey.Id != 0 && e.HotkeyInfo.Id == _hotkey.Id)))
        {
            _pressed();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _service.HotkeyTriggered -= OnHotkeyTriggered;
        _service.UnregisterHotkey(_hotkey);
    }
}
