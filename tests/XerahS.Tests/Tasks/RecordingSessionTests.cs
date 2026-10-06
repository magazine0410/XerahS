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

using NUnit.Framework;
using XerahS.Core.Managers;
using XerahS.RegionCapture.ScreenRecording;

namespace XerahS.Tests.Tasks;

[TestFixture, NonParallelizable]
public sealed class RecordingSessionTests
{
    private Func<IRecordingService>? _oldFactory;
    private Task? _oldInitialization;
    private ScreenRecordingManager _manager = null!;
    private FakeRecorder _recorder = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _oldFactory = ScreenRecorderService.NativeRecordingServiceFactory;
        _oldInitialization = ScreenRecordingManager.PlatformInitializationTask;
        ScreenRecordingManager.PlatformInitializationTask = Task.CompletedTask;
        _recorder = new FakeRecorder();
        ScreenRecorderService.NativeRecordingServiceFactory = () => _recorder;
        _manager = new ScreenRecordingManager();
        _directory = Path.Combine(Path.GetTempPath(), "recording-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public async Task TearDown()
    {
        _manager.SignalStart();
        await _manager.DiscardRecordingAsync();
        _recorder.Dispose();
        ScreenRecorderService.NativeRecordingServiceFactory = _oldFactory;
        ScreenRecordingManager.PlatformInitializationTask = _oldInitialization;
        Directory.Delete(_directory, true);
    }

    private RecordingOptions Options() => new()
    {
        OutputPath = Path.Combine(_directory, "output.mp4"),
        LinuxRecordingBackendPreference = LinuxRecordingBackendPreference.Native
    };

    [Test]
    public async Task ManualStart_DoesNotCaptureUntilStart_AndEarlyStopIsNotLost()
    {
        var options = Options();
        options.AutoStart = false;
        Task starting = _manager.StartRecordingAsync(options);
        Assert.That(_manager.IsWaiting, Is.True);
        Assert.That(_manager.IsRecording, Is.True);
        Assert.That(_recorder.Starts, Is.Zero);
        _manager.SignalStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(3));
        _manager.SignalStop();
        await _manager.WaitForStopSignalAsync().WaitAsync(TimeSpan.FromSeconds(3));
        string? path = await _manager.StopRecordingAsync();
        Assert.That(File.Exists(path), Is.True);
        Assert.That(_recorder.Starts, Is.EqualTo(1));
    }

    [Test]
    public async Task AbortWhileWaiting_NeverStartsCapture()
    {
        var options = Options();
        options.AutoStart = false;
        Task starting = _manager.StartRecordingAsync(options);
        await _manager.AbortRecordingAsync();
        Assert.ThrowsAsync<RecordingAbortedException>(async () => await starting);
        Assert.That(_recorder.Starts, Is.Zero);
        Assert.That(_manager.IsRecording, Is.False);
    }

    [Test]
    public async Task FixedDuration_ExcludesPausedTime_AndResumesSameBackend()
    {
        var options = Options();
        options.Duration = 0.35;
        await _manager.StartRecordingAsync(options);
        await _manager.PauseRecordingAsync();
        Task stopping = _manager.WaitForStopSignalAsync();
        await Task.Delay(450);
        Assert.That(stopping.IsCompleted, Is.False);
        await _manager.ResumeRecordingAsync();
        await stopping.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(_recorder.Starts, Is.EqualTo(1));
        Assert.That(_recorder.Pauses, Is.EqualTo(1));
        Assert.That(_recorder.Resumes, Is.EqualTo(1));
        await _manager.StopRecordingAsync();
    }

    [Test]
    public async Task Abort_DoesNotAsk_EvenWithConfirmationOn_AndDiscardsWithoutCompletion()
    {
        // As ShareX's AbortRecording (the Abort hotkey): only the recording controls ask first.
        var options = Options();
        options.AskConfirmationOnAbort = true;
        int completed = 0;
        _manager.RecordingCompleted += (_, _) => completed++;
        await _manager.StartRecordingAsync(options);
        await _manager.AbortRecordingAsync();
        Assert.That(_manager.IsRecording, Is.False);
        await _manager.WaitForStopSignalAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(await _manager.StopRecordingAsync(), Is.Null);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
        Assert.That(completed, Is.Zero);
    }

    [Test]
    public async Task RepeatedStop_FinalizesOnce_AndReturnsSamePath()
    {
        await _manager.StartRecordingAsync(Options());
        string?[] results = await Task.WhenAll(_manager.StopRecordingAsync(), _manager.StopRecordingAsync());
        Assert.That(results[0], Is.EqualTo(results[1]));
        Assert.That(File.Exists(results[0]), Is.True);
        Assert.That(_recorder.Stops, Is.EqualTo(1));
    }

