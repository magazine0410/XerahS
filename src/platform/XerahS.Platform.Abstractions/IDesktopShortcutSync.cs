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
/// Implemented by hotkey services that keep XerahS hotkeys in sync with the desktop's own shortcut
/// settings (KDE Plasma System Settings → Shortcuts).
/// </summary>
public interface IDesktopShortcutSync
{
    /// <summary>
    /// True when keys changed in XerahS are applied to the desktop's shortcut settings and the reverse.
    /// </summary>
    bool IsDesktopShortcutSyncActive { get; }

    /// <summary>
    /// Raised on the UI thread after the desktop's shortcut settings changed the keys of registered
    /// hotkeys. The listed <see cref="HotkeyInfo"/> objects are already updated; the handler saves them.
    /// </summary>
    event EventHandler<DesktopHotkeysChangedEventArgs>? HotkeysChangedByDesktop;
}

public sealed class DesktopHotkeysChangedEventArgs : EventArgs
{
    public DesktopHotkeysChangedEventArgs(IReadOnlyList<HotkeyInfo> hotkeys)
    {
        Hotkeys = hotkeys;
    }

    public IReadOnlyList<HotkeyInfo> Hotkeys { get; }
}
