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

using Avalonia.Threading;
using XerahS.Core;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

internal static class MouseHighlighterManager
{
    private static MouseHighlighterService? _service;
    private static MouseHighlighterWindow? _settingsWindow;
    private static bool _shutdown;
    private static bool _manualActive;
    private static int _recordingRequests;
    // The compositor's highlight (KWin's Mouse Click Animation) is one desktop setting, so the manual highlighter
    // and recordings share one use of it, ended when the last of them stops.
    private static IAsyncDisposable? _nativeHighlight;
    private static int _nativeUsers;
    private static readonly SemaphoreSlim _nativeLock = new(1, 1);
    private static IAsyncDisposable? _manualNativeLease;
    public static bool IsManuallyActive => _manualActive;
    public static event Action? StateChanged;

    public static void ShowWindow(MouseHighlighterOptions options)
    {
        if (_shutdown) return;
        if (!WorkflowCatalog.IsAvailable(WorkflowType.MouseHighlighter))
        {
            UploadWorkflowService.ReportError(new PlatformNotSupportedException("Mouse highlighting requires Windows, X11 with global input and click-through overlay support, or KDE Plasma on Wayland."));
            return;
        }
        if (_settingsWindow == null)
        {
            _settingsWindow = new MouseHighlighterWindow(options, ImageEditorOptionsStore.PersistSoon);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    public static void Toggle(MouseHighlighterOptions options)
    {
        try { SetManualActive(!IsManuallyActive, options); }
        catch (Exception ex) { UploadWorkflowService.ReportError(ex, "Could not start mouse highlighting"); }
    }

    /// <summary>
    /// Whether the highlighter is KWin's Mouse Click Animation (KDE Plasma Wayland), where XerahS cannot see the mouse
    /// to draw its own; ShareX's highlighter options do not apply to it.
    /// </summary>
    internal static bool UsesCompositorHighlight =>
        Platform.Abstractions.PlatformServices.IsInputServiceInitialized &&
        Platform.Abstractions.PlatformServices.IsWindowServiceInitialized &&
        Platform.Abstractions.PlatformServices.Input.SupportsCompositorMouseHighlight &&
        !(Platform.Abstractions.PlatformServices.Input.SupportsGlobalMouseMonitoring && Platform.Abstractions.PlatformServices.Window.SupportsClickThrough);

    internal static void SetManualActive(bool active, MouseHighlighterOptions options)
    {
        if (_shutdown) return;
        if (UsesCompositorHighlight)
        {
            SetManualCompositorActive(active);
            return;
        }
        if (active)
        {
            if (!WorkflowCatalog.IsAvailable(WorkflowType.MouseHighlighter))
                throw new PlatformNotSupportedException("Global mouse highlighting is not supported on this window system.");
            if (_service == null) _service = new MouseHighlighterService(options);
            else _service.UpdateOptions(options);
        }
        else if (_recordingRequests == 0)
        {
            _service?.Dispose();
            _service = null;
        }
        _manualActive = active;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Highlights the mouse for a screen recording: the shared overlay on X11 (keeping a manually started one), or
    /// the compositor's own effect (KDE Plasma on Wayland). The compositor calls do not block the UI thread.
    /// </summary>
    public static async Task<IAsyncDisposable> BeginRecordingAsync(MouseHighlighterOptions options)
    {
        if (_shutdown) throw new ObjectDisposedException(nameof(MouseHighlighterManager));
        if (await AcquireNativeAsync() is { } native)
        {
            if (_shutdown)
            {
                await native.DisposeAsync();
                throw new ObjectDisposedException(nameof(MouseHighlighterManager));
            }
            return native;
        }
        if (_service == null) _service = new MouseHighlighterService(options);
        _recordingRequests++;
        return new RecordingLease();
    }

    private sealed class RecordingLease : IAsyncDisposable
    {
        private bool _disposed;
        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            if (--_recordingRequests == 0 && !_manualActive)
            {
                _service?.Dispose();
                _service = null;
            }
            return ValueTask.CompletedTask;
        }
    }

    private static void SetManualCompositorActive(bool active)
    {
        if (active == _manualActive) return;
        _manualActive = active;
        StateChanged?.Invoke();
        _ = active ? StartManualCompositorAsync() : StopManualCompositorAsync();
    }

    private static async Task StartManualCompositorAsync()
    {
        try
        {
            IAsyncDisposable lease = await AcquireNativeAsync()
                ?? throw new PlatformNotSupportedException("Global mouse highlighting is not supported on this window system.");
            // Stopped (or started again) while KWin was being asked.
            if (!_manualActive || _shutdown || _manualNativeLease != null)
            {
                await lease.DisposeAsync();
                return;
            }
            _manualNativeLease = lease;
        }
        catch (Exception ex)
        {
            _manualActive = false;
            StateChanged?.Invoke();
            UploadWorkflowService.ReportError(ex, "Could not start mouse highlighting");
        }
    }

    private static async Task StopManualCompositorAsync()
    {
        IAsyncDisposable? lease = _manualNativeLease;
        _manualNativeLease = null;
        try
        {
            if (lease != null) await lease.DisposeAsync();
        }
        catch (Exception ex) { Common.DebugHelper.WriteException(ex, "Could not stop the mouse highlight"); }
    }

    /// <summary>Starts the compositor's highlight, or shares the running one. Null where there is none (X11, Windows).</summary>
    private static async Task<IAsyncDisposable?> AcquireNativeAsync()
    {
        await _nativeLock.WaitAsync();
        try
        {
            if (_nativeHighlight == null)
            {
                _nativeHighlight = await Platform.Abstractions.PlatformServices.Input.BeginRecordingHighlightAsync();
                if (_nativeHighlight == null) return null;
            }
            _nativeUsers++;
            return new NativeHighlightLease();
        }
        finally
        {
            _nativeLock.Release();
        }
    }

    private static async ValueTask ReleaseNativeAsync()
    {
        await _nativeLock.WaitAsync();
        try
        {
            if (_nativeUsers > 0 && --_nativeUsers == 0 && _nativeHighlight is { } native)
            {
                _nativeHighlight = null;
                await native.DisposeAsync();
            }
        }
        finally
        {
            _nativeLock.Release();
        }
    }

    private sealed class NativeHighlightLease : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 0 ? ReleaseNativeAsync() : ValueTask.CompletedTask;
    }

    public static void RefreshOptions(MouseHighlighterOptions options)
    {
        if (_service != null && ReferenceEquals(_service.Options, options)) _service.UpdateOptions(options);
    }

    public static void Shutdown()
    {
        _shutdown = true;
        // A compositor effect enabled for highlighting or a recording is restored even when XerahS exits meanwhile.
        if (_nativeHighlight is { } native)
        {
            _nativeHighlight = null;
            _nativeUsers = 0;
            try
            {
                if (native is IDisposable disposable) disposable.Dispose();
                else native.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex) { Common.DebugHelper.WriteException(ex, "Could not restore the mouse highlight"); }
        }
        _service?.Dispose();
        _service = null;
        _settingsWindow?.Close();
    }

    internal static void StopOnError(MouseHighlighterService service, Exception exception)
    {
        if (!ReferenceEquals(_service, service)) return;
        _service.Dispose();
        _service = null;
        // Highlighting has stopped, so the window offers to start it again.
        _manualActive = false;
        StateChanged?.Invoke();
        UploadWorkflowService.ReportError(exception, "Mouse highlighting failed");
    }
}