    [Test]
    public async Task BackendFailure_WakesWorkflow_AndDoesNotPublishPartialFile()
    {
        await _manager.StartRecordingAsync(Options());
        _recorder.Fail();
        await _manager.WaitForStopSignalAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var failure = Assert.ThrowsAsync<RecordingFailedException>(async () => await _manager.StopRecordingAsync());
        Assert.That(failure!.Message, Is.EqualTo("encoder failed"), "the notification shows the recorder's own reason");
        Assert.That(File.Exists(Path.Combine(_directory, "output.mp4")), Is.False);
    }

    [Test]
    public async Task AbortDuringInitialization_CancelsBeforeCapture_AndLeavesNoOutput()
    {
        _recorder.Initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task starting = _manager.StartRecordingAsync(Options());
        while (_recorder.Starts == 0) await Task.Delay(10);
        await _manager.AbortRecordingAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.ThrowsAsync<RecordingAbortedException>(async () => await starting);
        Assert.That(_manager.IsRecording, Is.False);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public async Task FailedSecondStage_RetainsCapturedSource_AndDoesNotPublishOutput()
    {
        var options = Options();
        options.TwoPassEncoding = true;
        options.FFmpegOverridePath = Path.Combine(_directory, "missing-ffmpeg");
        await _manager.StartRecordingAsync(options);
        var failure = Assert.ThrowsAsync<RecordingFailedException>(async () => await _manager.StopRecordingAsync());
        string kept = Path.Combine(_directory, "output.part000.mp4");
        Assert.That(failure!.Message, Does.Contain("The recording was kept: " + kept));
        Assert.That(File.Exists(options.OutputPath), Is.False);
        Assert.That(File.ReadAllText(kept), Is.EqualTo("test recording"));
        // A second stop (the toolbar, the tray) reports the same failure instead of encoding again.
        Assert.That(Assert.ThrowsAsync<RecordingFailedException>(async () => await _manager.StopRecordingAsync()), Is.SameAs(failure));
        Assert.That(_recorder.Stops, Is.EqualTo(1));
    }

    [Test]
    public async Task RealFfmpeg_SegmentedPauseResume_FinalizesAllTakes()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/ffmpeg")) Assert.Ignore("Requires Linux FFmpeg.");
        ScreenRecorderService.NativeRecordingServiceFactory = () => new FFmpegRecordingService();
        var options = Options();
        options.FFmpegOverridePath = "/usr/bin/ffmpeg";
        options.FFmpegOptions = new()
        {
            UseCustomCommands = true,
            CustomCommands = "-v error -re -f lavfi -i testsrc2=size=64x48:rate=10 -c:v libx264 -preset ultrafast -y \"$output$\""
        };
        await _manager.StartRecordingAsync(options);
        await Task.Delay(650);
        await _manager.PauseRecordingAsync();
        Task stop = _manager.WaitForStopSignalAsync();
        Assert.That(stop.IsCompleted, Is.False, "pausing must not end the workflow");
        await _manager.ResumeRecordingAsync();
        await Task.Delay(650);
        string? output = await _manager.StopRecordingAsync();
        string duration = await RegionCapture.RecordingEncodingTests.Run("ffprobe",
            $"-v error -show_entries format=duration -of csv=p=0 {RecordingEncoding.Quote(output!)}");
        Assert.That(double.Parse(duration.Trim(), System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(0.8));
        Assert.That(Directory.GetFiles(_directory, "*.part*"), Is.Empty);
    }

    [Test]
    public async Task SessionBackend_ChoosesItsSourceBeforeTheControls_AndRestartKeepsIt()
    {
        var session = new FakeSessionRecorder();
        ScreenRecorderService.NativeRecordingServiceFactory = () => { session.Created++; return session; };
        var events = new List<string>();
        _manager.RecordingPreparing += (_, _) => events.Add($"controls (prepared {session.Prepares})");
        _manager.StatusChanged += (_, e) => { if (e.Status == RecordingStatus.Waiting) events.Add("waiting"); };
        var options = Options();
        options.AutoStart = false;
        Task starting = _manager.StartRecordingAsync(options);
        while (!_manager.IsWaiting) await Task.Delay(10);
        Assert.That(events, Is.EqualTo(new[] { "controls (prepared 1)", "waiting" }), "the portal picker opens before the controls and the manual start");
        Assert.That(session.Starts, Is.Zero);
        _manager.SignalStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(3));

        _manager.RequestRestart();
        Assert.That(_manager.ConsumeRestartRequest(), Is.True);
        Task restarting = _manager.RestartRecordingAsync(options);
        while (!_manager.IsWaiting) await Task.Delay(10);
        Assert.That(session.Discards, Is.EqualTo(1));
        Assert.That(session.Disposes, Is.Zero, "the portal session stays open");
        Assert.That(_manager.IsRecording, Is.True, "the session continues between takes");
        _manager.SignalStart();
        await restarting.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(session.Prepares, Is.EqualTo(1), "Restart does not open the picker again");
        Assert.That(session.Created, Is.EqualTo(1));
        Assert.That(session.Starts, Is.EqualTo(2));
        Assert.That(await _manager.StopRecordingAsync(), Is.Not.Null);
    }

