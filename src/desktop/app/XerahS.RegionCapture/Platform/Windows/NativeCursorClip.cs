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

#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace XerahS.RegionCapture.Platform.Windows;

/// <summary>
/// ClipCursor wrapper used by active monitor mode (ShareX: Helpers.LockCursorToWindow).
/// Coordinates are physical pixels; the process is per-monitor DPI aware.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeCursorClip
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursorToRect(ref NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursorNone(IntPtr rect);

    public static bool Clip(int left, int top, int right, int bottom)
    {
        var rect = new NativeRect { Left = left, Top = top, Right = right, Bottom = bottom };
        return ClipCursorToRect(ref rect);
    }

    public static void Release() => ClipCursorNone(IntPtr.Zero);
}
#endif
