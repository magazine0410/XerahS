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

using XerahS.RegionCapture.Models;

namespace XerahS.RegionCapture.Services;

/// <summary>
/// Restricts the OS cursor to a rectangle in physical pixels.
/// Windows uses ClipCursor. Other platforms have no equivalent available to an X11/XWayland
/// client without taking over the pointer grab from Avalonia, so they report no support.
/// </summary>
internal static class CursorConfinementService
{
    public static bool IsSupported
    {
        get
        {
#if WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

    public static bool TryConfine(PixelRect bounds)
    {
#if WINDOWS
        var (x, y, width, height) = bounds.ToIntegerBounds();
        return Platform.Windows.NativeCursorClip.Clip(x, y, x + width, y + height);
#else
        return false;
#endif
    }

    public static void Release()
    {
#if WINDOWS
        Platform.Windows.NativeCursorClip.Release();
#endif
    }
}
