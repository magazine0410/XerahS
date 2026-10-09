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

using Avalonia.Input;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using XerahS.Bootstrap;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Managers;
using XerahS.Core.SendTo;
using XerahS.Platform.Abstractions;
using XerahS.UI.Views;
using XerahS.UI.Views.Dialogs;
using XerahS.Uploaders;

namespace XerahS.UI.Services;

internal static class UploadWorkflowService
{
    private static DragDropUploadWindow? _dropWindow;

    internal static async Task UploadDroppedDataAsync(IDataTransfer data, TaskSettings settings, IDesktopTaskManager taskManager)
    {
        // Copy drag data while the native drop event is alive, before the first await.
        var paths = UploadContentWindow.GetDroppedStorageItems(data)
            .Select(item => item.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths.Length > 0)
        {
            await UploadWorkflowService.UploadPathsAsync(paths, settings, taskManager);
            return;
        }
        var bitmap = data.TryGetBitmap();
        if (bitmap != null)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            using var image = XerahS.Common.ImageHelpers.LoadBitmap(stream);
            if (image == null) throw new IOException("The dropped image could not be read.");
            await taskManager.StartTask(UploadWorkflowService.CreateExecutionSettings(settings, WorkflowType.PrintScreen), image);
            return;
        }
        string? text = data.TryGetText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            // As in ShareX, dropped text goes through the task's custom text template.
            var textSettings = UploadWorkflowService.CreateExecutionSettings(settings, WorkflowType.UploadText);
            await taskManager.StartTextTask(textSettings, UploadWorkflowService.ApplyCustomText(text, textSettings));
        }
    }

    internal static void RefreshDropWindowSettings() => _dropWindow?.ApplySettings(SettingsManager.Settings);

    public static async Task HandleWorkflowAsync(WorkflowType job, Window? owner, TaskSettings? taskSettings,
        IDesktopTaskManager taskManager)
    {
        var settings = taskSettings ?? SettingsManager.DefaultTaskSettings;
        try
        {
            if (job == WorkflowType.DragDropUpload)
            {
                if (_dropWindow == null)
                {
                    _dropWindow = new DragDropUploadWindow();
                    _dropWindow.Closed += (_, _) => _dropWindow = null;
                }
                _dropWindow.Configure(settings, taskManager);
                _dropWindow.ApplySettings(SettingsManager.Settings);
                _dropWindow.Show();
                _dropWindow.Activate();
                return;
            }

            if (job == WorkflowType.FolderUpload)
            {
                var storage = StorageProviderResolver.Resolve(owner);
                if (storage == null) throw new InvalidOperationException("The folder picker is unavailable.");
                var initial = SettingsManager.Settings.FileUploadDefaultDirectory;
                var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select folder to upload",
                    AllowMultiple = false,
                    SuggestedStartLocation = Directory.Exists(initial)
                        ? await storage.TryGetFolderFromPathAsync(initial)
                        : await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Desktop)
                });
                if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
                SettingsManager.Settings.FileUploadDefaultDirectory = path;
                await SettingsManager.SaveApplicationConfigAsync();
                await UploadPathsAsync([path], settings, taskManager);
                return;
            }

            bool isUrl = job is WorkflowType.ShortenURL or WorkflowType.UploadURL;
            // As in ShareX: URL jobs start with a valid clipboard URL; Upload text accepts any clipboard text.
            string? initialText = await GetClipboardTextAsync();
            if (isUrl)
            {
                initialText = UploadInputWindow.IsValidUrl(initialText) ? initialText!.Trim() : null;
            }
            var kind = job == WorkflowType.UploadURL ? UploadInputKind.UploadUrl
                : isUrl ? UploadInputKind.ShortenUrl : UploadInputKind.Text;
            var prompt = new UploadInputWindow(kind, initialText);
            var input = await prompt.ShowAsync(owner);
            if (input != null)
            {
                await taskManager.StartTextTask(CreateExecutionSettings(settings, job), input);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private static async Task<string?> GetClipboardTextAsync()
    {
        if (!PlatformServices.IsInitialized) return null;
        try
        {
            return await PlatformServices.Clipboard.GetTextAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Could not read the clipboard text");
            return null;
        }
    }

    /// <inheritdoc cref="TaskHelpers.ApplyCustomText"/>
    internal static string ApplyCustomText(string text, TaskSettings settings) => TaskHelpers.ApplyCustomText(text, settings);

    internal static TaskSettings CreateExecutionSettings(TaskSettings source, WorkflowType job)
    {
        var settings = TaskSettings.GetSafeTaskSettings(source);
        settings.Job = job;
        return settings;
    }

    internal static async Task UploadPathsAsync(IEnumerable<string> paths, TaskSettings settings, IDesktopTaskManager taskManager)
    {
        using var scope = new UploadCancellationScope();
        var pathList = paths.ToArray();
        var files = await Task.Run(() => SendToPolicyResolver.ResolveFiles(
            SendToSelectionClassifier.Create(pathList.Where(File.Exists), pathList.Where(Directory.Exists)),
            SendToFolderPolicy.IncludeFilesRecursively), scope.Token);
        scope.Token.ThrowIfCancellationRequested();
        if (files.FailedFolderCount > 0)
        {
            throw new IOException("Some folders could not be read. Check their permissions and try again.");
        }
        if (files.FilePaths.Count == 0)
        {
            throw new IOException("There are no files to upload in the selected folder or selection.");
        }
        if (!await ConfirmMultiUploadAsync(files.FilePaths.Count))
        {
            return;
        }

        // As in ShareX, every file is uploaded by a task of its own, and the tasks all start at once; the simultaneous
        // upload limit (Application Settings → Upload) queues them.
        var executionSettings = new HashSet<TaskSettings>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var cancellations = new List<CancellationTokenRegistration>();
        void Started(object? sender, Core.Tasks.WorkerTask task)
        {
            lock (executionSettings)
            {
                if (executionSettings.Contains(task.Info.TaskSettings)) cancellations.Add(scope.Token.Register(task.Stop));
            }
        }

        var uploads = new List<Task>(files.FilePaths.Count);
        taskManager.TaskStarted += Started;
        try
        {
            foreach (var path in files.FilePaths)
            {
                scope.Token.ThrowIfCancellationRequested();
                var fileSettings = CreateExecutionSettings(settings, WorkflowType.FileUpload);
                lock (executionSettings) executionSettings.Add(fileSettings);
                uploads.Add(taskManager.StartFileTask(fileSettings, path));
            }
        }
        finally
        {
            // Started tasks finish (or stop) before the batch ends, also when Stop all uploads interrupted the starts.
            await Task.WhenAll(uploads);
            taskManager.TaskStarted -= Started;
            lock (executionSettings)
            {
                foreach (var cancellation in cancellations) cancellation.Dispose();
            }
        }
    }

    /// <summary>
    /// ShareX asks before uploading more than 10 files, unless "Don't show this message again" was ticked.
    /// </summary>
    internal static Func<int, Task<MultiUploadConfirmationResult>> ShowMultiUploadConfirmation { get; set; } =
        count => Dispatcher.UIThread.InvokeAsync(() => MultiUploadConfirmationWindow.ShowAsync(count));

    /// <summary>Saves the application settings after "Don't show this message again".</summary>
    internal static Func<Task> SaveApplicationSettings { get; set; } = () => SettingsManager.SaveApplicationConfigAsync();

    internal static async Task<bool> ConfirmMultiUploadAsync(int fileCount)
    {
        if (fileCount <= 10 || !SettingsManager.Settings.ShowMultiUploadWarning) return true;

        var result = await ShowMultiUploadConfirmation(fileCount);
        if (result.DontShowAgain)
        {
            SettingsManager.Settings.ShowMultiUploadWarning = false;
            await SaveApplicationSettings();
        }
        return result.IsConfirmed;
    }

    internal static void ReportError(Exception ex, string title = "Upload failed")
    {
        DebugHelper.WriteException(ex, "Upload workflow failed");
        if (PlatformServices.IsToastServiceInitialized)
        {
            try
            {
                PlatformServices.Toast.ShowToast(new ToastConfig
                {
                    Title = title,
                    Text = ex.Message,
                    Duration = 5,
                    AutoHide = true
                });
            }
            catch (Exception notificationError)
            {
                DebugHelper.WriteException(notificationError, "Could not show the upload error notification");
            }
        }
    }
}