    [Test]
    public async Task SessionBackend_AbortWhileWaiting_ClosesTheSession()
    {
        var session = new FakeSessionRecorder();
        ScreenRecorderService.NativeRecordingServiceFactory = () => session;
        var options = Options();
        options.AutoStart = false;
        Task starting = _manager.StartRecordingAsync(options);
        while (!_manager.IsWaiting) await Task.Delay(10);
        await _manager.AbortRecordingAsync();
        Assert.ThrowsAsync<RecordingAbortedException>(async () => await starting);
        Assert.That(session.Disposes, Is.EqualTo(1));
        Assert.That(session.Starts, Is.Zero);
    }

    [Test]
    public void SessionBackend_CancelledPicker_EndsQuietly()
    {
        var session = new FakeSessionRecorder { CancelPrepare = true };
        ScreenRecorderService.NativeRecordingServiceFactory = () => session;
        int controls = 0;
        _manager.RecordingPreparing += (_, _) => controls++;
        var cancelled = Assert.CatchAsync<OperationCanceledException>(async () => await _manager.StartRecordingAsync(Options()));
        Assert.That(cancelled, Is.Not.TypeOf<RecordingAbortedException>(), "like a cancelled region selection: no abort sound");
        Assert.That(controls, Is.Zero);
        Assert.That(_manager.IsRecording, Is.False);
        Assert.That(session.Disposes, Is.EqualTo(1));
    }

    private sealed class FakeSessionRecorder : IRecordingService, ISessionRecordingService
    {
        private RecordingOptions? _options;
        public int Created, Prepares, Starts, Discards, Disposes;
        public bool CancelPrepare;
        public event EventHandler<RecordingErrorEventArgs>? ErrorOccurred { add { } remove { } }
        public event EventHandler<RecordingStatusEventArgs>? StatusChanged;
        public RecordingRuntimeCapabilities GetCapabilities(RecordingOptions options) => new(RecordingPauseBehavior.NativePauseResume, true);
        public Task PrepareRecordingAsync(RecordingOptions options)
        {
            Prepares++;
            return CancelPrepare ? Task.FromException(new OperationCanceledException()) : Task.CompletedTask;
        }
        public Task StartRecordingAsync(RecordingOptions options)
        {
            _options = options;
            Starts++;
            StatusChanged?.Invoke(this, new(RecordingStatus.Recording, TimeSpan.Zero));
            return Task.CompletedTask;
        }
        public Task StopRecordingAsync()
        {
            File.WriteAllText(_options!.OutputPath!, "test recording");
            return Task.CompletedTask;
        }
        public Task DiscardTakeAsync() { Discards++; return Task.CompletedTask; }
        public void Dispose() => Disposes++;
    }

    private sealed class FakeRecorder : IRecordingService, IPausableRecordingService
    {
        private RecordingOptions? _options;
        public int Starts, Stops, Pauses, Resumes;
        public TaskCompletionSource<bool>? Initialization;
        public void CancelInitialization() => Initialization?.TrySetResult(false);
        public event EventHandler<RecordingErrorEventArgs>? ErrorOccurred;
        public event EventHandler<RecordingStatusEventArgs>? StatusChanged;
        public RecordingRuntimeCapabilities GetCapabilities(RecordingOptions options) => new(RecordingPauseBehavior.NativePauseResume, true);
        public async Task StartRecordingAsync(RecordingOptions options)
        {
            _options = options;
            Starts++;
            if (Initialization != null && !await Initialization.Task) throw new OperationCanceledException();
            StatusChanged?.Invoke(this, new(RecordingStatus.Recording, TimeSpan.Zero));
        }
        public Task StopRecordingAsync()
        {
            Stops++;
            File.WriteAllText(_options!.OutputPath!, "test recording");
            StatusChanged?.Invoke(this, new(RecordingStatus.Idle, TimeSpan.Zero));
            return Task.CompletedTask;
        }
        public Task PauseRecordingAsync() { Pauses++; return Task.CompletedTask; }
        public Task ResumeRecordingAsync() { Resumes++; return Task.CompletedTask; }
        public void Fail() => ErrorOccurred?.Invoke(this, new(new IOException("encoder failed"), true));
        public void Dispose() { }
    }
}
