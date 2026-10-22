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

namespace XerahS.Platform.Abstractions
{
    /// <summary>
    /// Temporary desktop changes around a capture, as ShareX's capture preparation on Windows: hiding the panels
    /// ("taskbar") that a captured window intersects, and hiding the desktop icons. Each change is undone when the
    /// returned scope is disposed, and a platform keeps enough state to undo it after a crash.
    /// </summary>
    public interface ICapturePreparationService
    {
        /// <summary>Whether panels can be hidden on this desktop.</summary>
        bool CanHidePanels { get; }

        /// <summary>Whether desktop icons can be hidden on this desktop.</summary>
        bool CanHideDesktopIcons { get; }

        /// <summary>
        /// Hides the panels that intersect <paramref name="bounds"/> (screen coordinates) and waits until they are
        /// hidden. Returns the scope that shows them again, or null when no panel had to be hidden.
        /// </summary>
        Task<IAsyncDisposable?> HidePanelsIntersectingAsync(Rectangle bounds, CancellationToken token);

        /// <summary>
        /// Hides the desktop icons and waits until they are gone. Returns the scope that shows them again, or null
        /// when they were left alone (for example because the active window is full screen, as in ShareX).
        /// </summary>
        Task<IAsyncDisposable?> HideDesktopIconsAsync(CancellationToken token);

        /// <summary>Undoes changes that an earlier process did not undo, for example after a crash during a capture.</summary>
        Task RecoverAsync();
    }
}
