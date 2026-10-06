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
using XerahS.Platform.Linux.Recording;
using XerahS.RegionCapture.ScreenRecording;

namespace XerahS.Tests.Platform;

[TestFixture]
public sealed class WaylandRecordingPipelineTests
{
    [Test]
    public async Task GStreamerBridge_VideoEndFinalizesLiveAudio_AndSignalsCompletion()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/ffmpeg") || !File.Exists("/usr/bin/gst-launch-1.0"))
            Assert.Ignore("Requires local FFmpeg and GStreamer.");
        string directory = Path.Combine(Path.GetTempPath(), "recording-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var service = new WaylandPortalRecordingService();
        try
        {
            string output = Path.Combine(directory, "video.mp4");
            var options = new RecordingOptions
            {
                OutputPath = output, FFmpegOptions = new(),
                Settings = new() { FPS = 10, CaptureSystemAudio = true }
            };
            string args = RecordingEncoding.LinuxArguments(options,
                "-f rawvideo -pix_fmt yuv420p -video_size 64x48 -framerate 10 -i pipe:0")
                .Replace("-f pulse -i @DEFAULT_MONITOR@", "-f lavfi -i sine=frequency=440");
            string capture = "-q -e videotestsrc is-live=true num-buffers=8 ! video/x-raw,format=I420,width=64,height=48,framerate=10/1 ! fdsink fd=1";
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            service.StatusChanged += (_, e) => { if (e.Status == RecordingStatus.Idle) completed.TrySetResult(); };
            service.ErrorOccurred += (_, e) => completed.TrySetException(e.Error);
            var run = typeof(WaylandPortalRecordingService).GetMethod("RunFFmpegEncodingBridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await Task.Run(() => run.Invoke(service, new object[] { capture, "/usr/bin/ffmpeg", args, output })).WaitAsync(TimeSpan.FromSeconds(10));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            string streams = await RegionCapture.RecordingEncodingTests.Run("ffprobe",
                $"-v error -show_entries stream=codec_type -of csv=p=0 {RecordingEncoding.Quote(output)}");
            Assert.That(streams, Does.Contain("video").And.Contain("audio"));
        }
        finally { service.Dispose(); Directory.Delete(directory, true); }
    }
}
