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

using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Bootstrap;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.UI.Services;

namespace XerahS.Tests.UI;

[TestFixture]
public class TaskListServiceTests
{
    [AvaloniaTest]
    public void ATaskThatEndsBeforeTheUIThreadRuns_IsNoLongerCountedAsActive()
    {
        var manager = new StartingTaskManager();
        var service = new TaskListService();
        service.Initialize(manager);

        using WorkerTask task = WorkerTask.Create(new TaskSettings { Job = WorkflowType.FileUpload });
        task.Stop();
        manager.RaiseTaskStarted(task);
        // The task runs on another thread while this one waits, so nothing posted to the UI thread runs before it ends.
        Task.Run(task.StartAsync).Wait();
        Assert.That(task.HasFinished, Is.True, "the task ended before the UI thread ran anything posted to it");

        Dispatcher.UIThread.RunJobs();
        Assert.That(service.ActiveTaskCount, Is.Zero);
    }

    private sealed class StartingTaskManager : IDesktopTaskManager
    {
        public event EventHandler<WorkerTask>? TaskCompleted { add { } remove { } }
        public event EventHandler<WorkerTask>? TaskStarted;

        public IEnumerable<WorkerTask> Tasks => [];

        public void RaiseTaskStarted(WorkerTask task) => TaskStarted?.Invoke(this, task);

        public Task StartTask(TaskSettings? taskSettings, SKBitmap? inputImage = null) => Task.CompletedTask;
        public Task StartFileTask(TaskSettings? taskSettings, string filePath) => Task.CompletedTask;
        public Task StartImageUploadTask(TaskSettings? taskSettings, SKBitmap image) => Task.CompletedTask;
        public Task StartTextTask(TaskSettings? taskSettings, string text) => Task.CompletedTask;

        public void StopAllTasks() { }
    }
}
