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

using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Helpers;
using XerahS.RegionCapture.ScreenRecording;
using XerahS.Services.Abstractions;
using System.Runtime.InteropServices;

namespace XerahS.Core.Managers;

/// <summary>
/// Global manager for screen recording sessions
/// Coordinates recording state across UI and workflow pipelines
/// Stage 5: Workflow Pipeline Integration
/// </summary>
public class ScreenRecordingManager : IScreenRecordingManager
{
    private static readonly Lazy<ScreenRecordingManager> _lazy = new(() => new ScreenRecordingManager());
    public static ScreenRecordingManager Instance => _lazy.Value;

    private readonly object _lock = new();
    private readonly SemaphoreSlim _stopSemaphore = new(1, 1);
    private IRecordingService? _currentRecording;
    private RecordingOptions? _currentOptions;
    private TaskCompletionSource<bool>? _stopSignal;
    private readonly List<string> _segments = new();
    private RecordingOptions? _resumeOptions;
    private string? _finalOutputPath;
    private int _segmentIndex;
    private bool _isPaused;
    private bool _restartRequested;
    private bool _isFinalized;
    private string? _cachedFinalPath;
    private TimeSpan _lastDuration;
    private RecordingRuntimeCapabilities _currentCapabilities = RecordingRuntimeCapabilities.None;

    /// <summary>
    /// Task representing platform-specific recording initialization.
    /// Set by the application startup code and awaited before starting recording.
    /// </summary>
    public static System.Threading.Tasks.Task? PlatformInitializationTask { get; set; }

    private Exception? _recordingFailure;
    private bool _sessionActive;
    private bool _suppressBackendStatus; // pause, resume and Restart's discard
    private bool _finishing;
    private bool _discardRequested;
    private TaskCompletionSource<bool>? _startSignal;
    private readonly System.Diagnostics.Stopwatch _sessionClock = new();
    private System.Threading.Timer? _sessionTimer;
    // A backend whose source was chosen before the start signal, or that Restart kept from the discarded take.
    private IRecordingService? _preparedRecording;
    private System.Runtime.ExceptionServices.ExceptionDispatchInfo? _finalizationFailure;
    public bool IsWaiting => _startSignal != null;
    public event EventHandler<RecordingStartedEventArgs>? RecordingPreparing;

    internal ScreenRecordingManager()
    {
    }

    /// <summary>
    /// Factory function for creating the primary recording service.
    /// MUST be initialized by the application composition root.
    /// </summary>


    /// <summary>
    /// Event fired when recording status changes
    /// </summary>
    public event EventHandler<RecordingStatusEventArgs>? StatusChanged;

    /// <summary>
    /// Event fired when a recording error occurs
    /// </summary>
    public event EventHandler<RecordingErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Event fired when a recording completes successfully
    /// </summary>
    public event EventHandler<string>? RecordingCompleted;

    /// <summary>
    /// Event fired when recording starts, includes information about the recording method
    /// </summary>
    public event EventHandler<RecordingStartedEventArgs>? RecordingStarted;

