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
using Avalonia.Platform.Storage;
using ShareX.ImageEditor.Hosting;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
using SkiaSharp;
using XerahS.Bootstrap;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Managers;
using XerahS.Platform.Abstractions;

namespace XerahS.UI.Services;

internal static class ImageEditingToolService
{
    public static async Task HandleWorkflowAsync(WorkflowType job, Window? owner, TaskSettings? taskSettings, IDesktopTaskManager taskManager)
    {
        try
        {
            if (job is WorkflowType.ImageEditor or WorkflowType.ImageBeautifier)
            {
                var storageProvider = StorageProviderResolver.Resolve(owner);
                if (storageProvider == null)
                {
                    return;
                }

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = job == WorkflowType.ImageBeautifier ? "Open Image in Beautifier" : "Open Image in Editor",
                    AllowMultiple = false,
                    FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All]
                });

                if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
                {
                    await OpenImageFileAsync(path, taskSettings, job == WorkflowType.ImageBeautifier, taskManager);
                }

                return;
            }

            var window = CreateToolWindow(job, taskSettings);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += async (_, _) =>
            {
                await ImageEditorOptionsStore.PersistAsync();
                completion.TrySetResult();
            };
            if (owner != null)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }
            await completion.Task;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, $"Failed to open {job}");
        }
    }

    internal static Window CreateToolWindow(WorkflowType job, TaskSettings? taskSettings = null)
    {
        var tools = (taskSettings ?? SettingsManager.DefaultTaskSettings).ToolsSettingsReference;
        return job switch
        {
            WorkflowType.BackgroundRemover => new BackgroundRemoverWindow(PathsManager.ModelsFolder,
                tools.BackgroundRemoverOptions ??= new BackgroundRemoverOptions()),
            WorkflowType.ImageComparer => new ImageComparerWindow(),
            WorkflowType.IconConverter => new IconConverterWindow(),
            _ => throw new ArgumentOutOfRangeException(nameof(job), job, "Not an image editing tool window.")
        };
    }

    internal static async Task<bool> OpenImageFileAsync(string path, TaskSettings? taskSettings, bool openBackgroundPanel, IDesktopTaskManager taskManager)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap == null)
        {
            DebugHelper.WriteLine($"Cannot decode image for editor: {path}");
            return false;
        }

        var session = await PlatformServices.UI.ShowEditorSessionAsync(bitmap,
            ImageEditorOptionsStore.GetEditorOptions(taskSettings), sourceFilePath: path,
            openBackgroundPanel: openBackgroundPanel);
        session?.SourceImage?.Dispose();
        using var result = session?.RenderedImage;

        // Exit, Cancel, and the close button also return a session; only Continue runs the after-capture tasks.
        if (result != null && session!.TaskResult == MainViewModel.EditorTaskResult.Continue)
        {
            var sourceSettings = taskSettings ?? SettingsManager.DefaultTaskSettings;
            var executionSettings = WatchFolderManager.CloneTaskSettings(sourceSettings);
            executionSettings.WorkflowId = sourceSettings.WorkflowId;
            await taskManager.StartTask(executionSettings, result.Copy());
        }
        return true;
    }
}
