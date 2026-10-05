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
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux.Wayland.WindowQuery;

namespace XerahS.Platform.Linux.Services;

/// <summary>Uses the session's audio server through its installed playback client.</summary>
internal sealed class LinuxSoundPlaybackService : ISoundPlaybackService
{
    public async Task PlayAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default)
    {
        string? player = new[] { "pw-play", "paplay", "aplay" }
            .FirstOrDefault(WaylandWindowPointQueryCommandRunner.CommandExists);
        if (player == null) throw new PlatformNotSupportedException("Install pw-play, paplay, or aplay to play notification sounds.");
        string path = Path.Combine(Path.GetTempPath(), "xerahs-sound-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (OperatingSystem.IsLinux()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(path, fileOptions))
                await stream.WriteAsync(audio, cancellationToken).ConfigureAwait(false);
            var start = new ProcessStartInfo(player) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            start.ArgumentList.Add(path);
            using var process = Process.Start(start) ?? throw new IOException("Could not start notification sound playback.");
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
                if (process.ExitCode != 0) throw new IOException($"Notification sound playback failed (exit {process.ExitCode}).");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        finally { File.Delete(path); }
    }
}