    /// <summary>
    /// Indicates whether a recording is currently active
    /// </summary>
    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _sessionActive;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _isPaused;
            }
        }
    }

    /// <summary>
    /// Indicates whether the current recording is using FFmpeg fallback
    /// </summary>
    public bool IsUsingFallback { get; private set; }

    public RecordingRuntimeCapabilities CurrentCapabilities
    {
        get
        {
            lock (_lock)
            {
                return _currentCapabilities;
            }
        }
    }

    public bool CanPauseResumeCurrentRecording
    {
        get
        {
            lock (_lock)
            {
                return _currentCapabilities.SupportsPauseResume;
            }
        }
    }

    /// <summary>
    /// Current recording options (null if not recording)
    /// </summary>
    public RecordingOptions? CurrentOptions
    {
        get
        {
            lock (_lock)
            {
                return _currentOptions ?? _resumeOptions;
            }
        }
    }

    /// <summary>
    /// Planned final output path for the active recording, after backend-specific
    /// container adjustments such as .mp4 -> .webm fallback.
    /// </summary>
    public string? PlannedOutputPath
    {
        get
        {
            lock (_lock)
            {
                return _finalOutputPath;
            }
        }
    }

    /// <summary>
    /// Signals the current recording task to stop.
    /// Used by the hotkey handler to resume the waiting WorkerTask.
    /// </summary>
    public void SignalStop()
    {
        lock (_lock)
        {
            if (_startSignal != null) _startSignal.TrySetResult(true);
            else _stopSignal?.TrySetResult(true);
        }
    }

    public void SignalStart() => _startSignal?.TrySetResult(true);

    /// <summary>
    /// Restart (ShareX #7255): wake the recording workflow like Stop does, flagged so it discards
    /// the current take and starts a new one with the same region and settings.
    /// </summary>
    public void RequestRestart()
    {
        lock (_lock)
        {
            if (!_sessionActive || _isFinalized)
            {
                return;
            }

            _restartRequested = true;
            _stopSignal?.TrySetResult(true);
        }

        DebugHelper.WriteLine("ScreenRecordingManager: Restart requested.");
    }

    public bool ConsumeRestartRequest()
    {
        lock (_lock)
        {
            bool requested = _restartRequested;
            _restartRequested = false;
            return requested;
        }
    }

    /// <summary>
    /// Asynchronously waits for the Stop signal.
    /// Called by the WorkerTask to yield execution while recording.
    /// </summary>
    public Task WaitForStopSignalAsync()
    {
        lock (_lock)
        {
            if (_stopSignal == null)
            {
                _stopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return _stopSignal.Task;
        }
    }

    /// <summary>
    /// Starts a new recording session
    /// </summary>
    /// <param name="options">Recording configuration</param>
    /// <exception cref="InvalidOperationException">Thrown if a recording is already in progress</exception>
    public async Task StartRecordingAsync(RecordingOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        // Wait for platform recording initialization to complete if it's still running
        // This ensures factories are set up before we try to create recording services
        await EnsureRecordingInitialized();

        // Verify recording services are available after initialization.
        // Windows uses CaptureSourceFactory + EncoderFactory (ScreenRecorderService); Linux/macOS may use NativeRecordingServiceFactory or FallbackServiceFactory.
        bool hasNativeFactory = ScreenRecorderService.NativeRecordingServiceFactory != null;
        bool hasFallbackFactory = ScreenRecorderService.FallbackServiceFactory != null;
        bool hasCaptureSourceFactory = ScreenRecorderService.CaptureSourceFactory != null;
        if (!hasNativeFactory && !hasFallbackFactory && !hasCaptureSourceFactory)
        {
            string message = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Screen recording is not available. On Linux Wayland, ensure xdg-desktop-portal with ScreenCast support is available, PipeWire is running, and either FFmpeg pipewire, GStreamer pipewiresrc, or wf-recorder is installed."
                : "Screen recording is not available. Ensure platform recording has been initialized.";
            throw new InvalidOperationException(message);
        }

        if (string.IsNullOrEmpty(options.OutputPath))
        {
            string screenCapturesFolder = SettingsManager.ScreencastsFolder;
            string dateFolderPath = Path.Combine(screenCapturesFolder, DateTime.Now.ToString("yyyy-MM"));
            Directory.CreateDirectory(dateFolderPath);

            string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
            options.OutputPath = Path.Combine(dateFolderPath, fileName);
            DebugHelper.WriteLine($"ScreenRecordingManager: Generated default output path: {options.OutputPath}");
        }

        lock (_lock)
        {
            if (_sessionActive) throw new InvalidOperationException("A recording is already in progress.");
            _sessionActive = true;
            _discardRequested = false;
            _isFinalized = false;
            _cachedFinalPath = null;
            _finalizationFailure = null;
            _currentOptions = options;
        }

        await _stopSemaphore.WaitAsync();
        try
        {
            await PrepareRecordingServiceAsync(options);
        }
        catch (Exception ex)
        {
            EndSessionWithoutOutput();
            if (ex is OperationCanceledException && _discardRequested && ex is not RecordingAbortedException) throw new RecordingAbortedException();
            throw;
        }
        finally { _stopSemaphore.Release(); }

        await RunTakeAsync(options);
    }

    /// <summary>
    /// Restart (ShareX #7255): discards the current take and records the next one with the same options. A backend
    /// that chose its source for the session (the Wayland portal) keeps it, so the source picker does not open again.
    /// </summary>
    public async Task RestartRecordingAsync(RecordingOptions options)
    {
        await _stopSemaphore.WaitAsync();
        try
        {
            lock (_lock)
            {
                if (!_sessionActive || _isFinalized || _discardRequested) throw new RecordingAbortedException();
            }
            _sessionTimer?.Dispose();
            _sessionTimer = null;
            _sessionClock.Stop();
            // The discarded take's Finalizing and Idle are not the session's: the controls stay for the next take.
            _suppressBackendStatus = true;
            try { await StopRecordingCoreAsync(signalStop: false, discard: true, keepSession: true); }
            finally { _suppressBackendStatus = false; }
            CleanupSegments(deleteFinalOutput: false);
        }
        catch (Exception ex) when (ex is not RecordingAbortedException)
        {
            EndSessionWithoutOutput();
            throw;
        }
        finally { _stopSemaphore.Release(); }

        await RunTakeAsync(options);
    }

    Task IScreenRecordingManager.RestartRecordingAsync(object options) => options is RecordingOptions recordingOptions
        ? RestartRecordingAsync(recordingOptions)
        : throw new ArgumentException("Recording options must be a RecordingOptions instance.", nameof(options));

    /// <summary>Choose a session backend's capture source before the controls, the start delay, and the manual start.</summary>
    private async Task PrepareRecordingServiceAsync(RecordingOptions options)
    {
        if (_preparedRecording != null || ShouldForceFallback(options) || ScreenRecorderService.NativeRecordingServiceFactory == null) return;
        IRecordingService service = ScreenRecorderService.NativeRecordingServiceFactory();
        lock (_lock) _preparedRecording = service;
        if (service is ISessionRecordingService session)
        {
            StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Initializing, TimeSpan.Zero));
            await session.PrepareRecordingAsync(options);
        }
    }

    /// <summary>One take: show the controls, wait for the delay or the manual start, then start the backend.</summary>
    private async Task RunTakeAsync(RecordingOptions options)
    {
        TaskCompletionSource<bool> startSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _currentOptions = options;
            _stopSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _startSignal = startSignal;
        }
        _recordingFailure = null;
        _sessionClock.Reset();
        _lastDuration = TimeSpan.Zero;
        int delay = options.AutoStart ? global::XerahS.Core.TaskHelpers.GetCaptureStartDelayMilliseconds(options.StartDelay) : 0;
        RecordingPreparing?.Invoke(this, new RecordingStartedEventArgs(false, options));
        StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Waiting, TimeSpan.FromMilliseconds(delay)));
        if (options.AutoStart)
        {
            await Task.WhenAny(startSignal.Task, Task.Delay(delay));
            startSignal.TrySetResult(true);
        }
        bool shouldStart = await startSignal.Task;
        lock (_lock) _startSignal = null;

        await _stopSemaphore.WaitAsync();
        try
        {
            if (!shouldStart || _discardRequested) throw new RecordingAbortedException();
            bool preferFallback = ShouldForceFallback(options);
            RecordingOptions optionsToStart = PrepareRecordingOptions(options, isResume: false);
            await StartRecordingCoreAsync(optionsToStart, preferFallback);
            if (_recordingFailure != null)
                throw new RecordingFailedException(RecordingEncoding.Summarize(_recordingFailure.Message), _recordingFailure);
            if (_isFinalized || _discardRequested) return;
            _sessionTimer = new System.Threading.Timer(_ =>
            {
                var elapsed = _sessionClock.Elapsed;
                _lastDuration = elapsed;
                if (_sessionClock.IsRunning)
                {
                    StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Recording, elapsed));
                    if (options.Duration > 0 && elapsed.TotalSeconds >= options.Duration) _stopSignal?.TrySetResult(true);
                }
            }, null, 100, 100);
        }
        catch (Exception ex)
        {
            EndSessionWithoutOutput();
            if (ex is OperationCanceledException && _discardRequested && ex is not RecordingAbortedException) throw new RecordingAbortedException();
            throw;
        }
        finally { _stopSemaphore.Release(); }
    }

    /// <summary>Ends a session that produced no output: aborted, failed to start, or its source was not chosen.</summary>
    private void EndSessionWithoutOutput()
    {
        _sessionTimer?.Dispose();
        _sessionTimer = null;
        _sessionClock.Stop();
        ReleasePreparedRecording();
        lock (_lock)
        {
            _startSignal = null;
            _stopSignal?.TrySetResult(true);
            _isFinalized = true;
            _cachedFinalPath = null;
            _sessionActive = false;
            _currentOptions = null;
        }
        StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Idle, _sessionClock.Elapsed));
    }

    private void ReleasePreparedRecording()
    {
        IRecordingService? prepared;
        lock (_lock)
        {
            prepared = _preparedRecording;
            _preparedRecording = null;
        }
        if (prepared != null) CleanupCurrentRecording(prepared);
    }

    async Task IScreenRecordingManager.StartRecordingAsync(object options)
    {
        if (options is not RecordingOptions recordingOptions)
        {
            throw new ArgumentException("Recording options must be a RecordingOptions instance.", nameof(options));
        }

        await StartRecordingAsync(recordingOptions);
    }

    /// <summary>
    /// Stops the current recording session.
    /// Thread-safe: Uses a semaphore to prevent concurrent stop/finalization operations.
    /// If already finalized, returns the cached final path without re-processing.
    /// </summary>
    /// <returns>Output file path if recording completed successfully, null otherwise</returns>
    public async Task<string?> StopRecordingAsync()
    {
        // Fast path: if already finalized, return cached path immediately
        lock (_lock)
        {
            if (_isFinalized)
            {
                DebugHelper.WriteLine("ScreenRecordingManager: Already finalized, returning cached path");
                // A failed finalization is reported again rather than encoded a second time.
                _finalizationFailure?.Throw();
                return _cachedFinalPath;
            }
        }

        // Acquire semaphore to ensure only one stop/finalization runs at a time
        await _stopSemaphore.WaitAsync();
        try
        {
            // Double-check after acquiring lock (another call may have finished)
            lock (_lock)
            {
                if (_isFinalized)
                {
                    DebugHelper.WriteLine("ScreenRecordingManager: Already finalized (after semaphore), returning cached path");
                    _finalizationFailure?.Throw();
                    return _cachedFinalPath;
                }
            }

            _finishing = true;
            if (_recordingFailure != null)
                throw new RecordingFailedException(RecordingEncoding.Summarize(_recordingFailure.Message), _recordingFailure);
            _sessionTimer?.Dispose();
            _sessionTimer = null;
            _sessionClock.Stop();
            bool wasPaused = IsPaused;
            if (wasPaused)
            {
                // Stop while paused: finalize segments without starting a new recording.
                _isPaused = false;
                StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Finalizing, _lastDuration));
            }

            await StopRecordingCoreAsync(signalStop: true);
            string? finalPath = await FinalizeSegmentsAsync();

            if (!string.IsNullOrEmpty(finalPath))
            {
                RecordingCompleted?.Invoke(this, finalPath);
            }

            if (string.IsNullOrEmpty(finalPath) && !string.IsNullOrEmpty(_finalOutputPath) && File.Exists(_finalOutputPath))
            {
                finalPath = _finalOutputPath;
            }

            _sessionActive = false;
            StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Idle, _lastDuration));

            // Mark as finalized and cache the path for subsequent calls
            lock (_lock)
            {
                _isFinalized = true;
                _cachedFinalPath = finalPath;
            }

            return finalPath;
        }
        catch (Exception ex)
        {
            _sessionTimer?.Dispose();
            _sessionTimer = null;
            ReleasePreparedRecording();
            lock (_lock)
            {
                _sessionActive = false;
                _isFinalized = true;
                _cachedFinalPath = null;
                _finalizationFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
            }
            StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Error, _lastDuration));
            throw;
        }
        finally
        {
            _finishing = false;
            _stopSemaphore.Release();
        }
    }

    /// <summary>
    /// Aborts the current recording session without saving. As ShareX's AbortRecording, this does not ask: the
    /// recording controls ask first when "Ask for confirmation when aborting" is on.
    /// </summary>
    public Task AbortRecordingAsync() => DiscardRecordingAsync();

    public async Task DiscardRecordingAsync()
    {
        lock (_lock)
        {
            if (!_sessionActive) return;
            _discardRequested = true;
            (_currentRecording ?? _preparedRecording)?.CancelInitialization();
            if (_startSignal != null)
            {
                // The waiting take ends the session itself.
                _startSignal.TrySetResult(false);
                return;
            }
        }
        await _stopSemaphore.WaitAsync();
        try
        {
            if (!_sessionActive) return;
            _finishing = true;
            _sessionTimer?.Dispose();
            _sessionTimer = null;
            _sessionClock.Stop();
            await StopRecordingCoreAsync(signalStop: false, discard: true);
            CleanupSegments(deleteFinalOutput: false);
            ReleasePreparedRecording();
            lock (_lock)
            {
                _isFinalized = true;
                _cachedFinalPath = null;
                _sessionActive = false;
                _stopSignal?.TrySetResult(true);
            }
            StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Idle, _sessionClock.Elapsed));
        }
        finally { _finishing = false; _stopSemaphore.Release(); }
    }

    /// <summary>
    /// Factory function for creating the fallback recording service (e.g. FFmpeg).
    /// MUST be initialized by the application composition root.
    /// </summary>


    // ... (existing code) ...

    private static IRecordingService CreateRecordingService(bool useFallback)
    {
        if (useFallback)
        {
            // Direct instantiation of FFmpeg fallback service
            return new XerahS.RegionCapture.ScreenRecording.FFmpegRecordingService();
        }

        // Check for native recording service factory (e.g., MacOSNativeRecordingService)
        if (ScreenRecorderService.NativeRecordingServiceFactory != null)
        {
            XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "NATIVE", "Using NativeRecordingServiceFactory");
            return ScreenRecorderService.NativeRecordingServiceFactory();
        }

        // Fallback to capture source + encoder pattern (Windows)
        return new XerahS.RegionCapture.ScreenRecording.ScreenRecorderService();
    }

    private static bool ShouldForceFallback(RecordingOptions options)
    {
        if (OperatingSystem.IsLinux() && (options.AudioOnly ||
            (options.FFmpegOptions != null && !IsWaylandSession))) return true;
        var settings = options.Settings;

        // Detect Wayland - FFmpeg x11grab doesn't work on Wayland
        bool isWayland = IsWaylandSession;
        bool hasNativeFactory = ScreenRecorderService.NativeRecordingServiceFactory != null;

        DebugHelper.WriteLine(
            $"ShouldForceFallback: isWayland={isWayland}, hasNativeFactory={hasNativeFactory}, " +
            $"UseModernCapture={options.UseModernCapture}, LinuxRecordingBackendPreference={options.LinuxRecordingBackendPreference}");

        if (OperatingSystem.IsLinux())
        {
            switch (options.LinuxRecordingBackendPreference)
            {
                case LinuxRecordingBackendPreference.Native:
                    if (hasNativeFactory)
                    {
                        XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "NATIVE", "Linux native recording backend requested -> using native backend");
                        return false;
                    }

                    XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "FALLBACK", "Linux native recording backend requested but unavailable -> using FFmpeg fallback");
                    return true;
                case LinuxRecordingBackendPreference.FFmpeg:
                    if (isWayland && hasNativeFactory)
                    {
                        XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "NATIVE", "Linux FFmpeg fallback requested on Wayland -> using native backend because x11grab is unavailable");
                        return false;
                    }

                    if (isWayland && !hasNativeFactory)
                    {
                        DebugHelper.WriteLine("WARNING: Linux FFmpeg fallback requested on Wayland but native recording is unavailable.");
                    }

                    XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "FALLBACK", "Linux FFmpeg fallback backend requested -> using FFmpeg");
                    return true;
            }
        }

        // On non-Linux platforms, UseModernCapture still selects between native and fallback recording paths.
        // Linux now uses LinuxRecordingBackendPreference instead.
        if (!OperatingSystem.IsLinux() && !options.UseModernCapture)
        {
            if (isWayland && hasNativeFactory)
            {
                XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "NATIVE", "UseModernCapture is disabled but on Wayland with portal available -> using native (x11grab won't work)");
                return false; // Use native on Wayland even if UseModernCapture is false
            }

            if (isWayland && !hasNativeFactory)
            {
                DebugHelper.WriteLine("WARNING: On Wayland but NativeRecordingServiceFactory is null - portal recording not initialized!");
            }

            XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "FALLBACK", "UseModernCapture is disabled -> forcing FFmpeg fallback");
            return true;
        }

        if (OperatingSystem.IsLinux() && isWayland && hasNativeFactory) return false;

        if (settings?.ForceFFmpeg == true)
        {
            // On Wayland, warn that ForceFFmpeg won't work and fall back to native if available
            if (isWayland && ScreenRecorderService.NativeRecordingServiceFactory != null)
            {
                XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "NATIVE", "ForceFFmpeg requested but on Wayland -> using native (x11grab won't work)");
                return false;
            }

            XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "FALLBACK", "ForceFFmpeg setting is enabled -> using FFmpeg");
            return true;
        }

        // Audio capture currently routes through FFmpeg fallback
        if (settings is not null && (settings.CaptureSystemAudio || settings.CaptureMicrophone))
        {
            if (isWayland && ScreenRecorderService.NativeRecordingServiceFactory != null)
            {
                XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "NATIVE", "Audio capture requested on Wayland -> using native portal backend");
                return false;
            }

            XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "FALLBACK", "Audio capture requested -> using FFmpeg");
            return true;
        }

        if (settings is not null && RecordingCodecSupportPolicy.RequiresFfmpegFallback(settings.Codec))
        {
            XerahS.Common.TroubleshootingHelper.Log(
                "ScreenRecorder",
                "FALLBACK",
                $"Codec {settings.Codec} requires FFmpeg because the native backend only supports H.264 in this build.");
            return true;
        }

        // Check if we have a native recording service factory (complete IRecordingService)
        if (ScreenRecorderService.NativeRecordingServiceFactory != null)
        {
            XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "NATIVE", "NativeRecordingServiceFactory available -> using native");
            return false; // Use native, not fallback
        }

        // If native capture factory is not configured (e.g. macOS without native), must use fallback
        if (ScreenRecorderService.CaptureSourceFactory == null)
        {
            XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "FALLBACK", "Native CaptureSourceFactory not set -> forcing FFmpeg fallback");
            return true;
        }

        return false;
    }

    private static bool IsWaylandSession =>
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.Equals("wayland", StringComparison.OrdinalIgnoreCase) == true ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    private static bool CanFallbackFrom(Exception ex)
    {
        return !(OperatingSystem.IsLinux() && IsWaylandSession)
            && (ex is PlatformNotSupportedException || ex is COMException);
    }

    /// <summary>
    /// Core recording start logic shared by both initial start and resume-after-pause.
    /// Attempts native recording first, then falls back to FFmpeg if the native attempt
    /// throws a recoverable exception.
    /// </summary>
    private async Task StartRecordingCoreAsync(RecordingOptions optionsToStart, bool preferFallback)
    {
        Exception? lastError = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool useFallback = preferFallback || attempt == 1;
            IRecordingService recordingService;
            RecordingRuntimeCapabilities capabilities;

            // Create service and assign state within single lock to prevent race condition
            lock (_lock)
            {
                if (_currentRecording != null)
                {
                    throw new InvalidOperationException("A recording is already in progress. Stop the current recording before starting a new one.");
                }

                try
                {
                    if (!useFallback && _preparedRecording != null)
                    {
                        recordingService = _preparedRecording;
                        _preparedRecording = null;
                    }
                    else
                    {
                        recordingService = CreateRecordingService(useFallback);
                    }
                    capabilities = recordingService.GetCapabilities(optionsToStart);
                    _currentRecording = recordingService;
                    _currentOptions = optionsToStart;
                    _currentCapabilities = capabilities;
                }
                catch
                {
                    // Rollback state on service creation failure
                    _currentRecording = null;
                    _currentOptions = null;
                    _currentCapabilities = RecordingRuntimeCapabilities.None;
                    _stopSignal = null;
                    throw;
                }
            }

            try
            {
                XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "MANAGER", $"Attempt {attempt + 1}: useFallback={useFallback}");
                WireRecordingEvents(recordingService);

                DebugHelper.WriteLine($"ScreenRecordingManager: Starting {(useFallback ? "fallback (FFmpeg)" : "native")} recording - Mode={optionsToStart.Mode}, Codec={optionsToStart.Settings?.Codec}, FPS={optionsToStart.Settings?.FPS}");

                XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "MANAGER", "Calling recordingService.StartRecordingAsync");
                await recordingService.StartRecordingAsync(optionsToStart);
                UpdateFinalOutputExtensionFromSegmentPath(optionsToStart.OutputPath);

                XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "MANAGER", "Recording started successfully");

                // Track fallback status and notify UI
                IsUsingFallback = useFallback;
                RecordingStarted?.Invoke(this, new RecordingStartedEventArgs(useFallback, optionsToStart));

                return;
            }
            catch (Exception ex) when (!useFallback && CanFallbackFrom(ex))
            {
                XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "MANAGER", $"Native recording failed: {ex.Message}, attempting FFmpeg fallback");
                DebugHelper.WriteException(ex, "ScreenRecordingManager: Native recording failed, attempting FFmpeg fallback...");
                lastError = ex;
                CleanupCurrentRecording(recordingService);
                lock (_lock)
                {
                    _currentCapabilities = RecordingRuntimeCapabilities.None;
                    _currentRecording = null;
                    _currentOptions = null;
                }
                preferFallback = true;
            }
            catch (Exception ex)
            {
                XerahS.Common.TroubleshootingHelper.Log("ScreenRecorder", "MANAGER", $"Recording failed with unrecoverable error: {ex.Message}");

                try
                {
                    CleanupCurrentRecording(recordingService);
                }
                catch (Exception cleanupEx)
                {
                    DebugHelper.WriteException(cleanupEx, "ScreenRecordingManager: Error during recording cleanup");
                }

                lock (_lock)
                {
                    _currentOptions = null;
                    _currentRecording = null;
                    _currentCapabilities = RecordingRuntimeCapabilities.None;
                }
                throw;
            }
        }

        lock (_lock)
        {
            _currentOptions = null;
            _currentRecording = null;
            _currentCapabilities = RecordingRuntimeCapabilities.None;
        }

        throw lastError ?? new InvalidOperationException("Recording failed and no fallback recording service is available.");
    }

    private void WireRecordingEvents(IRecordingService recordingService)
    {
        recordingService.StatusChanged += OnRecordingStatusChanged;
        recordingService.ErrorOccurred += OnRecordingErrorOccurred;
    }

    private void CleanupCurrentRecording(IRecordingService recordingService)
    {
        try
        {
            recordingService.StatusChanged -= OnRecordingStatusChanged;
            recordingService.ErrorOccurred -= OnRecordingErrorOccurred;
            recordingService.Dispose();
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private void OnRecordingStatusChanged(object? sender, RecordingStatusEventArgs e)
    {
        if (_suppressBackendStatus) return;
        if (e.Status == RecordingStatus.Recording) _sessionClock.Start();
        else _sessionClock.Stop();
        _lastDuration = _sessionClock.Elapsed;
        if (e.Status == RecordingStatus.Idle)
        {
            if (!_finishing) _stopSignal?.TrySetResult(true);
            return; // The manager emits Idle after final output processing.
        }
        e = new RecordingStatusEventArgs(e.Status, _lastDuration);
        DebugHelper.WriteLine($"ScreenRecordingManager: Status changed to {e.Status}, Duration={e.Duration}");
        StatusChanged?.Invoke(this, e);
    }

    private void OnRecordingErrorOccurred(object? sender, RecordingErrorEventArgs e)
    {
        DebugHelper.WriteException(e.Error, $"ScreenRecordingManager: Recording error (Fatal={e.IsFatal})");
        ErrorOccurred?.Invoke(this, e);

        // Clean up on fatal error and unblock the waiting WorkerTask
        if (e.IsFatal)
        {
            _recordingFailure = e.Error;
            _sessionClock.Stop();
            _sessionTimer?.Dispose();
            _sessionTimer = null;
            lock (_lock)
            {
                if (_currentRecording != null)
                {
                    CleanupCurrentRecording(_currentRecording);
                }

                _currentRecording = null;
                _currentOptions = null;
                _currentCapabilities = RecordingRuntimeCapabilities.None;
            }

            SignalStop();
        }
    }

    public async Task TogglePauseResumeAsync()
    {
        if (IsPaused)
        {
            await ResumeRecordingAsync();
        }
        else
        {
            await PauseRecordingAsync();
        }
    }

    public async Task PauseRecordingAsync()
    {
        await _stopSemaphore.WaitAsync();
        try
        {
            if (!IsRecording || IsPaused || !CurrentCapabilities.SupportsPauseResume) return;
            _suppressBackendStatus = true;
            _sessionClock.Stop();
            if (_currentRecording is IPausableRecordingService native) await native.PauseRecordingAsync();
            else await StopRecordingCoreAsync(signalStop: false);
            _isPaused = true;
            StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Paused, _sessionClock.Elapsed));
        }
        catch { _sessionClock.Start(); throw; }
        finally { _suppressBackendStatus = false; _stopSemaphore.Release(); }
    }

    public async Task ResumeRecordingAsync()
    {
        await _stopSemaphore.WaitAsync();
        try
        {
            if (!IsPaused || _resumeOptions == null) return;
            _suppressBackendStatus = true;
            if (_currentRecording is IPausableRecordingService native) await native.ResumeRecordingAsync();
            else await StartRecordingInternalAsync(_resumeOptions, isResume: true);
            _isPaused = false;
            _sessionClock.Start();
            StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Recording, _sessionClock.Elapsed));
        }
        finally { _suppressBackendStatus = false; _stopSemaphore.Release(); }
    }

    private RecordingOptions PrepareRecordingOptions(RecordingOptions options, bool isResume)
    {
        if (!isResume)
        {
            _segments.Clear();
            _restartRequested = false;
            _segmentIndex = 0;
            _isPaused = false;
            _isFinalized = false;
            _cachedFinalPath = null;
            _finalOutputPath = options.OutputPath;
            _resumeOptions = CloneOptions(options);
        }

        if (string.IsNullOrEmpty(_finalOutputPath))
        {
            _finalOutputPath = options.OutputPath;
        }

        var segmentPath = BuildSegmentPath(_finalOutputPath!, _segmentIndex++);
        var capture = CloneOptions(options, segmentPath);
        capture.IsLossless = options.TwoPassEncoding && !options.AudioOnly;
        if (capture.IsLossless) capture.OutputPath = Path.ChangeExtension(segmentPath, "mp4");
        return capture;
    }

    private void UpdateFinalOutputExtensionFromSegmentPath(string? actualSegmentPath)
    {
        if (_resumeOptions?.TwoPassEncoding == true || string.IsNullOrWhiteSpace(actualSegmentPath))
        {
            return;
        }

        string actualExtension = Path.GetExtension(actualSegmentPath);
        if (string.IsNullOrWhiteSpace(actualExtension))
        {
            return;
        }

        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_finalOutputPath))
            {
                return;
            }

            string currentExtension = Path.GetExtension(_finalOutputPath);
            if (string.Equals(currentExtension, actualExtension, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _finalOutputPath = Path.ChangeExtension(_finalOutputPath, actualExtension);
            if (_resumeOptions != null)
            {
                _resumeOptions.OutputPath = _finalOutputPath;
            }

            DebugHelper.WriteLine($"ScreenRecordingManager: Adjusted final output extension to match recorder container: {_finalOutputPath}");
        }
    }

    private async Task StartRecordingInternalAsync(RecordingOptions options, bool isResume)
    {
        await EnsureRecordingInitialized();

        bool preferFallback = ShouldForceFallback(options);
        RecordingOptions optionsToStart = PrepareRecordingOptions(options, isResume);

        await StartRecordingCoreAsync(optionsToStart, preferFallback);
    }

    private async Task StopRecordingCoreAsync(bool signalStop, bool discard = false, bool keepSession = false)
    {
        bool sessionKept = false;
        IRecordingService? recordingService;
        string? outputPath;
        string? fallbackSegmentPath;

        lock (_lock)
        {
            if (_currentRecording == null)
            {
                DebugHelper.WriteLine("ScreenRecordingManager: No recording in progress to stop");
                if (signalStop)
                {
                    SignalStop();
                }
                return;
            }

            recordingService = _currentRecording;
            outputPath = _currentOptions?.OutputPath;
            fallbackSegmentPath = GetLastSegmentPath();
        }

        try
        {
            DebugHelper.WriteLine("ScreenRecordingManager: Stopping recording...");
            if (discard && keepSession && recordingService is ISessionRecordingService session)
            {
                await session.DiscardTakeAsync();
                sessionKept = true;
            }
            else if (discard && recordingService is IAbortableRecordingService abortable)
                await abortable.AbortRecordingAsync();
            else await recordingService.StopRecordingAsync();
            if (!discard && _recordingFailure != null)
                throw new InvalidOperationException("Recording failed while stopping.", _recordingFailure);

            if (signalStop)
            {
                SignalStop();
            }

            string? resolvedOutput = outputPath;
            if (string.IsNullOrEmpty(resolvedOutput) && !string.IsNullOrEmpty(fallbackSegmentPath))
            {
                resolvedOutput = fallbackSegmentPath;
            }

            if (!string.IsNullOrEmpty(resolvedOutput))
            {
                if (!discard && !File.Exists(resolvedOutput))
                {
                    DebugHelper.WriteLine($"ScreenRecordingManager: Output not found yet, waiting: {resolvedOutput}");
                    bool appeared = await WaitForFileAsync(resolvedOutput, TimeSpan.FromSeconds(5));
                    DebugHelper.WriteLine($"ScreenRecordingManager: Output wait completed. Found={appeared} Path={resolvedOutput}");

                    if (!appeared)
                    {
                        // List files in the directory to aid debugging
                        try
                        {
                            string? dir = Path.GetDirectoryName(resolvedOutput);
                            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            {
                                string searchPattern = "*" + Path.GetExtension(resolvedOutput);
                                var files = Directory.GetFiles(dir, searchPattern);
                                DebugHelper.WriteLine($"ScreenRecordingManager: Files in {dir}: {string.Join(", ", files.Select(Path.GetFileName))}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugHelper.WriteException(ex, "ScreenRecordingManager: Error listing directory");
                        }
                    }
                }

                if (!File.Exists(resolvedOutput) && !string.IsNullOrEmpty(fallbackSegmentPath) && File.Exists(fallbackSegmentPath))
                {
                    DebugHelper.WriteLine($"ScreenRecordingManager: Using fallback segment path: {fallbackSegmentPath}");
                    resolvedOutput = fallbackSegmentPath;
                }

                if (File.Exists(resolvedOutput))
                {
                    _segments.Add(resolvedOutput);
                }
                else if (!discard)
                {
                    throw new InvalidOperationException($"Recording stopped without producing an output file: {resolvedOutput}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "ScreenRecordingManager: Error stopping recording");
            throw;
        }
        finally
        {
            lock (_lock)
            {
                if (sessionKept)
                {
                    // Restart records the next take with the same source.
                    recordingService.StatusChanged -= OnRecordingStatusChanged;
                    recordingService.ErrorOccurred -= OnRecordingErrorOccurred;
                    _preparedRecording = recordingService;
                }
                else if (recordingService != null)
                {
                    CleanupCurrentRecording(recordingService);
                }

                _currentRecording = null;
                _currentOptions = null;
            }
        }
    }

    private async Task<string?> FinalizeSegmentsAsync()
    {
        if (_segments.Count == 0 || _finalOutputPath == null) return null;
        string final = _finalOutputPath;
        string ffmpeg = _resumeOptions?.FFmpegOverridePath ?? PathsManager.GetFFmpegPath();
        string input = _segments[0];
        string? concat = null;
        try
        {
            if (_segments.Count > 1)
            {
                concat = FileHelpers.AppendTextToFileName(input, "-concat");
                await RecordingEncoding.ConcatenateAsync(ffmpeg, _segments, concat);
                input = concat;
            }
            if (_resumeOptions is { TwoPassEncoding: true, AudioOnly: false } options)
            {
                string encoded = FileHelpers.AppendTextToFileName(final, "-encoded");
                // As ShareX's two-pass encoding, report the encoding progress (shown in the tray).
                StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Finalizing, _lastDuration) { EncodingProgress = 0 });
                await RecordingEncoding.EncodeAsync(ffmpeg, input, options.Clone(encoded), progress =>
                    StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecordingStatus.Finalizing, _lastDuration) { EncodingProgress = progress }));
                File.Move(encoded, final, overwrite: true);
            }
            else File.Move(input, final, overwrite: true);
            foreach (string segment in _segments)
                if (File.Exists(segment)) File.Delete(segment);
            _segments.Clear();
            ResetSegmentState();
            return final;
        }
        catch (RecordingFailedException ex)
        {
            throw new RecordingFailedException($"{ex.Message} The recording was kept: {string.Join(", ", _segments)}", ex);
        }
        finally
        {
            // Originals are retained until encoding and moving the final output succeeded.
            if (concat != null && File.Exists(concat)) File.Delete(concat);
        }
    }

    private void CleanupSegments(bool deleteFinalOutput)
    {
        foreach (var segment in _segments)
        {
            try
            {
                if (File.Exists(segment))
                {
                    File.Delete(segment);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }

        _segments.Clear();

        if (deleteFinalOutput && !string.IsNullOrEmpty(_finalOutputPath))
        {
            try
            {
                if (File.Exists(_finalOutputPath))
                {
                    File.Delete(_finalOutputPath);
                }
            }
            catch
            {
                // Ignore
            }
        }

        ResetSegmentState();
    }

    private static RecordingOptions CloneOptions(RecordingOptions source, string? outputPath = null)
    {
        return source.Clone(outputPath);
    }

    private static string BuildSegmentPath(string outputPath, int index)
    {
        string directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(outputPath);
        string extension = Path.GetExtension(outputPath);
        string segmentFileName = $"{fileName}.part{index:D3}{extension}";
        return Path.Combine(directory, segmentFileName);
    }

    private string? GetLastSegmentPath()
    {
        if (string.IsNullOrEmpty(_finalOutputPath))
        {
            return null;
        }

        int lastIndex = Math.Max(0, _segmentIndex - 1);
        return BuildSegmentPath(_finalOutputPath, lastIndex);
    }

    private void ResetSegmentState()
    {
        _resumeOptions = null;
        _finalOutputPath = null;
        _segmentIndex = 0;
        _isPaused = false;
        _isFinalized = false;
        _cachedFinalPath = null;
        _currentCapabilities = RecordingRuntimeCapabilities.None;
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (File.Exists(path))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return File.Exists(path);
    }

    /// <summary>
    /// Ensures platform recording initialization has completed before starting recording.
    /// Waits for the async initialization task if it's still running.
    /// </summary>
    private static async Task EnsureRecordingInitialized()
    {
        try
        {
            var initTask = PlatformInitializationTask;

            if (initTask != null)
            {
                if (!initTask.IsCompleted)
                {
                    DebugHelper.WriteLine("ScreenRecordingManager: Waiting for recording initialization to complete...");
                    await initTask;
                }

                if (initTask.IsFaulted)
                {
                    throw new InvalidOperationException("Recording initialization failed", initTask.Exception);
                }

                DebugHelper.WriteLine("ScreenRecordingManager: Recording initialization completed successfully");
            }
        }
        catch (Exception ex)
        {
            // Initialization failed - re-throw so caller knows recording won't work
            DebugHelper.WriteException(ex, "ScreenRecordingManager: Error waiting for recording initialization");
            throw;
        }
    }
}
