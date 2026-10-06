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
using XerahS.Core;
using XerahS.Core.Hotkeys;
using XerahS.RegionCapture.ScreenRecording;
using XerahS.Tests.Xip0052;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.Tasks;

[TestFixture, NonParallelizable]
public sealed class RecordingSettingsTests
{
    [Test]
    public void RecordingPage_PreservesAdvancedEncodingOptionsUntilTheUserEditsThem()
    {
        var previous = SettingsManager.WorkflowsConfig;
        var workflow = new WorkflowSettings(WorkflowType.ScreenRecorder, new XerahS.Platform.Abstractions.HotkeyInfo());
        workflow.TaskSettings.UseDefaultCaptureSettings = false;
        var capture = workflow.TaskSettings.CaptureSettings;
        capture.FFmpegOptions.VideoCodec = FFmpegVideoCodec.libxvid;
        capture.FFmpegOptions.XviD_QScale = 3;
        capture.FFmpegOptions.x264_Use_Bitrate = false;
        capture.FFmpegOptions.VideoSource = "";
        capture.FFmpegOptions.AudioSource = "default";
        try
        {
            SettingsManager.WorkflowsConfig = new WorkflowsConfig();
            SettingsManager.WorkflowsConfig.Hotkeys.Add(workflow);
            using var viewModel = new RecordingViewModel(new FakeScreenRecordingCoordinator());
            typeof(RecordingViewModel).GetMethod("SyncSettingsToWorkflow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(viewModel, null);
            Assert.Multiple(() =>
            {
                Assert.That(capture.FFmpegOptions.VideoCodec, Is.EqualTo(FFmpegVideoCodec.libxvid));
                Assert.That(capture.FFmpegOptions.XviD_QScale, Is.EqualTo(3));
                Assert.That(capture.FFmpegOptions.x264_Use_Bitrate, Is.False);
                Assert.That(capture.FFmpegOptions.VideoSource, Is.Empty);
                Assert.That(capture.FFmpegOptions.AudioSource, Is.EqualTo("default"));
            });
        }
        finally { SettingsManager.WorkflowsConfig = previous; }
    }

    [Test]
    public void SettingsSavedBeforeShareXRecordingOptions_KeepTheirAudioFrameRateAndCursor()
    {
        // The recording page used to write only ScreenRecordingSettings, which recordings then read.
        const string saved = """
            {
              "ScreenRecordFPS": 30,
              "ScreenRecordShowCursor": true,
              "FFmpegOptions": { "AudioSource": "" },
              "ScreenRecordingSettings": { "FPS": 60, "ShowCursor": false, "CaptureSystemAudio": true }
            }
            """;
        var capture = Newtonsoft.Json.JsonConvert.DeserializeObject<TaskSettingsCapture>(saved)!;
        Assert.Multiple(() =>
        {
            Assert.That(capture.ScreenRecordFPS, Is.EqualTo(60));
            Assert.That(capture.ScreenRecordShowCursor, Is.False);
            Assert.That(capture.FFmpegOptions.AudioSource, Is.EqualTo(OperatingSystem.IsWindows() ? "virtual-audio-capturer" : "system"));
        });
        var options = XerahS.Core.Tasks.WorkerTask.CreateRecordingOptions(new TaskSettings { CaptureSettings = capture }, CaptureMode.Screen);
        Assert.That(options.Settings!.CaptureSystemAudio, Is.True, "the recording keeps its audio");
        Assert.That(options.Settings.FPS, Is.EqualTo(60));

        // Saving and loading again changes nothing, and a microphone-only setting maps to the default microphone.
        var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<TaskSettingsCapture>(Newtonsoft.Json.JsonConvert.SerializeObject(capture))!;
        Assert.That(reloaded.ScreenRecordFPS, Is.EqualTo(60));
        var microphone = Newtonsoft.Json.JsonConvert.DeserializeObject<TaskSettingsCapture>(
            """{ "FFmpegOptions": { "AudioSource": "" }, "ScreenRecordingSettings": { "CaptureMicrophone": true } }""")!;
        Assert.That(microphone.FFmpegOptions.AudioSource, Is.EqualTo("default"));
    }

    [Test]
    public void ChangingAudioSource_ReplacesStaleNativeAudioFlags()
    {
        var options = new FFmpegOptions { AudioSource = "default" };
        var recording = new ScreenRecordingSettings { CaptureMicrophone = true };
        var viewModel = new FFmpegOptionsViewModel(options, recording);
        viewModel.SelectedAudioDevice = new FFmpegCaptureDevice("system", "System audio");
        Assert.That(recording.CaptureSystemAudio, Is.True);
        Assert.That(recording.CaptureMicrophone, Is.False);
        viewModel.SelectedAudioDevice = FFmpegCaptureDevice.None;
        Assert.That(recording.CaptureSystemAudio || recording.CaptureMicrophone, Is.False);
    }
}
