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
using XerahS.Core.SendTo;
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
                    if (UploadsClipboardFilesSeparately(clipboardFiles))
                    {
                        return true;
                    }
                    Info.FilePath = clipboardFiles[0];
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
                DebugHelper.WriteLine($"Clipboard upload: indexing the copied folder path \"{text}\".");
                SetFolderIndex(info, taskSettings, text);
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

        /// <summary>A single copied file is uploaded by the clipboard task itself; more files, or a folder, by tasks of their own.</summary>
        internal static bool UploadsClipboardFilesSeparately(string[] paths) => paths.Length > 1 || !File.Exists(paths[0]);

        /// <summary>
        /// ShareX's ProcessFilesUpload: every copied file is uploaded by a task of its own, and so is every file in a copied
        /// folder and its subfolders. The tasks all start at once; the simultaneous upload limit queues them. This task only
        /// starts them, so it has no result of its own.
        /// </summary>
        internal async Task UploadClipboardFilesAsync(TaskSettings taskSettings, string[] paths, CancellationToken token)
        {
            Info.SuppressCompletionNotification = true;
            Info.FilePath = string.Empty;

            var resolved = await Task.Run(() => SendToPolicyResolver.ResolveFiles(
                SendToSelectionClassifier.Create(paths.Where(File.Exists), paths.Where(Directory.Exists)),
                SendToFolderPolicy.IncludeFilesRecursively), token);
            if (resolved.FailedFolderCount > 0)
            {
                DebugHelper.WriteLine($"Upload from clipboard: {resolved.FailedFolderCount} copied folder(s) could not be read.");
            }
            var files = resolved.FilePaths;
            if (files.Count == 0) return;
            // As in XerahS's folder and drop uploads, more than 10 files ask first, once for all of them.
            if (ConfirmMultiUploadCallback is { } confirm && !await confirm(files.Count)) return;
            token.ThrowIfCancellationRequested();

            var uploads = new List<Task>(files.Count);
            foreach (string filePath in files)
            {
                var fileSettings = TaskSettings.GetSafeTaskSettings(taskSettings);
                fileSettings.Job = WorkflowType.FileUpload;
                uploads.Add(Helpers.TaskHelpers.TaskManagerService.StartFileTask(fileSettings, filePath));
            }

            await Task.WhenAll(uploads);
        }

        /// <summary>
        /// ShareX's IndexFolder for the Index folder workflow: the folder in the workflow's Index folder settings is indexed,
        /// and the index is uploaded as text, named with the name pattern and the indexer output's extension. As for other
        /// text, "Save text tasks as files" also saves it.
        /// </summary>
        internal bool TryIndexFolder(TaskSettings taskSettings)
        {
            string folderPath = taskSettings.ToolsSettingsReference.IndexerFolderPath;
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                DebugHelper.WriteLine($"IndexFolder: Folder path invalid: '{folderPath}'");
                return false;
            }

            try
            {
                Info.SetFileName(TaskHelpers.GetFileName(taskSettings, taskSettings.AdvancedSettings.TextFileExtension, Info.Metadata));
                SetFolderIndex(Info, taskSettings, folderPath);
                return true;
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "IndexFolder: indexing failed");
                return false;
            }
        }

        /// <summary>The folder's index becomes the task's text, and the file name gets the indexer output's extension.</summary>
        private static void SetFolderIndex(TaskInfo info, TaskSettings taskSettings, string folderPath)
        {
            var indexerSettings = taskSettings.ToolsSettingsReference.IndexerSettings ?? new XerahS.Indexer.IndexerSettings();
            info.TextContent = XerahS.Indexer.Indexer.Index(folderPath, indexerSettings);
            info.DataType = EDataType.Text;
            info.Job = TaskJob.TextUpload;
            info.SetFileName(Path.ChangeExtension(info.FileName, GetIndexFolderExtension(indexerSettings.Output)));
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
