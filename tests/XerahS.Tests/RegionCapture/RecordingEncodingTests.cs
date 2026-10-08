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

using System.Diagnostics;
using System.Globalization;
using NUnit.Framework;
using XerahS.RegionCapture;
using XerahS.RegionCapture.ScreenRecording;

namespace XerahS.Tests.RegionCapture;

[TestFixture]
public sealed class RecordingEncodingTests
{
    [Test]
    public void CustomCommand_ExpandsShareXTokensInvariantly_AndRetainsQuotedPaths()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            var options = new RecordingOptions
            {
                OutputPath = "/tmp/recording with spaces.mp4", Duration = 1.5,
                Region = new(12, 34, 640, 480), Settings = new() { FPS = 25, ShowCursor = false },
                FFmpegOptions = new() { CustomCommands = "-r $FPS$ -size $area_width$x$area_height$ -x $area_x$ -y $area_y$ -cursor $cursor$ -t $duration$ \"$output$\"" }
            };
            Assert.That(RecordingEncoding.ExpandCustomCommand(options), Is.EqualTo("-r 25 -size 640x480 -x 12 -y 34 -cursor 0 -t 1.5 \"/tmp/recording with spaces.mp4\""));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [TestCase(FFmpegVideoCodec.libx264, "-preset slow", "-crf 19")]
    [TestCase(FFmpegVideoCodec.libx265, "-preset slow", "-crf 19")]
    [TestCase(FFmpegVideoCodec.libvpx, "-deadline realtime", "-b:v 2345k")]
    [TestCase(FFmpegVideoCodec.libvpx_vp9, "-c:v libvpx-vp9", "-b:v 2345k")]
    [TestCase(FFmpegVideoCodec.libxvid, "-c:v libxvid", "-qscale:v 5")]
    [TestCase(FFmpegVideoCodec.h264_nvenc, "-preset p6", "-b:v 4567k")]
    [TestCase(FFmpegVideoCodec.hevc_nvenc, "-tune hq", "-tag:v hvc1")]
    [TestCase(FFmpegVideoCodec.h264_amf, "-quality quality", "-b:v 5678k")]
    [TestCase(FFmpegVideoCodec.hevc_amf, "-usage transcoding", "-tag:v hvc1")]
    [TestCase(FFmpegVideoCodec.h264_qsv, "-preset slow", "-b:v 6789k")]
    [TestCase(FFmpegVideoCodec.hevc_qsv, "-preset slow", "-tag:v hvc1")]
    public void RequestedEncoderSettingsReachArguments(FFmpegVideoCodec codec, string first, string second)
    {
        var options = new FFmpegOptions
        {
            VideoCodec = codec, x264_Preset = FFmpegPreset.slow, x264_CRF = 19,
            VPx_Bitrate = 2345, XviD_QScale = 5, NVENC_Preset = FFmpegNVENCPreset.p6,
            NVENC_Tune = FFmpegNVENCTune.hq, NVENC_Bitrate = 4567,
            AMF_Usage = FFmpegAMFUsage.transcoding, AMF_Quality = FFmpegAMFQuality.quality, AMF_Bitrate = 5678,
            QSV_Preset = FFmpegQSVPreset.slow, QSV_Bitrate = 6789
        };
        string args = RecordingEncoding.VideoArguments(options, false, true);
        Assert.That(args, Does.Contain(first).And.Contain(second));
    }

    [Test]
    public void BitrateModeAndLosslessFirstStage_DoNotUseCrf()
    {
        var options = new FFmpegOptions { x264_Use_Bitrate = true, x264_Bitrate = 4321 };
        Assert.That(RecordingEncoding.VideoArguments(options, false, true), Does.Contain("-b:v 4321k").And.Not.Contain("-crf"));
        Assert.That(RecordingEncoding.VideoArguments(options, true, true), Does.Contain("-qp 0").And.Not.Contain("-b:v"));
    }

