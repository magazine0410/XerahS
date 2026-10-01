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

namespace XerahS.Platform.Linux.Capture.Scrolling;

/// <summary>Sends the input a scrolling capture needs: pointer moves, wheel notches, and keys.</summary>
internal interface IScrollInput : IAsyncDisposable
{
    /// <summary>Starts sending input. False when the window system or the user does not allow it.</summary>
    Task<bool> BeginAsync(CancellationToken cancellationToken);

    /// <summary>Moves the pointer to a point in XerahS's X11 screen coordinates, where possible.</summary>
    Task MovePointerAsync(Point target);

    /// <summary>Turns the mouse wheel down by the given number of notches.</summary>
    Task ScrollWheelAsync(int notches);

    /// <summary>Presses and releases a key, given as an X11 keysym.</summary>
    Task PressKeyAsync(int keysym);
}

internal static class Keysyms
{
    public const int Home = 0xff50;
    public const int Down = 0xff54;
    public const int PageDown = 0xff56;
}
