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

using System.Runtime.InteropServices;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Windows.Services;

/// <summary>Plays WAV audio from memory with the Windows multimedia API, as ShareX's SoundPlayer does.</summary>
internal sealed class WindowsSoundPlaybackService : ISoundPlaybackService
{
    private const uint SND_SYNC = 0x0000;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    public Task PlayAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default)
    {
        byte[] buffer = audio.ToArray();
        // Synchronous playback keeps the buffer alive until the sound has finished.
        return Task.Run(() =>
        {
            if (!PlaySound(buffer, IntPtr.Zero, SND_SYNC | SND_MEMORY | SND_NODEFAULT))
                throw new IOException("Windows could not play the notification sound.");
        }, cancellationToken);
    }

    [DllImport("winmm.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(byte[] sound, IntPtr module, uint flags);
}
