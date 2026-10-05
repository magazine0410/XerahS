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

using System.Drawing;
using NUnit.Framework;
using XerahS.Platform.Linux;
using XerahS.Platform.Linux.Capture.Scrolling;

namespace XerahS.Tests.Services;

[TestFixture]
public class ScrollingCaptureSessionTests
{
    [TestCase(0u, false), TestCase(1u, false), TestCase(2u, false), TestCase(3u, true), TestCase(7u, true)]
    public void PortalRequiresGrantedKeyboardAndPointer(uint devices, bool expected) =>
        Assert.That(RemoteDesktopScrollInput.HasRequiredDevices(devices), Is.EqualTo(expected));

    [Test]
    public void PortalOnlyUsesPersistenceOnVersionTwoOrNewer()
    {
        Assert.That(RemoteDesktopScrollInput.CreateSelectOptions(1, "saved").Keys, Is.EquivalentTo(new[] { "types" }));
        var options = RemoteDesktopScrollInput.CreateSelectOptions(2, "saved");
        Assert.That(options["restore_token"], Is.EqualTo("saved"));
        Assert.That(options["persist_mode"], Is.EqualTo(2u));
    }

    [Test]
    public async Task KeyPressSendsPressThenRelease()
    {
        var states = new List<uint>();
        await RemoteDesktopScrollInput.SendKeyPressAsync(state => { states.Add(state); return Task.CompletedTask; });
        Assert.That(states, Is.EqualTo(new[] { 1u, 0u }));
    }

    [Test]
    public void FailedKeyPressStillReleasesTheKeyAndReportsThePressError()
    {
        var states = new List<uint>();
        var error = Assert.ThrowsAsync<IOException>(() => RemoteDesktopScrollInput.SendKeyPressAsync(state =>
        {
            states.Add(state);
            return Task.FromException(state == 1 ? new IOException("Press failed") : new InvalidOperationException("Release failed"));
        }));
        Assert.That(error!.Message, Is.EqualTo("Press failed"));
        Assert.That(states, Is.EqualTo(new[] { 1u, 0u }));
    }

    [TestCase(false), TestCase(true)]
    public async Task FailedOrCancelledStartupDisposesInput(bool cancelled)
    {
        var input = new Input { Begin = _ => cancelled ? Task.FromException<bool>(new OperationCanceledException()) : Task.FromResult(false) };
        int factories = 0;
        var service = new LinuxScrollingCaptureService(() => true, () => true, () => false, () => { factories++; return input; });
        if (cancelled) Assert.ThrowsAsync<OperationCanceledException>(() => service.BeginAsync());
        else Assert.That(await service.BeginAsync(), Is.False);
        await service.EndAsync();
        Assert.That(input.Disposals, Is.EqualTo(1));
        Assert.That(factories, Is.EqualTo(1), "Refusal never tries a different input backend.");
    }

    [Test]
    public async Task ConcurrentBeginAndEndCannotLeakALateSession()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new Input { Begin = _ => { started.SetResult(); return finish.Task; } };
        var service = new LinuxScrollingCaptureService(() => true, () => true, () => false, () => input);
        var begin = service.BeginAsync();
        await started.Task;
        var end = service.EndAsync();
        Assert.That(end.IsCompleted, Is.False);
        finish.SetResult(true);
        Assert.That(await begin, Is.True);
        await end;
        await service.EndAsync();
        Assert.That(input.Disposals, Is.EqualTo(1));
    }

    private sealed class Input : IScrollInput
    {
        public required Func<CancellationToken, Task<bool>> Begin;
        public int Disposals;
        public Task<bool> BeginAsync(CancellationToken token) => Begin(token);
        public Task MovePointerAsync(Point point) => Task.CompletedTask;
        public Task ScrollWheelAsync(int notches) => Task.CompletedTask;
        public Task PressKeyAsync(int keysym) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
