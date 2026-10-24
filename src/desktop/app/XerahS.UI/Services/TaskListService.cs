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

using Avalonia.Threading;
using System.Collections.ObjectModel;
using XerahS.Bootstrap;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Platform.Abstractions;
using XerahS.UI.ViewModels;
using XerahS.Uploaders;

namespace XerahS.UI.Services;

/// <summary>
/// The tasks of ShareX's main window task list (TaskManager.TaskAdded and AddRecentTasksToMainWindow), and
/// ShareX's TaskManager.UpdateProgressUI: the upload progress on the tray icon and the taskbar (the main window
/// title already shows it; see WorkflowOrchestrator).
/// </summary>
public sealed class TaskListService
{
    public static TaskListService Instance { get; } = new();

    private readonly List<WorkerTask> _activeTasks = new();
    private IDesktopTaskManager? _taskManager;
    private int? _lastProgress;

    public ObservableCollection<TaskThumbnailViewModel> Items { get; } = new();

    public IDesktopTaskManager? TaskManager => _taskManager;

    internal TaskListService()
    {
    }

    public void Initialize(IDesktopTaskManager taskManager)
    {
        if (_taskManager != null) return;
        _taskManager = taskManager;
        taskManager.TaskStarted += (_, task) => Track(task);

        // ShareX's AddRecentTasksToMainWindow, when its main window opens with no tasks.
        ApplicationConfig settings = SettingsManager.Settings;
        if (settings.RecentTasksSave && settings.RecentTasksShowInMainWindow)
        {
            foreach (RecentTask recentTask in SettingsManager.RecentTaskManager.GetTasks())
            {
                Add(new TaskThumbnailViewModel(recentTask));
            }
        }
    }

    /// <summary>
    /// Runs when the task manager raises TaskStarted, before the task starts, so the task's events are handled
    /// from the start even when the task ends at once. The list is changed on the UI thread, in the order the
    /// events came: a task that completed before the UI thread added it is still removed.
    /// </summary>
    private void Track(WorkerTask task)
    {
        void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => Update(task));
        void OnProgress(ProgressManager progress) => Dispatcher.UIThread.Post(() => Update(task));
        void OnCompleted(object? sender, EventArgs e)
        {
            task.StatusChanged -= OnChanged;
            task.TaskCompleted -= OnCompleted;
            if (task.Info != null) task.Info.UploadProgressChanged -= OnProgress;
            Dispatcher.UIThread.Post(() =>
            {
                _activeTasks.Remove(task);
                Update(task);
            });
        }

        task.StatusChanged += OnChanged;
        task.TaskCompleted += OnCompleted;
        if (task.Info != null) task.Info.UploadProgressChanged += OnProgress;
        Dispatcher.UIThread.Post(() =>
        {
            _activeTasks.Add(task);
            Update(task);
        });
    }

    internal int ActiveTaskCount => _activeTasks.Count;

    /// <summary>
    /// As in ShareX, a capture appears once it has captured something, and an upload at once. Tasks that only
    /// open a tool, or that were cancelled before capturing, never appear.
    /// </summary>
    private void Update(WorkerTask task)
    {
        TaskThumbnailViewModel? item = Items.FirstOrDefault(i => ReferenceEquals(i.Task, task));
        if (item != null)
        {
            item.Refresh();
        }
        else if (task.Info != null && (task.HasContent || task.Info.Progress != null || !string.IsNullOrEmpty(task.Info.Result?.URL)))
        {
            Add(new TaskThumbnailViewModel(task));
        }

        UpdateProgressUI();
    }

    private void Add(TaskThumbnailViewModel item)
    {
        if (SettingsManager.Settings.ShowMostRecentTaskFirst) Items.Insert(0, item);
        else Items.Add(item);
    }

    /// <summary>Removes tasks from the list, stopping those still running, as ShareX's "Remove task from list".</summary>
    public void Remove(IEnumerable<TaskThumbnailViewModel> items)
    {
        foreach (TaskThumbnailViewModel item in items.ToList())
        {
            if (item.IsWorking) item.Task?.Stop();
            Items.Remove(item);
            item.Dispose();
        }
    }

    /// <summary>ShareX's "Clear task list": removes the finished tasks and forgets the recent tasks.</summary>
    public void Clear()
    {
        Remove(Items.Where(item => !item.IsWorking));
        SettingsManager.RecentTaskManager.Clear();
    }

    /// <summary>Applies changed main window settings to the thumbnails.</summary>
    public void RefreshSettings()
    {
        foreach (TaskThumbnailViewModel item in Items) item.RefreshSettings();
    }

    /// <summary>ShareX's UpdateProgressUI: the average progress of the uploading tasks.</summary>
    private void UpdateProgressUI()
    {
        // XerahS's capture and recording tasks are "working" too, so only uploads that report progress count.
        double[] uploads = _activeTasks
            .Where(t => t.Status == Core.TaskStatus.Working && t.Info?.Progress != null)
            .Select(t => t.Info.Progress!.Percentage)
            .Select(p => double.IsFinite(p) ? Math.Clamp(p, 0, 100) : 0)
            .ToArray();
        int? progress = uploads.Length > 0 ? (int)uploads.Average() : null;

        if (progress == _lastProgress) return;
        _lastProgress = progress;

        TrayIconHelper.Instance.SetTaskProgress(progress);
        try
        {
            if (PlatformServices.IsInitialized && SettingsManager.Settings.TaskbarProgressEnabled)
            {
                PlatformServices.System.SetTaskbarProgress(progress);
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Unable to show the progress on the taskbar");
        }
    }
}
