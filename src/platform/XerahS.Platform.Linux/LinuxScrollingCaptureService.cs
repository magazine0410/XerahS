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
using XerahS.Common;
using Point = System.Drawing.Point;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux.Capture.Scrolling;
using XerahS.Platform.Linux.Services;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Platform.Linux;

/// <summary>
/// Scrolls the target window for scrolling capture: through XTEST on X11, and through the
/// RemoteDesktop portal on Wayland. Linux has no equivalent of the Windows scroll messages, so only
/// the mouse wheel and the Down arrow and Page down keys are offered.
/// </summary>
public sealed class LinuxScrollingCaptureService : IScrollingCaptureService
{
    private static readonly ScrollMethod[] Methods = [ScrollMethod.MouseWheel, ScrollMethod.DownArrow, ScrollMethod.PageDown];
    private readonly Lazy<bool> _isSupported;
    private readonly Func<bool> _isWayland;
    private IScrollInput? _input;

    public LinuxScrollingCaptureService()
        : this(() => LinuxScreenCaptureService.IsWayland, () => PortalInterfaceChecker.HasInterface(RemoteDesktopScrollInput.InterfaceName), XTestScrollInput.IsAvailable)
    {
    }

    internal LinuxScrollingCaptureService(Func<bool> isWayland, Func<bool> hasRemoteDesktopPortal, Func<bool> hasXTest)
    {
        _isWayland = isWayland;
        _isSupported = new Lazy<bool>(() => isWayland() ? hasRemoteDesktopPortal() : hasXTest());
    }

    public bool IsSupported => _isSupported.Value;

    public IReadOnlyList<ScrollMethod> SupportedScrollMethods => Methods;

    /// <summary>Where the RemoteDesktop portal's restore token is kept between sessions.</summary>
    internal static string RestoreTokenPath => Path.Combine(PathsManager.SettingsFolder, "RemoteDesktopRestoreToken");

    public async Task<bool> BeginAsync(CancellationToken cancellationToken = default)
    {
        // The scrolling capture window starts the session before the area selection, so the desktop's
        // "remote control" notification has time to close; the capture then reuses it.
        if (_input != null)
            return true;
        if (!IsSupported)
            return false;

        IScrollInput input = _isWayland()
            ? new RemoteDesktopScrollInput(LoadRestoreToken, SaveRestoreToken)
            : new XTestScrollInput();
        if (!await input.BeginAsync(cancellationToken).ConfigureAwait(false))
        {
            await input.DisposeAsync().ConfigureAwait(false);
            return false;
        }

        _input = input;
        return true;
    }

    public async Task EndAsync()
    {
        IScrollInput? input = Interlocked.Exchange(ref _input, null);
        if (input != null)
            await input.DisposeAsync().ConfigureAwait(false);
    }

    public async Task ScrollWindowAsync(IntPtr windowHandle, ScrollMethod method, int amount, Point? targetPoint = null)
    {
        if (_input is not { } input)
            return;

        switch (method)
        {
            case ScrollMethod.DownArrow:
                for (int i = 0; i < amount; i++)
                    await input.PressKeyAsync(Keysyms.Down).ConfigureAwait(false);
                break;
            case ScrollMethod.PageDown:
                await input.PressKeyAsync(Keysyms.PageDown).ConfigureAwait(false);
                break;
            default:
                // The Windows-only message methods fall back to the mouse wheel.
                if (targetPoint is { } point)
                    await input.MovePointerAsync(point).ConfigureAwait(false);
                await input.ScrollWheelAsync(amount).ConfigureAwait(false);
                break;
        }
    }

    public async Task ScrollToTopAsync(IntPtr windowHandle, Point? targetPoint = null)
    {
        // ShareX presses Home and sends a scroll-to-top message; Linux has only the key.
        if (_input is { } input)
            await input.PressKeyAsync(Keysyms.Home).ConfigureAwait(false);
    }

    public ScrollBarInfo? GetScrollBarInfo(IntPtr windowHandle) => null;

    public async Task WaitUntilAreaIsClearAsync(Rectangle area, CancellationToken cancellationToken = default)
    {
        // KDE Plasma shows "Remote control session started" when a session starts; other popups can
        // appear too. KWin lists them, so wait for them to leave the area, for up to 10 seconds.
        if (KWinWindowManager.Shared is not { } kwin)
            return;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < NotificationWaitLimit && CoversArea(kwin.GetNotificationBounds(), area, KWinWindowManager.X11Scale))
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly TimeSpan NotificationWaitLimit = TimeSpan.FromSeconds(10);

    internal static bool CoversArea(IEnumerable<Rectangle> logicalPopups, Rectangle area, double x11Scale) =>
        logicalPopups.Any(popup => KWinWindowManager.ToX11(popup, x11Scale).IntersectsWith(area));

    public async Task MovePointerOutsideAsync(Rectangle area)
    {
        if (_input is not { } input || !PlatformServices.IsInitialized)
            return;

        Rectangle[] screens = PlatformServices.Screen.GetAllScreens().Select(screen => screen.Bounds).ToArray();
        if (GetPointOutside(area, screens) is { } point)
            await input.MovePointerAsync(point).ConfigureAwait(false);
    }

    /// <summary>
    /// A point a little outside the area, on a screen: to its right, left, below, or above, in that order.
    /// Null when the area covers every candidate, such as a full-screen area.
    /// </summary>
    internal static Point? GetPointOutside(Rectangle area, IReadOnlyList<Rectangle> screens, int gap = 32)
    {
        int middleX = area.Left + area.Width / 2;
        int middleY = area.Top + area.Height / 2;
        Point[] candidates =
        [
            new(area.Right + gap, middleY),
            new(area.Left - gap - 1, middleY),
            new(middleX, area.Bottom + gap),
            new(middleX, area.Top - gap - 1)
        ];

        foreach (Point candidate in candidates)
        {
            if (screens.Any(screen => screen.Contains(candidate)))
                return candidate;
        }

        return null;
    }

    private static string? LoadRestoreToken()
    {
        try
        {
            return File.Exists(RestoreTokenPath) ? File.ReadAllText(RestoreTokenPath).Trim() : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void SaveRestoreToken(string? token)
    {
        try
        {
            if (string.IsNullOrEmpty(token))
            {
                File.Delete(RestoreTokenPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(RestoreTokenPath)!);
            File.WriteAllText(RestoreTokenPath, token);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(RestoreTokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugHelper.WriteLine($"LinuxScrollingCaptureService: Could not save the remote desktop restore token: {ex.Message}");
        }
    }
}
