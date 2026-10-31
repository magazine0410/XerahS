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
using XerahS.Platform.Abstractions;
using XerahS.UI.Views;

namespace XerahS.Tests.UI;

/// <summary>ShareX's UploadInfoManager copy commands on the Tasks page: each copy plays the action-completed sound.</summary>
[TestFixture, NonParallelizable]
public class TasksViewCopyTests
{
    private ClipboardProxy _clipboard = null!;
    private Playback _playback = null!;
    private string _directory = null!;
    private bool _playSoundAfterAction;

    [SetUp]
    public void SetUp()
    {
        var clipboard = DispatchProxy.Create<IClipboardService, ClipboardProxy>();
        _clipboard = (ClipboardProxy)(object)clipboard;
        PlatformServices.Clipboard = clipboard;
        _playback = new Playback();
        PlatformServices.SoundPlayback = _playback;
        _playSoundAfterAction = SettingsManager.DefaultTaskSettings.GeneralSettings.PlaySoundAfterAction;
        SettingsManager.DefaultTaskSettings.GeneralSettings.PlaySoundAfterAction = true;
        _directory = Directory.CreateTempSubdirectory("xerahs-tasks-copy-").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        SettingsManager.DefaultTaskSettings.GeneralSettings.PlaySoundAfterAction = _playSoundAfterAction;
        PlatformServices.Reset();
        PlatformServices.SoundPlayback = null;
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task CopyTexts_CopiesTheNonEmptyTextsAndPlaysTheSound()
    {
        TasksView.CopyTexts(["https://a.test/1", "", "https://a.test/2"]);

        Assert.That(_clipboard.Calls, Is.EqualTo(new[] { "SetText:https://a.test/1" + Environment.NewLine + "https://a.test/2" }));
        Assert.That(await _playback.Played.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
    }

    [Test]
    public async Task CopyFiles_CopiesTheExistingFilesAndPlaysTheSound()
    {
        string file = Path.Combine(_directory, "a.txt");
        File.WriteAllText(file, "a");

        TasksView.CopyFiles([file, Path.Combine(_directory, "missing.txt")]);

        Assert.That(_clipboard.Calls, Is.EqualTo(new[] { "SetFileDropList:" + file }));
        Assert.That(await _playback.Played.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
    }

    [Test]
    public async Task NothingToCopy_CopiesNothingAndPlaysNoSound()
    {
        TasksView.CopyTexts(["", ""]);
        TasksView.CopyFiles([Path.Combine(_directory, "missing.txt")]);
        TasksView.CopyImage(Path.Combine(_directory, "missing.png"));

        await Task.Delay(200);
        Assert.That(_clipboard.Calls, Is.Empty);
        Assert.That(_playback.Played.Task.IsCompleted, Is.False);
    }

    public class ClipboardProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(IClipboardService.SetText): Calls.Add("SetText:" + args![0]); return null;
                case nameof(IClipboardService.SetFileDropList): Calls.Add("SetFileDropList:" + string.Join("|", (string[])args![0]!)); return null;
                default: throw new AssertionException("Unexpected clipboard operation: " + targetMethod.Name);
            }
        }
    }

    private sealed class Playback : ISoundPlaybackService
    {
        public TaskCompletionSource<bool> Played { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PlayAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default)
        {
            Played.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}
