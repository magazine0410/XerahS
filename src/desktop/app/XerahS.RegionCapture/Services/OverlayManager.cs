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
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using XerahS.RegionCapture.Models;
using XerahS.RegionCapture;
using XerahS.RegionCapture.UI;
using XerahS.Common;
using XerahS.RegionCapture.ViewModels;

namespace XerahS.RegionCapture.Services;

/// <summary>
/// Manages the lifecycle and coordination of per-monitor overlay windows.
/// This implements the "Per-Monitor Overlay" pattern (Strategy B) to bypass
/// mixed-DPI scaling artifacts common in single-span windows.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly List<OverlayWindow> _overlays = [];
    private readonly TaskCompletionSource<RegionSelectionResult?> _completionSource;
    private readonly CoordinateTranslationService _coordinateService;
    private readonly RegionCaptureAnnotationToolCoordinator _annotationToolCoordinator;
    private ActiveMonitorCoordinator? _activeMonitorCoordinator;
    private Action? _disconnectPointerPresence;
    private bool _disposed;

    public OverlayManager() : this(new CoordinateTranslationService()) { }

    internal OverlayManager(CoordinateTranslationService coordinateService)
    {
        _completionSource = new TaskCompletionSource<RegionSelectionResult?>();
        _coordinateService = coordinateService;
        _annotationToolCoordinator = new RegionCaptureAnnotationToolCoordinator();
    }

    /// <summary>
    /// Gets all active overlay windows.
    /// </summary>
    public IReadOnlyList<OverlayWindow> Overlays => _overlays;

    /// <summary>
    /// Gets the coordinate translation service for cross-monitor calculations.
    /// </summary>
    public CoordinateTranslationService CoordinateService => _coordinateService;

    /// <summary>
    /// Creates and shows overlay windows for all monitors.
    /// </summary>
    /// <summary>
    /// Creates and shows overlay windows for all monitors.
    /// </summary>
    public async Task<RegionSelectionResult?> ShowOverlaysAsync(
        Action<PixelRect>? onSelectionChanged = null,
        XerahS.Platform.Abstractions.CursorInfo? initialCursor = null,
        RegionCaptureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellation = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => _completionSource.TrySetCanceled(cancellationToken)));

        options ??= new RegionCaptureOptions();
        var monitors = _coordinateService.Monitors;

        if (monitors.Count == 0)
            return null;

        MonitorInfo? initialActiveMonitor = null;
        if (options.ActiveMonitorMode)
        {
            PixelPoint? cursor = _coordinateService.TryGetReliableCursorPosition(out var cursorPosition)
                ? cursorPosition
                : null;
            initialActiveMonitor = ActiveMonitorCoordinator.ResolveInitialActiveMonitor(monitors, cursor);

            // With the cursor confined to the active monitor (Windows), the other monitors need no overlay.
            if (initialActiveMonitor != null && CursorConfinementService.IsSupported)
                monitors = [initialActiveMonitor];
        }
        else
        {
            var captureBounds = monitors.Aggregate(PixelRect.Empty, (bounds, monitor) => bounds.Union(monitor.PhysicalBounds));
            options = options with { CaptureBounds = captureBounds };
        }

        try
        {
            // Create one overlay per monitor. In active monitor mode each overlay is limited to its own monitor.
            foreach (var monitor in monitors)
            {
                var overlayOptions = options.ActiveMonitorMode
                    ? options with { CaptureBounds = monitor.PhysicalBounds }
                    : options;
                var overlay = new OverlayWindow(monitor, _completionSource, onSelectionChanged, initialCursor, overlayOptions, _annotationToolCoordinator);
                _overlays.Add(overlay);
            }

            _disconnectPointerPresence = ConnectPointerPresence(_overlays.ToList());

            if (options.ActiveMonitorMode)
            {
                _activeMonitorCoordinator = new ActiveMonitorCoordinator(
                    _overlays,
                    _overlays.FirstOrDefault(overlay => overlay.Monitor == initialActiveMonitor));
            }

            // Show and focus the active overlay (or the primary one) before the others
            // (helps Linux/Wayland grant focus sooner)
            var primaryOverlay = _activeMonitorCoordinator?.ActiveOverlay
                ?? _overlays.FirstOrDefault(overlay => overlay.Monitor.IsPrimary);

            // Show primary overlay first and focus it immediately so compositor has one clear focus target (reduces pointer-event delay on Wayland)
            if (primaryOverlay != null)
            {
                ShowOverlayDetached(primaryOverlay);
                primaryOverlay.Activate();
                primaryOverlay.Focus();
                var primaryHandle = primaryOverlay.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                WindowDetectionService.ExcludeHandle(primaryHandle);
            }

            // Show remaining overlays
            foreach (var overlay in _overlays)
            {
                if (overlay == primaryOverlay)
                    continue;
                ShowOverlayDetached(overlay);
                // Inactive overlays must not take focus from the active one.
                if (overlay.MonitorState != OverlayMonitorState.Inactive)
                    overlay.Activate();
                var handle = overlay.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                WindowDetectionService.ExcludeHandle(handle);
            }

            if (_activeMonitorCoordinator?.ActiveOverlay is { } activeOverlay && _overlays.Count > 1)
                activeOverlay.FocusOverlay();

            if (options?.SessionStartUtc is { } start)
            {
                double elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds;
                DebugHelper.WriteLine($"[RegionCapture] Milestone: overlay displayed (+{elapsedMs:F0} ms)");
            }

            // Wait for result
            return await _completionSource.Task;
        }
        finally
        {
            CloseAllOverlays();
        }
    }


    /// <summary>
    /// Only the overlay under the pointer shows the cursor crosshair and magnifier: each overlay passes its
    /// pointer position to the others, which hide theirs unless the position is on their monitor.
    /// Returns an action that disconnects the overlays.
    /// </summary>
    internal static Action ConnectPointerPresence(IReadOnlyList<OverlayWindow> overlays)
    {
        if (overlays.Count < 2)
            return static () => { };

        void OnPointerLocationChanged(OverlayWindow source, PixelPoint physicalPoint)
        {
            foreach (var overlay in overlays)
            {
                if (!ReferenceEquals(overlay, source))
                {
                    overlay.UpdatePointerFromOtherOverlay(physicalPoint);
                }
            }
        }

        foreach (var overlay in overlays)
            overlay.PointerLocationChanged += OnPointerLocationChanged;

        return () =>
        {
            foreach (var overlay in overlays)
                overlay.PointerLocationChanged -= OnPointerLocationChanged;
        };
    }

    /// <summary>
    /// Shows an overlay as a free-floating top-level window (Owner cleared) so X11
    /// does not set transient-for on a hidden/minimised MainWindow. On non-visible-owner
    /// failure, briefly ensures MainWindow is mapped and retries Show once.
    /// </summary>
    private static void ShowOverlayDetached(OverlayWindow overlay)
    {
        overlay.ClearOwner();
        try
        {
            overlay.Show();
        }
        catch (Exception ex) when (IsNonVisibleOwnerFailure(ex))
        {
            DebugHelper.WriteLine($"[OverlayManager] Show failed (non-visible owner): {ex.Message}; ensuring main window visible and retrying.");
            EnsureMainWindowVisibleForOverlayRetry();
            overlay.ClearOwner();
            overlay.Show();
        }
    }

    private static bool IsNonVisibleOwnerFailure(Exception ex)
    {
        if (ex is Win32Exception)
            return true;

        return ex.Message.Contains("non-visible owner", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureMainWindowVisibleForOverlayRetry()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } mainWindow
            && (!mainWindow.IsVisible || mainWindow.WindowState == WindowState.Minimized))
        {
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
        }
    }

    private void CloseAllOverlays()
    {
        _activeMonitorCoordinator?.Dispose();
        _activeMonitorCoordinator = null;
        _disconnectPointerPresence?.Invoke();
        _disconnectPointerPresence = null;

        foreach (var overlay in _overlays)
        {
            var handle = overlay.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            WindowDetectionService.RemoveExcludedHandle(handle);

            try
            {
                overlay.Close();
            }
            catch
            {
                // Ignore close errors
            }
        }

        _overlays.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseAllOverlays();
        _completionSource.TrySetCanceled();
    }
}
