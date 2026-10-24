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
using XerahS.UI.Services;

namespace XerahS.Tests.UI;

[TestFixture]
public class DelayedSaveTests
{
    [Test]
    public async Task ManyChangesInARow_AreSavedOnce_AfterTheyStop()
    {
        int saves = 0;
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new DelayedSave(() =>
        {
            Interlocked.Increment(ref saves);
            saved.TrySetResult();
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(500));

        // As a color being dragged: a change every 10 ms for 300 ms.
        for (int change = 0; change < 30; change++)
        {
            delayed.Request();
            await Task.Delay(10);
        }

        Assert.That(saves, Is.Zero, "nothing is saved while the changes keep coming");
        Assert.That(await Task.WhenAny(saved.Task, Task.Delay(5000)), Is.SameAs(saved.Task));
        await Task.Delay(300);
        Assert.That(saves, Is.EqualTo(1));
    }
}
