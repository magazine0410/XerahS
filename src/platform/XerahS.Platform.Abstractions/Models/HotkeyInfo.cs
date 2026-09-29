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

namespace XerahS.Platform.Abstractions;

/// <summary>
/// Represents a global hotkey binding
/// </summary>
public class HotkeyInfo
{
    /// <summary>
    /// The primary key (e.g., PrintScreen, A, F1)
    /// </summary>
    public Key Key { get; set; }

    /// <summary>
    /// Key modifiers (Ctrl, Alt, Shift, Meta/Win)
    /// </summary>
    public KeyModifiers Modifiers { get; set; }

    /// <summary>
    /// Unique identifier assigned during registration
    /// </summary>
    [System.Runtime.Serialization.IgnoreDataMember]
    public ushort Id { get; set; }

    /// <summary>
    /// Current registration status
    /// </summary>
    [System.Runtime.Serialization.IgnoreDataMember]
    public HotkeyStatus Status { get; set; } = HotkeyStatus.NotConfigured;

    /// <summary>
    /// Optional compositor-provided description of the effective trigger.
    /// Used for display when the desktop environment owns shortcut assignment.
    /// </summary>
    [System.Runtime.Serialization.IgnoreDataMember]
    public string? NativeTriggerDescription { get; set; }

    /// <summary>
    /// Stable identifier for desktop shortcut registries such as the GlobalShortcuts portal, which store
    /// the user's assigned keys by this ID. Set to the workflow ID; <see cref="Id"/> changes on every registration.
    /// </summary>
    [System.Runtime.Serialization.IgnoreDataMember]
    public string? BindingId { get; set; }

    /// <summary>
    /// Name shown for this shortcut in desktop shortcut settings (the workflow name).
    /// </summary>
    [System.Runtime.Serialization.IgnoreDataMember]
    public string? BindingName { get; set; }

    /// <summary>
    /// Whether this is a valid hotkey (has a key assigned)
    /// </summary>
    public bool IsValid => Key != Key.None && !IsOnlyModifiers;

    /// <summary>
    /// Whether only modifier keys are pressed (not a valid complete hotkey)
    /// </summary>
    public bool IsOnlyModifiers => Key == Key.LeftCtrl || Key == Key.RightCtrl ||
                                    Key == Key.LeftShift || Key == Key.RightShift ||
                                    Key == Key.LeftAlt || Key == Key.RightAlt ||
                                    Key == Key.LWin || Key == Key.RWin;

    public bool HasControl => Modifiers.HasFlag(KeyModifiers.Control);
    public bool HasAlt => Modifiers.HasFlag(KeyModifiers.Alt);
    public bool HasShift => Modifiers.HasFlag(KeyModifiers.Shift);
    public bool HasMeta => Modifiers.HasFlag(KeyModifiers.Meta);

    public HotkeyInfo()
    {
    }

    public HotkeyInfo(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public override string ToString()
    {
        var parts = new System.Collections.Generic.List<string>();

        if (HasControl) parts.Add("Ctrl");
        if (HasAlt) parts.Add("Alt");
        if (HasShift) parts.Add("Shift");
        if (HasMeta)
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                parts.Add("Cmd");
            }
            else
            {
                parts.Add("Win");
            }
        }

        if (!IsOnlyModifiers && Key != Key.None)
        {
            parts.Add(FormatKeyName(Key));
        }
        else if (parts.Count > 0)
        {
            parts.Add("...");
        }

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Returns the key that identifies the physical key for conflict checks. Avalonia reports the
    /// Print Screen key as <see cref="Key.Print"/> on Linux (X11 keysym "Print") and as
    /// <see cref="Key.PrintScreen"/> on Windows, and the ShareX defaults use PrintScreen.
    /// Both reach the compositor and the GlobalShortcuts portal as the same "Print" trigger.
    /// </summary>
    public static Key NormalizeKey(Key key) => key == Key.Print ? Key.PrintScreen : key;

    /// <summary>
    /// Alt + Print Screen. With Alt held, the Print Screen key produces SysRq (XKB "Sys_Req"; the
    /// kernel's Magic SysRq key), so Linux desktops cannot deliver it as a shortcut.
    /// </summary>
    public bool IsSysRqCombination => IsValid && HasAlt && NormalizeKey(Key) == Key.PrintScreen;

    /// <summary>
    /// True when both hotkeys are valid and press the same physical key combination.
    /// </summary>
    public bool ConflictsWith(HotkeyInfo? other)
    {
        return other != null &&
               IsValid &&
               other.IsValid &&
               Modifiers == other.Modifiers &&
               NormalizeKey(Key) == NormalizeKey(other.Key);
    }

    public string GetDisplayString()
    {
        return string.IsNullOrWhiteSpace(NativeTriggerDescription)
            ? ToString()
            : NativeTriggerDescription;
    }

    private static string FormatKeyName(Key key)
    {
        return key switch
        {
            Key.Back => "Backspace",
            Key.Return => "Enter",
            Key.Capital => "Caps Lock",
            Key.PageDown => "Page Down",
            Key.PageUp => "Page Up",
            Key.PrintScreen or Key.Print => "Print Screen",
            Key.Scroll => "Scroll Lock",
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Numpad " + ((int)key - (int)Key.NumPad0),
            _ => key.ToString()
        };
    }

    /// <summary>
    /// Convert to Avalonia KeyGesture for in-app hotkey binding
    /// </summary>
    public KeyGesture? ToKeyGesture()
    {
        if (!IsValid) return null;
        return new KeyGesture(Key, Modifiers);
    }

    public override bool Equals(object? obj)
    {
        if (obj is HotkeyInfo other)
        {
            return Key == other.Key && Modifiers == other.Modifiers;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Key, Modifiers);
    }
}

/// <summary>
/// Status of hotkey registration
/// </summary>
public enum HotkeyStatus
{
    NotConfigured,
    Registered,
    Failed,
    UnsupportedPlatform,
    Recording  // User is currently editing this hotkey
}
