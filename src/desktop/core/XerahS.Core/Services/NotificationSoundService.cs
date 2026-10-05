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
using XerahS.Platform.Abstractions;

namespace XerahS.Core.Services;

/// <summary>Resolves the effective workflow's sound options, matching ShareX's ActionCompleted sound.</summary>
public static class NotificationSoundService
{
    public static void PlayActionCompleted(TaskSettings settings) =>
        _ = Task.Run(() => PlayActionCompletedAsync(settings));

    public static async Task PlayActionCompletedAsync(TaskSettings settings)
    {
        try
        {
            var general = TaskSettings.GetSafeTaskSettings(settings).GeneralSettings;
            if (!general.PlaySoundAfterAction || PlatformServices.SoundPlayback is not { } playback) return;
            byte[] audio;
            if (general.UseCustomActionCompletedSound && !string.IsNullOrEmpty(general.CustomActionCompletedSoundPath))
            {
                string path = FileHelpers.GetAbsolutePath(general.CustomActionCompletedSoundPath);
                if (!File.Exists(path)) return;
                audio = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }
            else
            {
                using var stream = typeof(NotificationSoundService).Assembly.GetManifestResourceStream("XerahS.Core.Resources.ActionCompletedSound.wav")!;
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer).ConfigureAwait(false);
                audio = buffer.ToArray();
            }
            await playback.PlayAsync(audio).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A sound failure must never discard a capture or prevent its upload.
            DebugHelper.WriteException(ex, "Notification sound playback");
        }
    }
}
