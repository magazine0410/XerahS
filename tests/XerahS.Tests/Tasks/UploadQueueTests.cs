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
using XerahS.Core.Managers;

namespace XerahS.Tests.Tasks;

[TestFixture]
public class UploadQueueTests
{
    [Test]
    public async Task Limit_RunsThatManyTasks_AndStartsTheOthersInTheOrderTheyCame()
    {
        int limit = 2;
        var queue = new UploadQueue(() => limit);
        var first = await queue.EnterAsync(default);
        var second = await queue.EnterAsync(default);
        var third = queue.EnterAsync(default);
        var fourth = queue.EnterAsync(default);

        Assert.Multiple(() =>
        {
            Assert.That(third.IsCompleted, Is.False);
            Assert.That(fourth.IsCompleted, Is.False);
            Assert.That(queue.RunningCount, Is.EqualTo(2));
            Assert.That(queue.WaitingCount, Is.EqualTo(2));
        });

        second.Dispose();
        var thirdPlace = await third.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(fourth.IsCompleted, Is.False, "The first task to wait starts first.");

        second.Dispose();
        Assert.That(fourth.IsCompleted, Is.False, "A place is freed once.");

        first.Dispose();
        var fourthPlace = await fourth.WaitAsync(TimeSpan.FromSeconds(5));
        thirdPlace.Dispose();
        fourthPlace.Dispose();
        Assert.That(queue.RunningCount, Is.Zero);
    }

    [Test]
    public async Task ZeroDisablesTheLimit()
    {
        var queue = new UploadQueue(() => 0);
        var places = new List<IDisposable>();
        for (int i = 0; i < 30; i++) places.Add(await queue.EnterAsync(default).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.That(queue.RunningCount, Is.EqualTo(30));
        places.ForEach(place => place.Dispose());
        Assert.That(queue.RunningCount, Is.Zero);
    }

    [Test]
    public async Task StoppedWhileWaiting_LeavesTheQueue()
    {
        var queue = new UploadQueue(() => 1);
        var running = await queue.EnterAsync(default);
        using var stop = new CancellationTokenSource();
        var stopped = queue.EnterAsync(stop.Token);
        var next = queue.EnterAsync(default);

        stop.Cancel();
        Assert.That(async () => await stopped, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(queue.WaitingCount, Is.EqualTo(1));

        running.Dispose();
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.That(queue.RunningCount, Is.Zero);
    }

    [Test]
    public async Task RaisingTheLimit_StartsWaitingTasksWithTheNextTask()
    {
        int limit = 1;
        var queue = new UploadQueue(() => limit);
        var running = await queue.EnterAsync(default);
        var waiting = queue.EnterAsync(default);
        Assert.That(waiting.IsCompleted, Is.False);

        limit = 3;
        var newer = await queue.EnterAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        var started = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(queue.RunningCount, Is.EqualTo(3));

        running.Dispose();
        newer.Dispose();
        started.Dispose();
    }
}
