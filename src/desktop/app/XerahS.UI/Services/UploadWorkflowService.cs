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

using System.Web;
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

            bool shorten = job == WorkflowType.ShortenURL;
            // As in ShareX: Shorten URL starts with a URL from the clipboard, Upload text with any clipboard text.
            string? initialText = await GetClipboardTextAsync();
            if (shorten)
            {
                initialText = UploadInputWindow.IsValidUrl(initialText) ? initialText!.Trim() : null;
            }
            var prompt = new UploadInputWindow(shorten, initialText);
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

    /// <summary>
    /// ShareX's custom text for dropped text: the advanced setting TextCustom with %input replaced by the
    /// text, HTML-encoded first when TextCustomEncodeInput is on. Without a template the text is unchanged.
    /// </summary>
    internal static string ApplyCustomText(string text, TaskSettings settings)
    {
        string? template = settings.AdvancedSettings?.TextCustom;
        if (string.IsNullOrEmpty(template)) return text;
        if (settings.AdvancedSettings!.TextCustomEncodeInput) text = HttpUtility.HtmlEncode(text);
        return template.Replace("%input", text);
    }

    internal static TaskSettings CreateExecutionSettings(TaskSettings source, WorkflowType job)
    {
        var settings = WatchFolderManager.CloneTaskSettings(source);
        settings.WorkflowId = source.WorkflowId;
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

        foreach (var path in files.FilePaths)
        {
            scope.Token.ThrowIfCancellationRequested();
            var executionSettings = CreateExecutionSettings(settings, WorkflowType.FileUpload);
            CancellationTokenRegistration cancellation = default;
            void Started(object? sender, Core.Tasks.WorkerTask task)
            {
                if (ReferenceEquals(task.Info.TaskSettings, executionSettings)) cancellation = scope.Token.Register(task.Stop);
            }
            taskManager.TaskStarted += Started;
            try
            {
                await taskManager.StartFileTask(executionSettings, path);
            }
            finally
            {
                taskManager.TaskStarted -= Started;
                cancellation.Dispose();
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
