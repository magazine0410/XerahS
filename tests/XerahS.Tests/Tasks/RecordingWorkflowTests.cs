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

using System.Reflection;
using NUnit.Framework;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Core.Tasks.Processors;
using XerahS.RegionCapture.ScreenRecording;
using XerahS.Services.Abstractions;

namespace XerahS.Tests.Tasks;

[TestFixture, NonParallelizable]
public sealed class RecordingWorkflowTests
{
    [Test]
    public void RecordingOptions_SnapshotAllSettings_AndGifDoesNotMutateWorkflow()
    {
        var task = new TaskSettings { Job = WorkflowType.ScreenRecorderGIF };
        task.CaptureSettings.ScreenRecordAutoStart = false;
        task.CaptureSettings.ScreenRecordFixedDuration = true;
        task.CaptureSettings.ScreenRecordDuration = 1.5f;
        task.CaptureSettings.ScreenRecordShowTimer = false;
        task.CaptureSettings.ScreenRecordShowButtonLabels = false;
        task.CaptureSettings.ScreenRecordAskConfirmationOnAbort = true;
        task.CaptureSettings.ScreenRecordMouseHighlighter = true;
        task.CaptureSettings.GIFFPS = 12;
        task.CaptureSettings.FFmpegOptions.x264_CRF = 17;
        task.CaptureSettings.FFmpegOptions.UserArgs = "-threads 2";
        var options = WorkerTask.CreateRecordingOptions(task, CaptureMode.Region);
        Assert.Multiple(() =>
        {
            Assert.That(options.AutoStart, Is.False);
            Assert.That(options.Duration, Is.EqualTo(1.5));
            Assert.That(options.ShowTimer, Is.False);
            Assert.That(options.ShowButtonLabels, Is.False);
            Assert.That(options.AskConfirmationOnAbort, Is.True);
            Assert.That(options.HighlightMouse, Is.True);
            Assert.That(options.TwoPassEncoding, Is.True);
            Assert.That(options.Settings!.FPS, Is.EqualTo(12));
            Assert.That(options.FFmpegOptions!.VideoCodec, Is.EqualTo(XerahS.RegionCapture.FFmpegVideoCodec.gif));
            Assert.That(options.FFmpegOptions.x264_CRF, Is.EqualTo(17));
            Assert.That(options.FFmpegOptions.UserArgs, Is.EqualTo("-threads 2"));
            Assert.That(task.CaptureSettings.FFmpegOptions.VideoCodec, Is.EqualTo(FFmpegVideoCodec.libx264));
        });
        options.Settings!.ForceFFmpeg = true;
        Assert.That(task.CaptureSettings.ScreenRecordingSettings.ForceFFmpeg, Is.False);
    }

