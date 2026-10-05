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

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using NUnit.Framework;
using XerahS.UI.Helpers;

namespace XerahS.Tests.UI;

[TestFixture, NonParallelizable]
public class StartupWorkflowLauncherTests
{
    [AvaloniaTest]
    public void WorkflowWaitsUntilWindowHasOpenedWithoutRequestingFocus()
    {
        var window = new Window();
        int runs = 0;
        bool sawOpened = false;
        window.Opened += (_, _) => sawOpened = true;
        StartupWorkflowLauncher.RunAfterOpened(window, () =>
        {
            Assert.That(sawOpened, Is.True);
            runs++;
        });
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.That(runs, Is.Zero);
            Assert.That(window.ShowActivated, Is.False);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(runs, Is.EqualTo(1));
            window.Hide();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(runs, Is.EqualTo(1));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void ClosingDuringStartupCancelsQueuedWorkflow()
    {
        var window = new Window();
        int runs = 0;
        StartupWorkflowLauncher.RunAfterOpened(window, () => runs++);
        window.Show();
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.That(runs, Is.Zero);
    }
}
