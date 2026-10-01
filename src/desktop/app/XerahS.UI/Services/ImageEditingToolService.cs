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
using XerahS.UI.Views;

namespace XerahS.UI.Services;

internal static class ImageEditingToolService
{
    public static async Task HandleWorkflowAsync(WorkflowType job, Window? owner, TaskSettings? taskSettings, IDesktopTaskManager taskManager)
    {
        try
        {
            if (job is WorkflowType.ImageEditor or WorkflowType.ImageBeautifier or WorkflowType.ImageEffects)
            {
                var storageProvider = StorageProviderResolver.Resolve(owner);
                if (storageProvider == null)
                {
                    return;
                }

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = job switch
                    {
                        WorkflowType.ImageBeautifier => "Open Image in Beautifier",
                        WorkflowType.ImageEffects => "Open Image for Effects",
                        _ => "Open Image in Editor"
                    },
                    AllowMultiple = false,
                    FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All]
                });

                if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
                {
                    bool opened = job == WorkflowType.ImageEffects
                        ? await OpenImageEffectsAsync(path, taskSettings, taskManager)
                        : await OpenImageFileAsync(path, taskSettings, job == WorkflowType.ImageBeautifier, taskManager);
                    if (!opened)
                    {
                        throw new IOException("The selected image could not be opened. Check that it exists and uses a supported image format.");
                    }
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
            UploadWorkflowService.ReportError(ex, $"Could not open {EnumExtensions.GetDescription(job)}");
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
            var executionSettings = TaskSettings.GetSafeTaskSettings(sourceSettings);
            await taskManager.StartTask(executionSettings, result.Copy());
        }
        return true;
    }

    /// <summary>
    /// ShareX's Image effects job: the task's image effect preset in the preset editor, previewed on the image.
    /// Preset changes are written to the saved workflow (ShareX edits ImageSettingsReference) and saved when
    /// the window closes. Returns when the window closes, or false if the image cannot be opened.
    /// </summary>
    internal static async Task<bool> OpenImageEffectsAsync(string path, TaskSettings? taskSettings, IDesktopTaskManager taskManager)
    {
        using var bitmap = File.Exists(path) ? SKBitmap.Decode(path) : null;
        if (bitmap == null)
        {
            DebugHelper.WriteLine($"Cannot decode image for image effects: {path}");
            return false;
        }

        var window = CreateImageEffectsWindow(bitmap, path, taskSettings, taskManager);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += async (_, _) =>
        {
            await ImageEditorOptionsStore.PersistAsync();
            completion.TrySetResult();
        };
        window.Show();
        await completion.Task;
        return true;
    }

    internal static ImageEffectsToolWindow CreateImageEffectsWindow(SKBitmap image, string? path, TaskSettings? taskSettings,
        IDesktopTaskManager taskManager)
    {
        var sourceSettings = taskSettings ?? SettingsManager.DefaultTaskSettings;
        var savedSettings = SettingsManager.GetWorkflowById(sourceSettings.WorkflowId ?? string.Empty)?.TaskSettings ?? sourceSettings;
        var viewModel = UiViewModelFactoryAccessor.GetRequired()
            .CreateImageEffectsViewModel(savedSettings.ImageSettings ??= new TaskSettingsImage());
        return new ImageEffectsToolWindow(viewModel, image, path,
            result => UploadImageEffectsResultAsync(result, sourceSettings, taskManager));
    }

    /// <summary>
    /// ShareX's Upload runs the image task (UploadManager.RunImageTask) with the workflow's after-capture tasks.
    /// "Upload image to host" is added for this run so the button always uploads, even when the workflow's
    /// after-capture tasks leave it out (ShareX's defaults include it). The preset is already applied, so
    /// "Add image effects" is skipped instead of applying it a second time.
    /// </summary>
    internal static Task UploadImageEffectsResultAsync(SKBitmap result, TaskSettings sourceSettings, IDesktopTaskManager taskManager)
    {
        var executionSettings = TaskSettings.GetSafeTaskSettings(sourceSettings);
        executionSettings.AfterCaptureJob = (executionSettings.AfterCaptureJob & ~AfterCaptureTasks.AddImageEffects)
            | AfterCaptureTasks.UploadImageToHost;
        return taskManager.StartTask(executionSettings, result.Copy());
    }
}
