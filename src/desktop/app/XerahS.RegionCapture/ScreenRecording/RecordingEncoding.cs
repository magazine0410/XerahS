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

using System.Globalization;
using XerahS.Common;
using XerahS.Media;

namespace XerahS.RegionCapture.ScreenRecording;

/// <summary>ShareX encoding options shared by X11, PipeWire and the lossless second stage.</summary>
public static class RecordingEncoding
{
    public static string Quote(string value)
    {
        var quoted = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            quoted.Append(character);
            slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }

    /// <summary>Crops one pixel when needed: like ShareX's EvenRectangleSize, for encoders that require even dimensions.</summary>
    public const string EvenSizeFilter = "crop=trunc(iw/2)*2:trunc(ih/2)*2";

    /// <summary>The first line of a message and the last lines of the tool output after it, for a notification.</summary>
    public static string Summarize(string message, int outputLines = 3)
    {
        string[] lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length <= outputLines + 1) return string.Join(" ", lines);
        return lines[0] + " " + string.Join(" ", lines[^outputLines..]);
    }

    public static string Extension(RecordingOptions options) => options.IsLossless ? "mp4" : options.FFmpegOptions?.Extension ?? "mp4";

    public static string VideoArguments(FFmpegOptions options, bool lossless, bool recording)
    {
        if (lossless) return "-c:v libx264 -preset ultrafast -tune zerolatency -qp 0";
        string codec = options.VideoCodec == FFmpegVideoCodec.libvpx_vp9 ? "libvpx-vp9" : options.VideoCodec == FFmpegVideoCodec.libaom_av1 ? "libaom-av1" : options.VideoCodec.ToString();
        string args = options.VideoCodec switch
        {
            FFmpegVideoCodec.libx264 or FFmpegVideoCodec.libx265 => $"-preset {options.x264_Preset} " +
                (recording ? "-tune zerolatency " : "") +
                (options.x264_Use_Bitrate ? $"-b:v {options.x264_Bitrate}k" : $"-crf {options.x264_CRF}") + " -pix_fmt yuv420p -movflags +faststart",
            FFmpegVideoCodec.libvpx or FFmpegVideoCodec.libvpx_vp9 => (recording ? "-deadline realtime " : "") + $"-b:v {options.VPx_Bitrate}k -pix_fmt yuv420p",
            FFmpegVideoCodec.libaom_av1 => $"-cpu-used 8 -b:v {options.VPx_Bitrate}k -pix_fmt yuv420p" + (recording ? " -usage realtime" : ""),
            FFmpegVideoCodec.libxvid => $"-qscale:v {options.XviD_QScale} -pix_fmt yuv420p",
            FFmpegVideoCodec.h264_nvenc or FFmpegVideoCodec.hevc_nvenc => $"-preset {options.NVENC_Preset} -tune {options.NVENC_Tune} -b:v {options.NVENC_Bitrate}k -movflags +faststart",
            FFmpegVideoCodec.h264_amf or FFmpegVideoCodec.hevc_amf => $"-usage {options.AMF_Usage} -quality {options.AMF_Quality} -b:v {options.AMF_Bitrate}k -pix_fmt yuv420p",
            FFmpegVideoCodec.h264_qsv or FFmpegVideoCodec.hevc_qsv => $"-preset {options.QSV_Preset} -b:v {options.QSV_Bitrate}k",
            FFmpegVideoCodec.libwebp => "-lossless 0 -preset default -loop 0",
            FFmpegVideoCodec.apng => "-f apng -plays 0 -pix_fmt rgb24",
            FFmpegVideoCodec.gif => "-loop 0",
            _ => throw new NotSupportedException($"Unsupported recording codec: {options.VideoCodec}")
        };
        if (codec.StartsWith("hevc", StringComparison.Ordinal) || options.VideoCodec == FFmpegVideoCodec.libx265) args += " -tag:v hvc1";
        return $"-c:v {codec} {args}";
    }

    public static string AudioArguments(FFmpegOptions options) => options.AudioCodec switch
    {
        FFmpegAudioCodec.aac => $"-c:a aac -ac 2 -b:a {options.AAC_Bitrate}k",
        FFmpegAudioCodec.libopus => $"-c:a libopus -b:a {options.Opus_Bitrate}k",
        FFmpegAudioCodec.libvorbis => $"-c:a libvorbis -qscale:a {options.Vorbis_QScale}",
        FFmpegAudioCodec.libmp3lame => $"-c:a libmp3lame -qscale:a {options.MP3_QScale}",
        _ => throw new NotSupportedException($"Unsupported audio codec: {options.AudioCodec}")
    };

    public static string OutputArguments(RecordingOptions options, bool hasAudio, bool recording = true)
    {
        var ffmpeg = options.FFmpegOptions ?? new FFmpegOptions();
        var args = new List<string> { ffmpeg.UserArgs };
        if (!options.AudioOnly)
        {
            args.Add(VideoArguments(ffmpeg, options.IsLossless, recording));
            args.Add($"-fps_mode cfr -r {options.Settings?.FPS ?? 30}");
        }
        if (options.AudioOnly) args.Add("-vn");
        if (hasAudio && (options.AudioOnly || !ffmpeg.IsAnimatedImage)) args.Add(AudioArguments(ffmpeg));
        else args.Add("-an");
        args.Add("-y " + Quote(options.OutputPath!));
        return string.Join(" ", args);
    }

    public static string ExpandCustomCommand(RecordingOptions options, string? input = null)
    {
        var settings = options.Settings ?? new ScreenRecordingSettings();
        string command = options.FFmpegOptions?.CustomCommands ?? "";
        var values = new Dictionary<string, string>
        {
            ["fps"] = settings.FPS.ToString(CultureInfo.InvariantCulture),
            ["area_x"] = options.Region.X.ToString(CultureInfo.InvariantCulture),
            ["area_y"] = options.Region.Y.ToString(CultureInfo.InvariantCulture),
            ["area_width"] = options.Region.Width.ToString(CultureInfo.InvariantCulture),
            ["area_height"] = options.Region.Height.ToString(CultureInfo.InvariantCulture),
            ["cursor"] = settings.ShowCursor ? "1" : "0",
            ["duration"] = options.Duration.ToString("0.0", CultureInfo.InvariantCulture),
            ["output"] = options.OutputPath ?? "",
            ["input"] = input ?? "pipe:0"
        };
        foreach (var pair in values) command = command.Replace("$" + pair.Key + "$", pair.Value, StringComparison.OrdinalIgnoreCase);
        return command;
    }

    public static string LinuxArguments(RecordingOptions options, string? videoInput = null, string? videoFilter = null)
    {
        var settings = options.Settings ?? new ScreenRecordingSettings();
        var ffmpeg = options.FFmpegOptions ?? new FFmpegOptions();
        if (!options.IsLossless && ffmpeg is { UseCustomCommands: true, CustomCommands.Length: > 0 })
            return ExpandCustomCommand(options);
        var inputs = new List<string>();
        if (!options.AudioOnly)
        {
            if (videoInput != null) inputs.Add(videoInput);
            else
            {
                string display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0.0";
                bool window = options.Mode == CaptureMode.Window && options.TargetWindowHandle != IntPtr.Zero;
                string area = !window && options.Region.Width > 0 && options.Region.Height > 0
                    ? $"-video_size {options.Region.Width}x{options.Region.Height} " : "";
                if (area.Length > 0) display += $"+{options.Region.X},{options.Region.Y}";
                string target = window ? $"-window_id {options.TargetWindowHandle.ToInt64()} " : "";
                inputs.Add($"-f x11grab -framerate {settings.FPS} -draw_mouse {(settings.ShowCursor ? 1 : 0)} {target}{area}-i {Quote(display)}");
                // Regions are already even-sized; a window is recorded at its own size, which may be odd.
                if (window && ffmpeg.IsEvenSizeRequired) videoFilter ??= EvenSizeFilter;
            }
        }
        int firstAudio = inputs.Count;
        bool audioAllowed = options.AudioOnly || !ffmpeg.IsAnimatedImage;
        if (audioAllowed && settings.CaptureSystemAudio)
            inputs.Add("-thread_queue_size 1024 -f pulse -i @DEFAULT_MONITOR@");
        if (audioAllowed && settings.CaptureMicrophone)
            inputs.Add("-thread_queue_size 1024 -f pulse -i " + Quote(settings.MicrophoneDeviceId ?? "default"));
        int audioCount = inputs.Count - firstAudio;
        if (options.AudioOnly && audioCount == 0)
            throw new InvalidOperationException("Select an audio source for audio-only recording.");
        var args = new List<string> { "-hide_banner", string.Join(" ", inputs) };
        if (!options.AudioOnly)
        {
            args.Add("-map 0:v:0");
            if (videoFilter != null) args.Add("-vf " + Quote(videoFilter));
        }
        if (audioCount == 1) args.Add($"-map {firstAudio}:a:0");
        else if (audioCount == 2)
            args.Add($"-filter_complex \"[{firstAudio}:a][{firstAudio + 1}:a]amix=inputs=2:duration=longest[audio]\" -map \"[audio]\"");
        // The PipeWire bridge closes video stdin to stop; live PulseAudio must end with it.
        if (videoInput != null && audioCount > 0) args.Add("-shortest");
        args.Add(OutputArguments(options, audioCount > 0));
        return string.Join(" ", args);
    }

    public static async Task EncodeAsync(string ffmpegPath, string input, RecordingOptions options, Action<int>? progress = null)
    {
        await RunAsync(ffmpegPath, EncodeArguments(input, options), options.OutputPath!, progress);
    }

    /// <summary>The second stage of two-pass encoding, from the lossless capture to the requested output.</summary>
    public static string EncodeArguments(string input, RecordingOptions options)
    {
        var output = options.Clone();
        output.IsLossless = false;
        if (output.FFmpegOptions is { VideoCodec: FFmpegVideoCodec.gif } gif)
        {
            // As ShareX's FFmpegEncodeAsGIF: a palette pass that ignores custom commands and extra arguments.
            string scale = gif.GIFMaxWidth > 0 ? $"scale='min({gif.GIFMaxWidth},iw)':-1:flags=lanczos," : "";
            string filter = $"{scale}split[a][b];[a]palettegen=stats_mode={gif.GIFStatsMode}[p];[b][p]paletteuse=dither={gif.GIFDither}";
            if (gif.GIFDither == FFmpegPaletteUseDither.bayer) filter += $":bayer_scale={gif.GIFBayerScale}";
            if (gif.GIFStatsMode == FFmpegPaletteGenStatsMode.single) filter += ":new=1";
            return $"-i {Quote(input)} -lavfi {Quote(filter)} -y {Quote(output.OutputPath!)}";
        }
        if (output.FFmpegOptions is { UseCustomCommands: true, CustomCommands.Length: > 0 })
            return ExpandCustomCommand(output, input);
        return $"-i {Quote(input)} " + OutputArguments(output, true, recording: false);
    }

    public static async Task ConcatenateAsync(string ffmpegPath, IReadOnlyList<string> segments, string output)
    {
        string list = Path.Combine(Path.GetDirectoryName(output)!, $".recording-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllLinesAsync(list, segments.Select(path => "file '" + Path.GetFullPath(path).Replace("'", "'\\''") + "'"));
            await RunAsync(ffmpegPath, $"-f concat -safe 0 -i {Quote(list)} -c copy -y {Quote(output)}", output);
        }
        finally { File.Delete(list); }
    }

    /// <summary>Wait for process startup before sending q, then wait for a flushed output.</summary>
    public static async Task StopProcessAsync(FFmpegCLIManager ffmpeg, Task? running)
    {
        if (running == null) return;
        while (!ffmpeg.IsProcessRunning && !running.IsCompleted) await Task.Delay(10);
        if (!running.IsCompleted)
        {
            ffmpeg.StopRequested = true;
            try { ffmpeg.WriteInput("q"); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        }
        if (await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(10))) != running)
        {
            ffmpeg.ForceClose();
            await running;
            throw new IOException("FFmpeg did not finalize the recording before shutdown timed out. The source recording has been retained.");
        }
        await running;
    }

    private static async Task RunAsync(string ffmpegPath, string args, string output, Action<int>? progress = null)
    {
        await Task.Run(() =>
        {
            using var ffmpeg = new FFmpegCLIManager(ffmpegPath) { TrackEncodeProgress = progress != null };
            if (progress != null) ffmpeg.EncodeProgressChanged += percentage => progress(Math.Clamp((int)percentage, 0, 100));
            if (!ffmpeg.Run(args) || !File.Exists(output) || new FileInfo(output).Length == 0)
            {
                DebugHelper.WriteLine($"[RecordingEncoding] FFmpeg failed: {ffmpegPath} {args}\n{ffmpeg.Output}");
                throw new RecordingFailedException(Summarize($"FFmpeg could not finish the recording.\n{ffmpeg.Output}"));
            }
        });
    }
}
