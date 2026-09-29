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

using Avalonia.Input;
using XerahS.Common;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>
/// Keeps XerahS hotkeys and their KDE shortcuts equal. The GlobalShortcuts portal only suggests keys
/// for shortcuts KDE has not seen before and keeps saved keys afterwards, so edits in XerahS would
/// otherwise never reach KDE.
///
/// For each shortcut the last key seen in KDE is remembered. A hotkey that differs from it was edited
/// in XerahS and is written to KDE; a KDE key that differs from it was edited in System Settings and is
/// copied into the hotkey. When a shortcut is first seen in this process, KDE's key wins, so changes
/// made in System Settings while XerahS was closed are kept.
/// </summary>
internal sealed class KdeShortcutSync
{
    private readonly IKdeGlobalAccel _accel;
    private readonly string _component;
    private readonly Dictionary<string, int> _lastKdeKeys = new(StringComparer.Ordinal);

    // KDE keys XerahS cannot represent; the hotkey is left alone until it is edited in XerahS.
    private readonly Dictionary<string, int> _unrepresentedHotkeys = new(StringComparer.Ordinal);

    public KdeShortcutSync(IKdeGlobalAccel accel, string component)
    {
        _accel = accel;
        _component = component;
    }

    public string Component => _component;

    /// <summary>
    /// Synchronizes the bound shortcuts and removes KDE shortcuts that no bound hotkey uses any more.
    /// Returns the hotkeys whose keys were changed from KDE; the caller saves them.
    /// </summary>
    public async Task<IReadOnlyList<HotkeyInfo>> SyncAsync(IReadOnlyList<(string Id, HotkeyInfo Hotkey)> bound)
    {
        var changedFromKde = new List<HotkeyInfo>();

        foreach (var (id, hotkey) in bound)
        {
            int hotkeyKey = QtKeyMapper.ToQt(hotkey);
            if (hotkeyKey == 0)
                continue; // No Qt equivalent: leave the portal's suggested key alone.

            int kdeKey = await _accel.GetKeyAsync(_component, id).ConfigureAwait(false);
            bool known = _lastKdeKeys.TryGetValue(id, out int lastKdeKey);
            bool unrepresented = _unrepresentedHotkeys.TryGetValue(id, out int unrepresentedHotkey) &&
                                 unrepresentedHotkey == hotkeyKey;

            if (!known)
            {
                if (kdeKey != 0)
                {
                    ApplyKdeKey(id, hotkey, hotkeyKey, kdeKey, changedFromKde);
                }
                else
                {
                    kdeKey = await WriteKdeKeyAsync(id, hotkey, hotkeyKey).ConfigureAwait(false);
                }
            }
            else if (hotkeyKey != lastKdeKey && !unrepresented)
            {
                kdeKey = await WriteKdeKeyAsync(id, hotkey, hotkeyKey).ConfigureAwait(false);
            }
            else if (kdeKey != lastKdeKey)
            {
                ApplyKdeKey(id, hotkey, hotkeyKey, kdeKey, changedFromKde);
            }

            _lastKdeKeys[id] = kdeKey;
            MarkIfNotAssigned(hotkey, kdeKey);
        }

        await RemoveUnusedAsync(bound.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal)).ConfigureAwait(false);
        return changedFromKde;
    }

    private async Task<int> WriteKdeKeyAsync(string id, HotkeyInfo hotkey, int hotkeyKey)
    {
        string name = string.IsNullOrWhiteSpace(hotkey.BindingName) ? hotkey.ToString() : hotkey.BindingName;
        await _accel.SetKeyAsync(_component, id, name, hotkeyKey).ConfigureAwait(false);
        _unrepresentedHotkeys.Remove(id);

        // kglobalaccel drops a key that another shortcut already uses; read back what it kept.
        int kdeKey = await _accel.GetKeyAsync(_component, id).ConfigureAwait(false);
        DebugHelper.WriteLine(kdeKey == hotkeyKey
            ? $"KdeShortcutSync: Set KDE shortcut '{name}' to {hotkey}."
            : $"KdeShortcutSync: KDE did not accept {hotkey} for '{name}'.");
        return kdeKey;
    }

    private void ApplyKdeKey(string id, HotkeyInfo hotkey, int hotkeyKey, int kdeKey, List<HotkeyInfo> changed)
    {
        if (kdeKey == hotkeyKey)
            return;

        if (kdeKey == 0)
        {
            hotkey.Key = Key.None;
            hotkey.Modifiers = KeyModifiers.None;
        }
        else if (QtKeyMapper.TryFromQt(kdeKey, out var key, out var modifiers))
        {
            hotkey.Key = key;
            hotkey.Modifiers = modifiers;
        }
        else
        {
            DebugHelper.WriteLine($"KdeShortcutSync: KDE key 0x{kdeKey:X8} for '{hotkey.BindingName}' has no XerahS equivalent; keeping {hotkey}.");
            _unrepresentedHotkeys[id] = hotkeyKey;
            return;
        }

        DebugHelper.WriteLine($"KdeShortcutSync: '{hotkey.BindingName}' changed in KDE to {(hotkey.IsValid ? hotkey.ToString() : "none")}.");
        changed.Add(hotkey);
    }

    private static void MarkIfNotAssigned(HotkeyInfo hotkey, int kdeKey)
    {
        if (kdeKey != 0 || !hotkey.IsValid)
            return;

        hotkey.Status = XerahS.Platform.Abstractions.HotkeyStatus.Failed;
        hotkey.NativeTriggerDescription = $"{hotkey} (not assigned in KDE: another shortcut uses these keys)";
    }

    private async Task RemoveUnusedAsync(HashSet<string> boundIds)
    {
        foreach (string action in await _accel.GetActionIdsAsync(_component).ConfigureAwait(false))
        {
            if (boundIds.Contains(action))
                continue;

            await _accel.UnregisterAsync(_component, action).ConfigureAwait(false);
            _lastKdeKeys.Remove(action);
            _unrepresentedHotkeys.Remove(action);
            DebugHelper.WriteLine($"KdeShortcutSync: Removed unused KDE shortcut '{action}'.");
        }
    }
}
