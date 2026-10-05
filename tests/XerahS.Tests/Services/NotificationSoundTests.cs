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
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.Core.Tasks;
using TaskStatus = XerahS.Core.TaskStatus;
using XerahS.Platform.Abstractions;

namespace XerahS.Tests.Services;

[TestFixture, NonParallelizable]
public class NotificationSoundTests
{
    [TearDown]
    public void TearDown() => PlatformServices.SoundPlayback = null;

    [TestCase(true), TestCase(false)]
    public async Task DefaultSoundHonorsEffectiveSettings(bool enabled)
    {
        var playback = new Playback();
        PlatformServices.SoundPlayback = playback;
        var defaults = new TaskSettings();
        defaults.GeneralSettings.PlaySoundAfterAction = enabled;
        var saved = new TaskSettings();
        saved.GeneralSettings.PlaySoundAfterAction = !enabled;
        await NotificationSoundService.PlayActionCompletedAsync(TaskSettings.GetSafeTaskSettings(saved, defaults));
        Assert.That(playback.Calls, Is.EqualTo(enabled ? 1 : 0));
        if (enabled) Assert.That(System.Text.Encoding.ASCII.GetString(playback.Audio!, 0, 4), Is.EqualTo("RIFF"));
    }

    [Test]
    public async Task EmptyCustomPathUsesShareXsDefaultSound()
    {
        var playback = new Playback();
        PlatformServices.SoundPlayback = playback;
        var settings = new TaskSettings { UseDefaultGeneralSettings = false };
        settings.GeneralSettings.PlaySoundAfterAction = true;
        settings.GeneralSettings.UseCustomActionCompletedSound = true;
        settings.GeneralSettings.CustomActionCompletedSoundPath = "";
        await NotificationSoundService.PlayActionCompletedAsync(settings);
        Assert.That(playback.Calls, Is.EqualTo(1));
        Assert.That(System.Text.Encoding.ASCII.GetString(playback.Audio!, 0, 4), Is.EqualTo("RIFF"));
    }

