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

using SkiaSharp;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using XerahS.Services.Abstractions;

namespace XerahS.Core.Tasks.Processors;

/// <summary>File tasks run after image processing and before the upload, as in ShareX.</summary>
public static class AfterCaptureFileTasks
{
    public static async Task ProcessAsync(TaskInfo info, CancellationToken token)
    {
        if (string.IsNullOrEmpty(info.FilePath) || !File.Exists(info.FilePath)) return;
        var settings = info.TaskSettings;
        if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.PerformActions))
        {
            // Actions keep a pending input deletion path. Never share that state between captures.
            foreach (var configured in (settings.ExternalPrograms ?? []).Where(action => action.IsActive))
            {
                token.ThrowIfCancellationRequested();
                var action = configured.Clone();
                string? output = await action.RunAsync(info.FilePath, token);
                string resultPath = string.IsNullOrEmpty(output) ? info.FilePath : output;
                if (!File.Exists(resultPath))
                    throw new IOException($"Action result file does not exist: {resultPath}");
                if (!string.IsNullOrEmpty(output))
                {
                    info.FilePath = output;
                    action.DeletePendingInputFile();
                }
            }
        }

        token.ThrowIfCancellationRequested();
        try
        {
            if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFileToClipboard))
                PlatformServices.Clipboard.SetFileDropList([info.FilePath]);
            else if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFilePathToClipboard))
                PlatformServices.Clipboard.SetText(info.FilePath);
            else if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFolderPathToClipboard))
                PlatformServices.Clipboard.SetText(Path.GetDirectoryName(info.FilePath) ?? string.Empty);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Copy file information to clipboard");
        }

        if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowInExplorer))
        {
            try { PlatformServices.System.ShowFileInExplorer(info.FilePath); }
            catch (Exception ex) { DebugHelper.WriteException(ex, "Show in file manager"); }
        }
    }

    public static async Task<string?> SaveThumbnailAsync(TaskInfo info, CancellationToken token = default)
    {
        SKBitmap? image = info.Metadata.Image;
        var settings = info.TaskSettings;
        var options = settings.ImageSettings;
        if (image == null || (options.ThumbnailWidth <= 0 && options.ThumbnailHeight <= 0) ||
            (options.ThumbnailCheckSize && (image.Width <= options.ThumbnailWidth || image.Height <= options.ThumbnailHeight)))
            return null;

        string fileName = string.IsNullOrEmpty(info.FilePath) ? info.FileName : Path.GetFileName(info.FilePath);
        string folder = string.IsNullOrEmpty(info.FilePath)
            ? TaskHelpers.GetScreenshotsFolder(settings) : Path.GetDirectoryName(info.FilePath)!;
        string thumbnailName = Path.GetFileNameWithoutExtension(fileName) + options.ThumbnailName + ".jpg";
        // A thumbnail suffix is a filename, not a path, and must never overwrite the main image.
        thumbnailName = Path.GetFileName(thumbnailName);
        string path = Path.Combine(folder, thumbnailName);
        bool overwrite = settings.ImageSettings.FileExistAction == FileExistAction.Overwrite;
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(info.FilePath.Length > 0 ? info.FilePath : Path.Combine(folder, fileName)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            path = FileHelpers.GetUniqueFilePath(Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + " (1).jpg"));
            overwrite = false;
        }
        if (File.Exists(path) && settings.ImageSettings.FileExistAction == FileExistAction.Ask)
        {
            var choice = await PlatformServices.UI.ResolveFileConflictAsync(path, token);
            if (choice == null) return null;
            path = choice.FilePath;
            overwrite = choice.Overwrite;
        }
        else path = TaskHelpers.HandleExistsFile(path, settings);
        if (string.IsNullOrEmpty(path)) return null;
        token.ThrowIfCancellationRequested();

        using var resized = ImageHelpers.ResizeImage(image, options.ThumbnailWidth, options.ThumbnailHeight);
        using var opaque = new SKBitmap(resized.Width, resized.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(opaque))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(resized, 0, 0, new SKSamplingOptions());
        }
        using var encoded = TaskHelpers.SaveImageAsStream(opaque, EImageFormat.JPEG, jpegQuality: 90)
            ?? throw new IOException("The thumbnail could not be encoded.");
        TaskHelpers.WriteImageStreamToFile(encoded, path, overwrite);
        return path;
    }

    public static void DeleteFile(TaskInfo info)
    {
        if (!info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.DeleteFile) || string.IsNullOrEmpty(info.FilePath)) return;
        if (info.Job != TaskJob.Job && !(info.Job == TaskJob.FileUpload && info.TaskSettings.AdvancedSettings.UseAfterCaptureTasksDuringFileUpload)) return;
        if (File.Exists(info.FilePath)) File.Delete(info.FilePath);
    }
}
