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
    private static readonly List<IAsyncDisposable> _nativeRecordingHighlights = new();
    public static bool IsManuallyActive => _manualActive;
    public static event Action? StateChanged;

    public static void ShowWindow(MouseHighlighterOptions options)
    {
        if (_shutdown) return;
        if (!WorkflowCatalog.IsAvailable(WorkflowType.MouseHighlighter))
        {
            UploadWorkflowService.ReportError(new PlatformNotSupportedException("Mouse highlighting requires Windows or X11 with global input and click-through overlay support."));
            return;
        }
        if (_settingsWindow == null)
        {
            _settingsWindow = new MouseHighlighterWindow(options, () => _ = ImageEditorOptionsStore.PersistAsync());
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

    internal static void SetManualActive(bool active, MouseHighlighterOptions options)
    {
        if (_shutdown) return;
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
        if (await Platform.Abstractions.PlatformServices.Input.BeginRecordingHighlightAsync() is { } native)
        {
            if (_shutdown)
            {
                await native.DisposeAsync();
                throw new ObjectDisposedException(nameof(MouseHighlighterManager));
            }
            _nativeRecordingHighlights.Add(native);
            return new NativeRecordingLease(native);
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

    private sealed class NativeRecordingLease(IAsyncDisposable native) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() =>
            _nativeRecordingHighlights.Remove(native) ? native.DisposeAsync() : ValueTask.CompletedTask;
    }

    public static void RefreshOptions(MouseHighlighterOptions options)
    {
        if (_service != null && ReferenceEquals(_service.Options, options)) _service.UpdateOptions(options);
    }

    public static void Shutdown()
    {
        _shutdown = true;
        // A compositor effect enabled for a recording is restored even when XerahS exits during the recording.
        foreach (var native in _nativeRecordingHighlights.ToArray())
        {
            try
            {
                if (native is IDisposable disposable) disposable.Dispose();
                else native.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex) { Common.DebugHelper.WriteException(ex, "Could not restore the recording mouse highlight"); }
        }
        _nativeRecordingHighlights.Clear();
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