    [Test]
    public async Task CustomSoundAndPlaybackFailureDoNotAffectCapture()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] audio = [1, 2, 3, 4];
            await File.WriteAllBytesAsync(path, audio);
            var settings = new TaskSettings { UseDefaultGeneralSettings = false };
            settings.GeneralSettings.PlaySoundAfterAction = true;
            settings.GeneralSettings.UseCustomActionCompletedSound = true;
            settings.GeneralSettings.CustomActionCompletedSoundPath = path;
            var playback = new Playback { Throw = true };
            PlatformServices.SoundPlayback = playback;
            await NotificationSoundService.PlayActionCompletedAsync(settings);
            Assert.That(playback.Audio, Is.EqualTo(audio));
            File.Delete(path);
            await NotificationSoundService.PlayActionCompletedAsync(settings);
            Assert.That(playback.Calls, Is.EqualTo(1), "A missing custom file is silent, as in ShareX.");
        }
        finally { File.Delete(path); }
    }

    [TestCase(NotificationSound.Capture, "CaptureSound.wav")]
    [TestCase(NotificationSound.TaskCompleted, "TaskCompletedSound.wav")]
    [TestCase(NotificationSound.ActionCompleted, "ActionCompletedSound.wav")]
    [TestCase(NotificationSound.Error, "ErrorSound.wav")]
    public async Task EachSoundPlaysShareXsOwnDefaultFile(NotificationSound sound, string fileName)
    {
        var playback = new Playback();
        PlatformServices.SoundPlayback = playback;
        var settings = new TaskSettings { UseDefaultGeneralSettings = false };
        await NotificationSoundService.PlayAsync(sound, settings);
        using var expected = typeof(NotificationSoundService).Assembly.GetManifestResourceStream("XerahS.Core.Resources." + fileName)!;
        using var buffer = new MemoryStream();
        await expected.CopyToAsync(buffer);
        Assert.That(playback.Audio, Is.EqualTo(buffer.ToArray()));
    }

    // ShareX: capture follows "after capture", task completed and error follow "after task", actions follow "after action".
    [TestCase(NotificationSound.Capture, true, false, false)]
    [TestCase(NotificationSound.TaskCompleted, false, true, false)]
    [TestCase(NotificationSound.Error, false, true, false)]
    [TestCase(NotificationSound.ActionCompleted, false, false, true)]
    public async Task EachSoundHasItsOwnSwitch(NotificationSound sound, bool capture, bool upload, bool action)
    {
        var playback = new Playback();
        PlatformServices.SoundPlayback = playback;
        var settings = new TaskSettings { UseDefaultGeneralSettings = false };
        settings.GeneralSettings.PlaySoundAfterCapture = capture;
        settings.GeneralSettings.PlaySoundAfterUpload = upload;
        settings.GeneralSettings.PlaySoundAfterAction = action;
        await NotificationSoundService.PlayAsync(sound, settings);
        Assert.That(playback.Calls, Is.EqualTo(1));

        settings.GeneralSettings.PlaySoundAfterCapture = !capture;
        settings.GeneralSettings.PlaySoundAfterUpload = !upload;
        settings.GeneralSettings.PlaySoundAfterAction = !action;
        await NotificationSoundService.PlayAsync(sound, settings);
        Assert.That(playback.Calls, Is.EqualTo(1), "Turning off the sound's own switch silences it, and the other switches do not play it.");
    }

    [TestCase(NotificationSound.Capture)]
    [TestCase(NotificationSound.TaskCompleted)]
    [TestCase(NotificationSound.Error)]
    public async Task EachSoundUsesItsOwnCustomFile(NotificationSound sound)
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] audio = [(byte)sound, 9, 9];
            await File.WriteAllBytesAsync(path, audio);
            var settings = new TaskSettings { UseDefaultGeneralSettings = false };
            var general = settings.GeneralSettings;
            switch (sound)
            {
                case NotificationSound.Capture: general.UseCustomCaptureSound = true; general.CustomCaptureSoundPath = path; break;
                case NotificationSound.TaskCompleted: general.UseCustomTaskCompletedSound = true; general.CustomTaskCompletedSoundPath = path; break;
                case NotificationSound.Error: general.UseCustomErrorSound = true; general.CustomErrorSoundPath = path; break;
            }
            var playback = new Playback();
            PlatformServices.SoundPlayback = playback;
            await NotificationSoundService.PlayAsync(sound, settings);
            Assert.That(playback.Audio, Is.EqualTo(audio));
            await NotificationSoundService.PlayAsync(NotificationSound.ActionCompleted, settings);
            Assert.That(playback.Audio, Is.Not.EqualTo(audio), "Another sound keeps its default file.");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void TaskCompletionSoundFollowsShareXsTaskManager()
    {
        var info = new TaskInfo(new TaskSettings());
        Assert.That(WorkerTask.GetCompletionSound(TaskStatus.Completed, info), Is.Null, "No result, no sound.");

        info.FilePath = "/tmp/capture.png";
        Assert.That(WorkerTask.GetCompletionSound(TaskStatus.Completed, info), Is.EqualTo(NotificationSound.TaskCompleted));
        Assert.That(WorkerTask.GetCompletionSound(TaskStatus.Stopped, info), Is.Null);
        Assert.That(WorkerTask.GetCompletionSound(TaskStatus.Canceled, info), Is.Null);
        Assert.That(WorkerTask.GetCompletionSound(TaskStatus.Failed, info), Is.EqualTo(NotificationSound.Error));

        info.Result.Errors.Add("s-ul: API key error.");
        Assert.That(WorkerTask.GetCompletionSound(TaskStatus.Completed, info), Is.EqualTo(NotificationSound.Error),
            "A capture whose upload failed plays the error sound.");

        var share = new TaskInfo(new TaskSettings()) { Job = TaskJob.ShareURL };
        share.Result.URL = "https://example.com";
        Assert.That(WorkerTask.GetCompletionSound(TaskStatus.Completed, share), Is.Null, "As in ShareX, a shared URL plays no sound.");
    }

    private sealed class Playback : ISoundPlaybackService
    {
        public int Calls;
        public byte[]? Audio;
        public bool Throw;
        public Task PlayAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default)
        {
            Calls++; Audio = audio.ToArray();
            return Throw ? Task.FromException(new IOException("Playback unavailable")) : Task.CompletedTask;
        }
    }
}
