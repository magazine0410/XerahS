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
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>
/// Converts between XerahS hotkeys and the Qt key combinations (QKeyCombination::toCombined)
/// that KDE's kglobalaccel stores: a Qt::Key value OR-ed with Qt keyboard modifier flags.
/// </summary>
internal static class QtKeyMapper
{
    private const int ShiftModifier = 0x02000000;
    private const int ControlModifier = 0x04000000;
    private const int AltModifier = 0x08000000;
    private const int MetaModifier = 0x10000000;
    private const int KeypadModifier = 0x20000000;
    private const int ModifierMask = ShiftModifier | ControlModifier | AltModifier | MetaModifier | KeypadModifier;

    private static readonly (Key Key, int QtKey, bool Keypad)[] Table = BuildTable();
    private static readonly Dictionary<Key, (int QtKey, bool Keypad)> ToQtMap = BuildToQtMap();
    private static readonly Dictionary<(int QtKey, bool Keypad), Key> FromQtMap = BuildFromQtMap();

    /// <summary>Returns the Qt key combination, or 0 when the key has no Qt equivalent here.</summary>
    public static int ToQt(HotkeyInfo hotkey)
    {
        if (!hotkey.IsValid || !ToQtMap.TryGetValue(HotkeyInfo.NormalizeKey(hotkey.Key), out var entry))
            return 0;

        int combined = entry.QtKey;
        if (entry.Keypad) combined |= KeypadModifier;
        if (hotkey.HasShift) combined |= ShiftModifier;
        if (hotkey.HasControl) combined |= ControlModifier;
        if (hotkey.HasAlt) combined |= AltModifier;
        if (hotkey.HasMeta) combined |= MetaModifier;
        return combined;
    }

    /// <summary>Converts a Qt key combination, or returns false when XerahS cannot represent it.</summary>
    public static bool TryFromQt(int combined, out Key key, out KeyModifiers modifiers)
    {
        key = Key.None;
        modifiers = KeyModifiers.None;
        if (combined == 0)
            return false;

        bool keypad = (combined & KeypadModifier) != 0;
        int qtKey = combined & ~ModifierMask;
        if (!FromQtMap.TryGetValue((qtKey, keypad), out key) &&
            !(keypad && FromQtMap.TryGetValue((qtKey, false), out key)))
        {
            return false;
        }

        if ((combined & ShiftModifier) != 0) modifiers |= KeyModifiers.Shift;
        if ((combined & ControlModifier) != 0) modifiers |= KeyModifiers.Control;
        if ((combined & AltModifier) != 0) modifiers |= KeyModifiers.Alt;
        if ((combined & MetaModifier) != 0) modifiers |= KeyModifiers.Meta;
        return true;
    }

    private static (Key, int, bool)[] BuildTable()
    {
        var table = new List<(Key, int, bool)>();

        for (int i = 0; i < 26; i++)
            table.Add((Key.A + i, 0x41 + i, false));
        for (int i = 0; i < 10; i++)
            table.Add((Key.D0 + i, 0x30 + i, false));
        for (int i = 0; i < 10; i++)
            table.Add((Key.NumPad0 + i, 0x30 + i, true));
        for (int i = 0; i < 24; i++)
            table.Add((Key.F1 + i, 0x01000030 + i, false));

        table.AddRange(new (Key, int, bool)[]
        {
            (Key.Escape, 0x01000000, false),
            (Key.Tab, 0x01000001, false),
            (Key.Back, 0x01000003, false),
            (Key.Return, 0x01000004, false),
            (Key.Insert, 0x01000006, false),
            (Key.Delete, 0x01000007, false),
            (Key.Pause, 0x01000008, false),
            (Key.PrintScreen, 0x01000009, false),
            (Key.Home, 0x01000010, false),
            (Key.End, 0x01000011, false),
            (Key.Left, 0x01000012, false),
            (Key.Up, 0x01000013, false),
            (Key.Right, 0x01000014, false),
            (Key.Down, 0x01000015, false),
            (Key.PageUp, 0x01000016, false),
            (Key.PageDown, 0x01000017, false),
            (Key.CapsLock, 0x01000024, false),
            (Key.NumLock, 0x01000025, false),
            (Key.Scroll, 0x01000026, false),
            (Key.Apps, 0x01000055, false),
            (Key.Space, 0x20, false),
            (Key.OemQuotes, 0x27, false),
            (Key.OemComma, 0x2c, false),
            (Key.OemMinus, 0x2d, false),
            (Key.OemPeriod, 0x2e, false),
            (Key.OemQuestion, 0x2f, false),
            (Key.OemSemicolon, 0x3b, false),
            (Key.OemPlus, 0x3d, false),
            (Key.OemOpenBrackets, 0x5b, false),
            (Key.OemPipe, 0x5c, false),
            (Key.OemCloseBrackets, 0x5d, false),
            (Key.OemTilde, 0x60, false),
            (Key.Multiply, 0x2a, true),
            (Key.Add, 0x2b, true),
            (Key.Subtract, 0x2d, true),
            (Key.Decimal, 0x2e, true),
            (Key.Divide, 0x2f, true),
            (Key.VolumeDown, 0x01000070, false),
            (Key.VolumeMute, 0x01000071, false),
            (Key.VolumeUp, 0x01000072, false),
            (Key.MediaStop, 0x01000081, false),
            (Key.MediaPreviousTrack, 0x01000082, false),
            (Key.MediaNextTrack, 0x01000083, false),
            (Key.MediaPlayPause, 0x01000086, false),
        });

        return table.ToArray();
    }

    private static Dictionary<Key, (int, bool)> BuildToQtMap()
    {
        var map = new Dictionary<Key, (int, bool)>();
        foreach (var (key, qtKey, keypad) in Table)
            map.TryAdd(key, (qtKey, keypad));
        return map;
    }

    private static Dictionary<(int, bool), Key> BuildFromQtMap()
    {
        var map = new Dictionary<(int, bool), Key>();
        foreach (var (key, qtKey, keypad) in Table)
            map.TryAdd((qtKey, keypad), key);
        return map;
    }
}
