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

using System.Text;
using XerahS.Common;
using XerahS.Core.Managers;
using XerahS.Platform.Abstractions;

namespace XerahS.Core.Tasks
{
    /// <summary>
    /// What ShareX's WorkerTask does when it starts. ShareX captures the screen, or reads the clipboard, before it
    /// creates the task; in XerahS that is part of the task, so these steps run after it: before the after capture
    /// tasks, the download, or the upload.
    /// </summary>
    internal static class TaskStart
    {
        /// <summary>
        /// Enters the queue when a task is created with its file, text, or image, so that such tasks start in the order
        /// they were created, as ShareX's queued tasks do. The task waits for the place when it starts.
        /// </summary>
        internal static void ReserveUploadQueuePlace(TaskInfo info, CancellationToken token)
        {
            if (info.UploadQueuePlace == null) info.UploadQueueEntry ??= UploadQueue.Instance.EnterAsync(token);
        }

        /// <summary>Waits for a place under the simultaneous upload limit. A task enters the queue once.</summary>
        internal static async Task WaitForUploadLimitAsync(TaskInfo info, CancellationToken token)
        {
            if (info.UploadQueuePlace != null) return;
            var entry = info.UploadQueueEntry ?? UploadQueue.Instance.EnterAsync(token);
            info.UploadQueueEntry = null;
            info.UploadQueuePlace = await entry.ConfigureAwait(false);
        }

        internal static void LeaveUploadQueue(TaskInfo info)
        {
            info.UploadQueuePlace?.Dispose();
            info.UploadQueuePlace = null;

            // A task that ended before it needed its reserved place frees it when the queue gives it.
            if (info.UploadQueueEntry is { } entry)
            {
                info.UploadQueueEntry = null;
                _ = entry.ContinueWith(reserved =>
                {
                    if (reserved.IsCompletedSuccessfully) reserved.Result.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// ShareX's CreateFileUploaderTask with "Process images during file upload": an image file is loaded and runs
        /// the after capture tasks like a screenshot, so it is uploaded only when "Upload image to host" is one of them.
        /// A file that cannot be read as an image is uploaded as a file.
        /// </summary>
        internal static void LoadImageFromFile(TaskInfo info)
        {
            if (!info.LoadImageFromFile) return;
            info.LoadImageFromFile = false;

            var image = ImageHelpers.LoadBitmap(info.FilePath);
            if (image == null)
            {
                DebugHelper.WriteLine($"Process images during file upload: \"{info.FilePath}\" could not be read as an image; uploading the file.");
                return;
            }

            info.Metadata.Image?.Dispose();
            info.Metadata.Image = image;
            info.DataType = EDataType.Image;
            info.Job = TaskJob.Job;
            info.ImageSourceFilePath = info.FilePath;
        }

        /// <summary>ShareX's "Automatically clear clipboard": a task that uploads clears the clipboard when it starts.</summary>
        internal static void ClearClipboardIfUploading(TaskInfo info)
        {
            if (info.ClipboardClearedOnStart) return;
            info.ClipboardClearedOnStart = true;
            if (!info.IsUploadJob || !info.TaskSettings.AdvancedSettings.AutoClearClipboard) return;

            try
            {
                PlatformServices.Clipboard.Clear();
                DebugHelper.WriteLine("Clipboard cleared at the start of the upload task.");
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Automatically clear clipboard");
            }
        }

        /// <summary>
        /// ShareX's DoTextJobs: with "Save text tasks as files" (on by default), the text of a text upload is also saved
        /// in the screenshots folder under the task's file name. The upload still sends the text itself.
        /// </summary>
        internal static async Task SaveTextAsFileAsync(TaskInfo info, CancellationToken token)
        {
            if (info.Job != TaskJob.TextUpload || string.IsNullOrEmpty(info.TextContent) ||
                !info.TaskSettings.AdvancedSettings.TextTaskSaveAsFile || !string.IsNullOrEmpty(info.FilePath))
            {
                return;
            }

            var settings = info.TaskSettings;
            string fileName = string.IsNullOrEmpty(info.FileName)
                ? TaskHelpers.GetFileName(settings, settings.AdvancedSettings.TextFileExtension, info.Metadata)
                : info.FileName;

            try
            {
                string folder = TaskHelpers.GetScreenshotsFolder(settings, info.Metadata);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, fileName);
                if (File.Exists(path) && settings.ImageSettings.FileExistAction == FileExistAction.Ask)
                {
                    var resolution = PlatformServices.IsUIServiceInitialized
                        ? await PlatformServices.UI.ResolveFileConflictAsync(path, token).ConfigureAwait(false) : null;
                    token.ThrowIfCancellationRequested();
                    if (resolution == null) return;
                    path = resolution.FilePath;
                }
                else
                {
                    path = TaskHelpers.HandleExistsFile(path, settings);
                    if (string.IsNullOrEmpty(path)) return;
                }

                // Without a byte order mark (ShareX writes one), so the file has the same bytes as the upload.
                await File.WriteAllTextAsync(path, info.TextContent, new UTF8Encoding(false), token).ConfigureAwait(false);
                info.FilePath = path;
                DebugHelper.WriteLine("Text saved to file: " + path);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // The text is still uploaded, as a screenshot is when saving it fails.
                DebugHelper.WriteException(ex, "Save text task as file");
            }
        }
    }
}