    [Test]
    public void AudioOnlyRecording_IsNotMarkedAsVideoInHistory()
    {
        var settings = new TaskSettings();
        settings.CaptureSettings.FFmpegOptions.VideoSource = "";
        settings.CaptureSettings.FFmpegOptions.AudioSource = "default";
        var history = WorkerTask.CreateRecordingHistoryItem(new TaskInfo(settings), "/tmp/audio.opus");
        Assert.That(history.IsVideo, Is.False);
        Assert.That(history.Type, Is.EqualTo("File"));
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task RecordingUpload_RequiresUploadImageToHost_EvenWithCopyUrlEnabled(bool upload, int expectedPrompts)
    {
        string directory = Path.Combine(Path.GetTempPath(), "recording-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var folder = typeof(PathsManager).GetField("_personalFolder", BindingFlags.Static | BindingFlags.NonPublic)!;
        var folderOverride = typeof(PathsManager).GetField("_personalFolderOverrideSet", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? oldFolder = folder.GetValue(null), oldOverride = folderOverride.GetValue(null);
        IScreenRecordingManager previousManager = WorkerTask.RecordingManagerService;
        var previousPrompt = UploadJobProcessor.ShowBeforeUploadCallback;
        int prompts = 0;
        try
        {
            PathsManager.PersonalFolder = directory;
            WorkerTask.RecordingManagerService = new CompletedRecording();
            // Prevent network access even if this regression causes an unwanted upload.
            UploadJobProcessor.ShowBeforeUploadCallback = (_, _) => { prompts++; return Task.FromResult(false); };
            var flags = AfterCaptureTasks.ShowBeforeUploadWindow | (upload ? AfterCaptureTasks.UploadImageToHost : AfterCaptureTasks.None);
            var settings = new TaskSettings
            {
                Job = WorkflowType.ScreenRecorder, AfterCaptureJob = flags,
                AfterUploadJob = AfterUploadTasks.CopyURLToClipboard,
                OverrideScreenshotsFolder = true, ScreenshotsFolder = directory,
                GeneralSettings = new() { PlaySoundAfterAction = false }
            };
            using var worker = WorkerTask.Create(settings);
            await worker.HandleStartRecordingAsync(CaptureMode.Screen);
            Assert.That(prompts, Is.EqualTo(expectedPrompts));
            Assert.That(settings.AfterCaptureJob, Is.EqualTo(flags));
            Assert.That(File.Exists(worker.Info.FilePath), Is.True);
        }
        finally
        {
            UploadJobProcessor.ShowBeforeUploadCallback = previousPrompt;
            WorkerTask.RecordingManagerService = previousManager;
            folder.SetValue(null, oldFolder);
            folderOverride.SetValue(null, oldOverride);
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task StopDuringTheRecording_KeepsTheFinishedRecordingsUploadAndHistory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "recording-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var folder = typeof(PathsManager).GetField("_personalFolder", BindingFlags.Static | BindingFlags.NonPublic)!;
        var folderOverride = typeof(PathsManager).GetField("_personalFolderOverrideSet", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? oldFolder = folder.GetValue(null), oldOverride = folderOverride.GetValue(null);
        IScreenRecordingManager previousManager = WorkerTask.RecordingManagerService;
        var previousPrompt = UploadJobProcessor.ShowBeforeUploadCallback;
        int prompts = 0;
        try
        {
            PathsManager.PersonalFolder = directory;
            var recording = new StoppableRecording();
            WorkerTask.RecordingManagerService = recording;
            // Prevent network access: the before-upload window declines the upload.
            UploadJobProcessor.ShowBeforeUploadCallback = (_, _) => { prompts++; return Task.FromResult(false); };
            var settings = new TaskSettings
            {
                Job = WorkflowType.ScreenRecorder,
                AfterCaptureJob = AfterCaptureTasks.ShowBeforeUploadWindow | AfterCaptureTasks.UploadImageToHost,
                OverrideScreenshotsFolder = true, ScreenshotsFolder = directory,
                GeneralSettings = new() { PlaySoundAfterAction = false }
            };
            using var worker = WorkerTask.Create(settings);
            Task recordingTask = worker.HandleStartRecordingAsync(CaptureMode.Screen);
            await recording.Started.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(worker.IsRecordingInProgress, Is.True);

            // What "Stop all uploads" and a task's "Stop upload" do while the recording runs.
            worker.Stop();
            recording.Stop();
            await recordingTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(worker.IsRecordingInProgress, Is.False);
            Assert.That(prompts, Is.EqualTo(1), "The finished recording must still reach its upload.");
            Assert.That(worker.Info.HistoryItemId, Is.GreaterThan(0), "The finished recording must still be added to the history.");
        }
        finally
        {
            UploadJobProcessor.ShowBeforeUploadCallback = previousPrompt;
            WorkerTask.RecordingManagerService = previousManager;
            folder.SetValue(null, oldFolder);
            folderOverride.SetValue(null, oldOverride);
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A recording that runs until <see cref="Stop"/> is called.</summary>
    private sealed class StoppableRecording : IScreenRecordingManager
    {
        private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;
        public bool IsRecording => !_stop.Task.IsCompleted;
        public bool IsPaused => false;
        public bool IsUsingFallback => false;
        public string? PlannedOutputPath { get; private set; }
        public void Stop() => _stop.TrySetResult();
        public void SignalStop() => Stop();
        public Task WaitForStopSignalAsync() => _stop.Task;
        public Task StartRecordingAsync(object options)
        {
            PlannedOutputPath = ((RecordingOptions)options).OutputPath;
            File.WriteAllText(PlannedOutputPath!, "test recording");
            _started.TrySetResult();
            return Task.CompletedTask;
        }
        public Task<string?> StopRecordingAsync() => Task.FromResult(PlannedOutputPath);
        public Task AbortRecordingAsync() => Task.CompletedTask;
        public Task TogglePauseResumeAsync() => Task.CompletedTask;
    }

    private sealed class CompletedRecording : IScreenRecordingManager
    {
        public bool IsRecording => true;
        public bool IsPaused => false;
        public bool IsUsingFallback => false;
        public string? PlannedOutputPath { get; private set; }
        public void SignalStop() { }
        public Task WaitForStopSignalAsync() => Task.CompletedTask;
        public Task StartRecordingAsync(object options)
        {
            PlannedOutputPath = ((RecordingOptions)options).OutputPath;
            File.WriteAllText(PlannedOutputPath!, "test recording");
            return Task.CompletedTask;
        }
        public Task<string?> StopRecordingAsync() => Task.FromResult(PlannedOutputPath);
        public Task AbortRecordingAsync() => Task.CompletedTask;
        public Task TogglePauseResumeAsync() => Task.CompletedTask;
    }
}
