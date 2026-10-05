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

using System.ComponentModel;

namespace XerahS.Platform.Abstractions
{
    /// <summary>
    /// Platform-agnostic service for scroll simulation and scroll bar queries.
    /// Used by the scrolling capture manager to programmatically scroll windows.
    /// </summary>
    public interface IScrollingCaptureService
    {
        /// <summary>
        /// Whether scrolling capture is supported on this platform.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// The scroll methods this platform can perform. Windows-message methods exist only on Windows.
        /// </summary>
        IReadOnlyList<ScrollMethod> SupportedScrollMethods => Enum.GetValues<ScrollMethod>();

        /// <summary>
        /// Prepares scroll input for one capture, such as a remote desktop portal session on Wayland,
        /// which may ask the user for permission. Returns false when input cannot be sent.
        /// </summary>
        Task<bool> BeginAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        /// <summary>
        /// Moves the pointer out of the captured area, so it is not in the frames and does not hover over
        /// the content. Needed where screenshots include the cursor (the Wayland screenshot portal).
        /// </summary>
        Task MovePointerOutsideAsync(System.Drawing.Rectangle area) => Task.CompletedTask;

        /// <summary>
        /// Waits while something the desktop shows above every window, such as a notification popup,
        /// covers the area, because a fixed popup over scrolling content keeps the frames from matching.
        /// </summary>
        Task WaitUntilAreaIsClearAsync(System.Drawing.Rectangle area, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>Ends what <see cref="BeginAsync"/> started.</summary>
        Task EndAsync() => Task.CompletedTask;

        /// <summary>
        /// Scrolls the specified window using the given method and amount.
        /// </summary>
        /// <param name="windowHandle">Target window handle</param>
        /// <param name="method">Scroll method to use</param>
        /// <param name="amount">Number of scroll units</param>
        Task ScrollWindowAsync(IntPtr windowHandle, ScrollMethod method, int amount, System.Drawing.Point? targetPoint = null);

        /// <summary>
        /// Scrolls the specified window to the top of its content.
        /// </summary>
        /// <param name="windowHandle">Target window handle</param>
        Task ScrollToTopAsync(IntPtr windowHandle, System.Drawing.Point? targetPoint = null);

        /// <summary>
        /// Gets scroll bar position and range information for the specified window.
        /// </summary>
        /// <param name="windowHandle">Target window handle</param>
        /// <returns>Scroll bar info, or null if the window has no scrollbar</returns>
        ScrollBarInfo? GetScrollBarInfo(IntPtr windowHandle);
    }

    /// <summary>
    /// Scroll bar position and range information for a window.
    /// </summary>
    public record ScrollBarInfo(int Position, int MinRange, int MaxRange, int PageSize)
    {
        /// <summary>
        /// Whether the scroll bar is at the bottom of its range.
        /// </summary>
        public bool IsAtBottom => MaxRange <= Position + PageSize - 1;
    }

    /// <summary>
    /// Method used to scroll a window during scrolling capture.
    /// </summary>
    public enum ScrollMethod // Localized
    {
        [Description("Mouse wheel")]
        MouseWheel,
        [Description("Mouse wheel message (no cursor movement)")]
        MouseWheelMessage,
        [Description("Down arrow")]
        DownArrow,
        [Description("Page down")]
        PageDown,
        [Description("Scroll message")]
        ScrollMessage
    }

    /// <summary>
    /// Status of a scrolling capture operation.
    /// </summary>
    public enum ScrollingCaptureStatus
    {
        Failed,
        PartiallySuccessful,
        Successful
    }

    /// <summary>
    /// Result of a scrolling capture operation.
    /// </summary>
    public class ScrollingCaptureResult
    {
        public SkiaSharp.SKBitmap? Image { get; set; }
        public ScrollingCaptureStatus Status { get; set; }
        public int FramesCaptured { get; set; }

        /// <summary>
        /// True when the platform could not send scroll input, for example because the user refused it,
        /// or because the window system ended the input session during the capture.
        /// </summary>
        public bool InputUnavailable { get; set; }

        /// <summary>The target closed, moved, changed identity, or could not retain keyboard focus.</summary>
        public bool TargetUnavailable { get; set; }

        /// <summary>The error that ended the capture. <see cref="Image"/> still holds the frames stitched before it.</summary>
        public Exception? Error { get; set; }
    }

    /// <summary>Thrown by scroll input when the window system has ended the input session.</summary>
    public sealed class ScrollInputUnavailableException(string message) : Exception(message);

    /// <summary>
    /// Progress data reported during a scrolling capture operation.
    /// </summary>
    public class ScrollingCaptureProgress
    {
        public int FramesCaptured { get; set; }
        public SkiaSharp.SKBitmap? LatestFrame { get; set; }
    }
}