    [Test]
    public void AudioOnly_HasNoScreenInput_AndMixesBothRequestedSources()
    {
        var options = new RecordingOptions
        {
            OutputPath = "/tmp/audio.opus", FFmpegOptions = new() { VideoSource = "", AudioSource = "system", AudioCodec = FFmpegAudioCodec.libopus, Opus_Bitrate = 96 },
            Settings = new() { CaptureSystemAudio = true, CaptureMicrophone = true }
        };
        string command = RecordingEncoding.LinuxArguments(options);
        Assert.That(command, Does.Not.Contain("x11grab").And.Not.Contain("pipewire").And.Not.Contain("-c:v"));
        Assert.That(command, Does.Contain("@DEFAULT_MONITOR@").And.Contain("amix=inputs=2").And.Contain("-b:a 96k"));
    }

    [TestCase(FFmpegVideoCodec.libx264, "mp4", "h264")]
    [TestCase(FFmpegVideoCodec.libvpx, "webm", "vp8")]
    [TestCase(FFmpegVideoCodec.libvpx_vp9, "webm", "vp9")]
    [TestCase(FFmpegVideoCodec.libxvid, "avi", "mpeg4")]
    [TestCase(FFmpegVideoCodec.apng, "apng", "apng")]
    [TestCase(FFmpegVideoCodec.gif, "gif", "gif")]
    [TestCase(FFmpegVideoCodec.libwebp, "webp", "webp")]
    public async Task RealFfmpeg_EncodesRequestedVideoFormat(FFmpegVideoCodec codec, string extension, string expectedCodec)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/ffmpeg")) Assert.Ignore("Requires local FFmpeg.");
        string directory = Path.Combine(Path.GetTempPath(), "recording-encoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.mp4");
            await Run("ffmpeg", $"-v error -f lavfi -i testsrc2=size=64x48:rate=10 -t 0.6 -c:v libx264 -qp 0 -y {RecordingEncoding.Quote(source)}");
            string output = Path.Combine(directory, "o'brien \"recording\\ clip." + extension);
            await RecordingEncoding.EncodeAsync("/usr/bin/ffmpeg", source, new RecordingOptions
            {
                OutputPath = output, Settings = new() { FPS = 10 }, FFmpegOptions = new() { VideoCodec = codec }
            });
            Assert.That(File.Exists(source), Is.True, "source remains available until the session commits its final output");
            if (extension == "webp")
            {
                string bytes = System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(output));
                Assert.That(bytes, Does.StartWith("RIFF").And.Contain("ANIM"));
                Assert.That(bytes.Split("ANMF").Length, Is.GreaterThan(2));
            }
            else
            {
                string probe = await Run("ffprobe", $"-v error -select_streams v:0 -show_entries stream=codec_name -of csv=p=0 {RecordingEncoding.Quote(output)}");
                Assert.That(probe.Trim(), Is.EqualTo(expectedCodec));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestCase(FFmpegAudioCodec.aac, "m4a", "aac")]
    [TestCase(FFmpegAudioCodec.libopus, "opus", "opus")]
    [TestCase(FFmpegAudioCodec.libvorbis, "ogg", "vorbis")]
    [TestCase(FFmpegAudioCodec.libmp3lame, "mp3", "mp3")]
    public async Task RealFfmpeg_EncodesAudioOnlyWithRequestedCodec(FFmpegAudioCodec codec, string extension, string expectedCodec)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/ffmpeg")) Assert.Ignore("Requires local FFmpeg.");
        string directory = Path.Combine(Path.GetTempPath(), "recording-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.wav");
            await Run("ffmpeg", $"-v error -f lavfi -i sine=frequency=440:duration=0.3 -y {RecordingEncoding.Quote(source)}");
            string output = Path.Combine(directory, "audio." + extension);
            await RecordingEncoding.EncodeAsync("/usr/bin/ffmpeg", source, new()
            {
                OutputPath = output,
                FFmpegOptions = new()
                {
                    VideoSource = "", AudioSource = "default", AudioCodec = codec,
                    AAC_Bitrate = 96, Opus_Bitrate = 96, Vorbis_QScale = 5, MP3_QScale = 3,
                    UserArgs = "-metadata title=RecordingTest"
                }
            });
            string probe = await Run("ffprobe", $"-v error -show_entries stream=codec_name,codec_type -of csv=p=0 {RecordingEncoding.Quote(output)}");
            Assert.That(probe.Trim(), Is.EqualTo(expectedCodec + ",audio"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void X11WindowRecording_SelectsTheRequestedWindow()
    {
        var options = new RecordingOptions
        {
            Mode = CaptureMode.Window, TargetWindowHandle = new IntPtr(12345),
            FFmpegOptions = new(), OutputPath = "/tmp/window.mp4"
        };
        Assert.That(RecordingEncoding.LinuxArguments(options), Does.Contain("-window_id 12345"));
    }

    [Test]
    public void X11WindowRecording_CropsToAnEvenSize_UnlessTheOutputIsAnimated()
    {
        // As ShareX's EvenRectangleSize: a window can have an odd size, which yuv420p encoders reject.
        var window = new RecordingOptions
        {
            Mode = CaptureMode.Window, TargetWindowHandle = new IntPtr(12345),
            FFmpegOptions = new(), OutputPath = "/tmp/window.mp4"
        };
        Assert.That(RecordingEncoding.LinuxArguments(window), Does.Contain("-vf " + RecordingEncoding.Quote(RecordingEncoding.EvenSizeFilter)));
        window.FFmpegOptions.VideoCodec = FFmpegVideoCodec.gif;
        Assert.That(RecordingEncoding.LinuxArguments(window), Does.Not.Contain("crop="));
        var region = new RecordingOptions
        {
            Mode = CaptureMode.Region, Region = new(10, 20, 640, 480),
            FFmpegOptions = new(), OutputPath = "/tmp/region.mp4"
        };
        Assert.That(RecordingEncoding.LinuxArguments(region), Does.Not.Contain("crop="), "regions are already even-sized");
    }

    [Test]
    public void PipeWireMonitorRecording_CropsToAnEvenSize()
    {
        var options = new RecordingOptions { Mode = CaptureMode.Screen, FFmpegOptions = new(), OutputPath = "/tmp/monitor.mp4" };
        var builder = typeof(XerahS.Platform.Linux.Recording.WaylandPortalRecordingService).GetMethod("BuildFFmpegArguments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        string command = (string)builder.Invoke(null, new object[] { options, (uint)42, options.OutputPath })!;
        Assert.That(command, Does.Contain(RecordingEncoding.EvenSizeFilter));
    }

    [Test]
    public void GifConversion_IgnoresCustomCommandsAndExtraArguments_AsShareX()
    {
        var options = new RecordingOptions
        {
            OutputPath = "/tmp/out.gif",
            FFmpegOptions = new()
            {
                VideoCodec = FFmpegVideoCodec.gif, UseCustomCommands = true, CustomCommands = "-i $input$ custom $output$",
                UserArgs = "-threads 2", GIFDither = FFmpegPaletteUseDither.floyd_steinberg, GIFBayerScale = 3
            }
        };
        string gif = RecordingEncoding.EncodeArguments("/tmp/in.mp4", options);
        Assert.That(gif, Does.Contain("palettegen").And.Contain("paletteuse=dither=floyd_steinberg"));
        Assert.That(gif, Does.Not.Contain("custom").And.Not.Contain("-threads 2").And.Not.Contain("bayer_scale"));
        options.FFmpegOptions.GIFDither = FFmpegPaletteUseDither.bayer;
        Assert.That(RecordingEncoding.EncodeArguments("/tmp/in.mp4", options), Does.Contain("dither=bayer:bayer_scale=3"));
        options.FFmpegOptions.VideoCodec = FFmpegVideoCodec.libx264;
        Assert.That(RecordingEncoding.EncodeArguments("/tmp/in.mp4", options), Is.EqualTo("-i /tmp/in.mp4 custom /tmp/out.gif"),
            "other formats use the custom command, as ShareX's second pass does");
    }

    [Test]
    public void FailureSummary_KeepsTheFirstLineAndTheEndOfTheOutput()
    {
        Assert.That(RecordingEncoding.Summarize("FFmpeg failed.\nversion\n\nopening\n[libx264] width not divisible by 2\nConversion failed!\n"),
            Is.EqualTo("FFmpeg failed. opening [libx264] width not divisible by 2 Conversion failed!"));
        Assert.That(RecordingEncoding.Summarize("encoder failed"), Is.EqualTo("encoder failed"));
    }

    [Test]
    public void FailureSummary_ShowsFFmpegsErrorLinesInsteadOfItsGeneralLastLines()
    {
        // FFmpeg's output when the AMF encoder is chosen without an AMD GPU (X11 test, 2026-10-08).
        const string output = """
            FFmpeg process failed.
            Output: Input #0, x11grab, from ':99+100,100':
            Stream mapping:
            Press [q] to stop, [?] for help
            [AMF @ 0x7faab033d8c0] DLL libamfrt64.so.1 failed to open
            [h264_amf @ 0x5636613ec040] Failed to create  hardware device context (AMF) : Unknown error occurred
            [vost#0:0/h264_amf @ 0x5636613eb580] [enc:h264_amf @ 0x5636613ebc00] Error while opening encoder - maybe incorrect parameters such as bit_rate, rate, width or height.
            [vf#0:0 @ 0x5636613ec6c0] Error sending frames to consumers: Unknown error occurred
            [vf#0:0 @ 0x5636613ec6c0] Task finished with error code: -1313558101 (Unknown error occurred)
            [vf#0:0 @ 0x5636613ec6c0] Terminating thread with return code -1313558101 (Unknown error occurred)
            [out#0/mp4 @ 0x5636613eb080] Nothing was written into output file, because at least one of its streams received no packets.
            frame=    0 fps=0.0 q=0.0 Lsize=       0KiB time=N/A bitrate=N/A speed=N/A elapsed=0:00:00.03
            Conversion failed!
            """;

        Assert.That(RecordingEncoding.Summarize(output), Is.EqualTo(
            "FFmpeg process failed. [AMF] DLL libamfrt64.so.1 failed to open " +
            "[h264_amf] Failed to create  hardware device context (AMF) : Unknown error occurred " +
            "[vost#0:0/h264_amf] [enc:h264_amf] Error while opening encoder - maybe incorrect parameters such as bit_rate, rate, width or height."));
    }

    [Test]
    public async Task RealFfmpeg_ReportsSecondStageProgress()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/ffmpeg")) Assert.Ignore("Requires local FFmpeg.");
        string directory = Path.Combine(Path.GetTempPath(), "recording-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.mp4");
            await Run("ffmpeg", $"-v error -f lavfi -i testsrc2=size=320x240:rate=30 -t 3 -c:v libx264 -qp 0 -y {RecordingEncoding.Quote(source)}");
            var progress = new System.Collections.Concurrent.ConcurrentQueue<int>();
            await RecordingEncoding.EncodeAsync("/usr/bin/ffmpeg", source, new RecordingOptions
            {
                OutputPath = Path.Combine(directory, "output.webm"), Settings = new() { FPS = 30 },
                FFmpegOptions = new() { VideoCodec = FFmpegVideoCodec.libvpx_vp9 }
            }, progress.Enqueue);
            Assert.That(progress, Is.Not.Empty);
            Assert.That(progress, Has.All.InRange(0, 100));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void PipeWireRecording_PreservesRegionCropWithDetailedOptions()
    {
        var options = new RecordingOptions
        {
            Mode = CaptureMode.Region, Region = new(12, 24, 640, 480),
            FFmpegOptions = new(), OutputPath = "/tmp/region.mp4"
        };
        var builder = typeof(XerahS.Platform.Linux.Recording.WaylandPortalRecordingService).GetMethod("BuildFFmpegArguments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        string command = (string)builder.Invoke(null, new object[] { options, (uint)42, options.OutputPath })!;
        Assert.That(command, Does.Contain("crop=640:480:12:24").And.Contain("-f pipewire"));
    }

    [Test]
    public void PipeWireWithAudio_EndsLiveAudioWhenVideoEnds()
    {
        var options = new RecordingOptions
        {
            OutputPath = "/tmp/recording.mp4", FFmpegOptions = new(),
            Settings = new() { CaptureSystemAudio = true }
        };
        Assert.That(RecordingEncoding.LinuxArguments(options, "-f rawvideo -i pipe:0"), Does.Contain("-shortest"));
    }

    internal static async Task<string> Run(string executable, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false
        })!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, await errors);
        return await output;
    }
}
