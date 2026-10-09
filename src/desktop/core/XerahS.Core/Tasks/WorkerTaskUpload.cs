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

using XerahS.Common;
using XerahS.Core.Helpers;
using XerahS.Platform.Abstractions;

namespace XerahS.Core.Tasks
{
    /// <summary>
    /// WorkerTask partial class for upload and clipboard operations.
    /// </summary>
    public partial class WorkerTask
    {
        internal bool TryLoadClipboardContent(TaskSettings taskSettings, TaskMetadata metadata, out string[]? clipboardFiles)
        {
            clipboardFiles = null;
            var clipboard = PlatformServices.Clipboard;
            if (clipboard == null)
            {
                return false;
            }

            var content = ClipboardContentHelper.ParseClipboard(clipboard);
            if (content == null)
            {
                return false;
            }

            switch (content.DataType)
            {
                case EDataType.Image:
                    metadata.Image = content.Image;
                    Info.DataType = EDataType.Image;
                    // As in ShareX's ProcessImageUpload: unless "Use after capture tasks for clipboard image
                    // uploads" is on, the image is only uploaded. Otherwise it runs as an image task, and its
                    // after capture tasks decide whether it is uploaded.
                    if (taskSettings.AdvancedSettings.ProcessImagesDuringClipboardUpload)
                    {
                        Info.Job = TaskJob.Job;
                    }
                    else
                    {
                        taskSettings.AfterCaptureJob = AfterCaptureTasks.None;
                        Info.Job = TaskJob.DataUpload; // Uploads the image from memory without saving a file.
                    }
                    string imageExtension = EnumExtensions.GetDescription(taskSettings.ImageSettings.ImageFormat);
                    Info.SetFileName(TaskHelpers.GetFileName(taskSettings, imageExtension, metadata));
                    return true;

                case EDataType.Text:
                    Info.TextContent = content.Text;
                    Info.DataType = EDataType.Text;
                    Info.Job = TaskJob.TextUpload;
                    ApplyClipboardURLJob(Info, taskSettings);
                    string textExtension = taskSettings.AdvancedSettings.TextFileExtension;
                    Info.SetFileName(TaskHelpers.GetFileName(taskSettings, textExtension, metadata));
                    if (Info.Job == TaskJob.TextUpload) ApplyClipboardTextJob(Info, taskSettings, content.Text!);
                    return true;

                case EDataType.File:
                    clipboardFiles = content.Files;
                    if (clipboardFiles == null || clipboardFiles.Length == 0)
                    {
                        return false;
                    }
                    Info.FilePath = clipboardFiles[0];
                    Info.DataType = EDataType.File;
                    Info.Job = TaskJob.FileUpload;
                    TaskHelpers.PrepareFileUpload(Info);
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The rest of ShareX's ProcessTextUpload, for copied text that is not handled as a URL: with "If clipboard
        /// contains a folder path, index and upload it", a folder path is indexed and the index is uploaded as text, named
        /// with the indexer's output format. Other text goes through the custom text template ("TextCustom").
        /// </summary>
        internal static void ApplyClipboardTextJob(TaskInfo info, TaskSettings taskSettings, string text)
        {
            if (taskSettings.UploadSettings.ClipboardUploadAutoIndexFolder && text.Length <= 260 && Directory.Exists(text))
            {
                var indexerSettings = taskSettings.ToolsSettingsReference.IndexerSettings ?? new XerahS.Indexer.IndexerSettings();
                DebugHelper.WriteLine($"Clipboard upload: indexing the copied folder path \"{text}\".");
                info.TextContent = XerahS.Indexer.Indexer.Index(text, indexerSettings);
                info.SetFileName(Path.ChangeExtension(info.FileName, GetIndexFolderExtension(indexerSettings.Output)));
                return;
            }

            info.TextContent = TaskHelpers.ApplyCustomText(text, taskSettings);
        }

        /// <summary>
        /// ShareX's ProcessTextUpload: copied text that is a URL is downloaded and uploaded, shortened,
        /// or shared, by the first of those clipboard upload options that is on. Otherwise it stays a text upload.
        /// </summary>
        internal static void ApplyClipboardURLJob(TaskInfo info, TaskSettings taskSettings)
        {
            string url = info.TextContent?.Trim() ?? string.Empty;
            if (!URLHelpers.IsValidURL(url)) return;

            var upload = taskSettings.UploadSettings;
            if (upload.ClipboardUploadURLContents)
            {
                info.DataType = EDataType.File;
                info.Job = TaskJob.DownloadUpload;
            }
            else if (upload.ClipboardUploadShortenURL)
            {
                info.DataType = EDataType.URL;
                info.Job = TaskJob.ShortenURL;
            }
            else if (upload.ClipboardUploadShareURL)
            {
                info.DataType = EDataType.URL;
                info.Job = TaskJob.ShareURL;
            }
            else
            {
                return;
            }

            info.TextContent = url;
        }

        /// <summary>
        /// Asks before uploading more than 10 files, unless "Don't show this message again" was ticked, as ShareX does.
        /// Set by the UI layer.
        /// </summary>
        public static Func<int, Task<bool>>? ConfirmMultiUploadCallback { get; set; }

        /// <summary>
        /// ShareX's ProcessFilesUpload: every copied file is uploaded by a task of its own, and the tasks all start at once;
        /// the simultaneous upload limit queues them. This task only starts them, so it has no result of its own.
        /// </summary>
        internal async Task UploadClipboardFilesAsync(TaskSettings taskSettings, string[] files, CancellationToken token)
        {
            Info.SuppressCompletionNotification = true;
            Info.FilePath = string.Empty;

            files = files.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).ToArray();
            if (files.Length == 0) return;
            if (ConfirmMultiUploadCallback is { } confirm && !await confirm(files.Length)) return;
            token.ThrowIfCancellationRequested();

            var uploads = new List<Task>(files.Length);
            foreach (string filePath in files)
            {
                var fileSettings = TaskSettings.GetSafeTaskSettings(taskSettings);
                fileSettings.Job = WorkflowType.FileUpload;
                uploads.Add(Helpers.TaskHelpers.TaskManagerService.StartFileTask(fileSettings, filePath));
            }

            await Task.WhenAll(uploads);
        }

        internal bool TryIndexFolder(TaskSettings taskSettings, out string? outputPath)
        {
            outputPath = null;

            if (taskSettings == null)
            {
                DebugHelper.WriteLine("IndexFolder: TaskSettings missing.");
                return false;
            }

            var toolsSettings = taskSettings.ToolsSettingsReference;
            string folderPath = toolsSettings.IndexerFolderPath;
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                DebugHelper.WriteLine($"IndexFolder: Folder path invalid: '{folderPath}'");
                return false;
            }

            try
            {
                var indexerSettings = toolsSettings.IndexerSettings ?? new XerahS.Indexer.IndexerSettings();

                string output = XerahS.Indexer.Indexer.Index(folderPath, indexerSettings);
                outputPath = WriteIndexOutput(taskSettings, folderPath, output, indexerSettings.Output);
                return !string.IsNullOrEmpty(outputPath);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "IndexFolder: indexing failed");
                return false;
            }
        }

        private static string WriteIndexOutput(TaskSettings taskSettings, string folderPath, string output, XerahS.Indexer.IndexerOutput outputType)
        {
            string extension = GetIndexFolderExtension(outputType);
            string screenshotsFolder = TaskHelpers.GetScreenshotsFolder(taskSettings);
            Directory.CreateDirectory(screenshotsFolder);

            string fileName = TaskHelpers.GetFileName(taskSettings, extension);
            string resolvedPath = TaskHelpers.HandleExistsFile(screenshotsFolder, fileName, taskSettings);
            if (string.IsNullOrWhiteSpace(resolvedPath)) return string.Empty;
            File.WriteAllText(resolvedPath, output);
            return resolvedPath;
        }

        private static string GetIndexFolderExtension(XerahS.Indexer.IndexerOutput outputType)
        {
            return outputType switch
            {
                XerahS.Indexer.IndexerOutput.Html => "html",
                XerahS.Indexer.IndexerOutput.Txt => "txt",
                XerahS.Indexer.IndexerOutput.Xml => "xml",
                XerahS.Indexer.IndexerOutput.Json => "json",
                _ => "txt"
            };
        }
    }
}
