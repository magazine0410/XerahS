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

using ShareX.ImageEditor.Core.ImageEffects;
using System;
using XerahS.Common;
using XerahS.Services.Abstractions;

namespace XerahS.Core;

/// <summary>
/// Task-related helper methods for file naming, folder management, and image processing.
/// Extracted from ShareX TaskHelpers - contains only pure logic (no UI dependencies).
/// </summary>
public static partial class TaskHelpers
{
    #region Job Type Classification

    /// <summary>
    /// Media type classification for jobs
    /// </summary>
    public enum JobMediaType
    {
        None,
        Image,
        Video,
        Text,
        File,
        Tool,
        System
    }

    /// <summary>
    /// Get the media type for a given WorkflowType job based on its category
    /// </summary>
    public static JobMediaType GetJobMediaType(WorkflowType job)
    {
        string category = job.GetHotkeyCategory();

        return category switch
        {
            EnumExtensions.WorkflowType_Category_ScreenCapture => GetMediaTypeForScreenCapture(job),
            EnumExtensions.WorkflowType_Category_ScreenRecord => JobMediaType.Video,
            EnumExtensions.WorkflowType_Category_Upload => GetMediaTypeForUpload(job),
            EnumExtensions.WorkflowType_Category_Tools => GetMediaTypeForTools(job),
            EnumExtensions.WorkflowType_Category_Other => JobMediaType.System,
            _ => JobMediaType.None
        };
    }

    /// <summary>
    /// Determine media type for screen capture jobs
    /// </summary>
    private static JobMediaType GetMediaTypeForScreenCapture(WorkflowType job)
    {
        // All screen capture jobs produce images
        return JobMediaType.Image;
    }

    /// <summary>
    /// Determine media type for upload jobs
    /// </summary>
    private static JobMediaType GetMediaTypeForUpload(WorkflowType job)
    {
        return job switch
        {
            WorkflowType.FileUpload or
            WorkflowType.UploadURL or
            WorkflowType.FolderUpload or
            WorkflowType.DragDropUpload or
            WorkflowType.ClipboardUpload or
            WorkflowType.ClipboardUploadWithContentViewer => JobMediaType.File,
            WorkflowType.UploadText or WorkflowType.ShortenURL => JobMediaType.Text,
            WorkflowType.StopUploads => JobMediaType.System,
            _ => JobMediaType.None
        };
    }

    /// <summary>
    /// Determine media type for tool jobs
    /// </summary>
    private static JobMediaType GetMediaTypeForTools(WorkflowType job)
    {
        return job switch
        {
            // Image-specific tools
            WorkflowType.MouseHighlighter or WorkflowType.ActiveWindowTopMost or WorkflowType.ActiveWindowBorderless or WorkflowType.InspectWindow or WorkflowType.BorderlessWindow or WorkflowType.Metadata or WorkflowType.StripMetadata => JobMediaType.System,
            WorkflowType.ImageEditor or
            WorkflowType.ImageBeautifier or
            WorkflowType.ImageEffects or
            WorkflowType.ImageViewer or
            WorkflowType.BackgroundRemover or
            WorkflowType.ImageComparer or
            WorkflowType.IconConverter or
            WorkflowType.ImageCombiner or
            WorkflowType.ImageSplitter or
            WorkflowType.ImageThumbnailer or
            WorkflowType.AnalyzeImage => JobMediaType.Image,

            // Video-specific tools
            WorkflowType.VideoConverter or
            WorkflowType.VideoTrimmer or
            WorkflowType.VideoThumbnailer => JobMediaType.Video,

            // Text-specific tools
            WorkflowType.OCR => JobMediaType.Text,

            // All other tools are utility tools
            _ => JobMediaType.Tool
        };
    }

    /// <summary>
    /// Get the media type for a TaskSettings based on its Job property
    /// </summary>
    public static JobMediaType GetJobMediaType(TaskSettings taskSettings)
    {
        return GetJobMediaType(taskSettings.Job);
    }

    /// <summary>
    /// Determine if a screen capture workflow should delay before capturing.
    /// </summary>
    public static bool IsScreenCaptureStartJob(WorkflowType job)
    {
        return job switch
        {
            WorkflowType.PrintScreen or
            WorkflowType.ActiveWindow or
            WorkflowType.CustomWindow or
            WorkflowType.ActiveMonitor or
            WorkflowType.RectangleRegion or
            WorkflowType.RectangleTransparent or
            WorkflowType.CustomRegion or
            WorkflowType.LastRegion or
            WorkflowType.ScrollingCapture or
            WorkflowType.AutoCapture or
            WorkflowType.StartAutoCapture => true,
            _ => false
        };
    }

    /// <summary>
    /// Determine if a screen recording workflow should delay before recording starts.
    /// </summary>
    public static bool IsScreenRecordStartJob(WorkflowType job)
    {
        return job switch
        {
            WorkflowType.ScreenRecorder or
            WorkflowType.StartScreenRecorder or
            WorkflowType.ScreenRecorderActiveWindow or
            WorkflowType.ScreenRecorderCustomRegion or
            WorkflowType.ScreenRecorderGIF or
            WorkflowType.ScreenRecorderGIFActiveWindow or
            WorkflowType.ScreenRecorderGIFCustomRegion or
            WorkflowType.StartScreenRecorderGIF => true,
            _ => false
        };
    }

    /// <summary>
    /// Resolve the capture start delay (in seconds) for a task and return the workflow category.
    /// </summary>
    public static double GetCaptureStartDelaySeconds(TaskSettings taskSettings, out string category)
    {
        if (taskSettings == null)
        {
            throw new ArgumentNullException(nameof(taskSettings));
        }

        category = taskSettings.Job.GetHotkeyCategory();
        var captureSettings = taskSettings.CaptureSettings ?? new TaskSettingsCapture();

        if (category == EnumExtensions.WorkflowType_Category_ScreenCapture && IsScreenCaptureStartJob(taskSettings.Job))
        {
            return (double)captureSettings.ScreenshotDelay;
        }

        if (category == EnumExtensions.WorkflowType_Category_ScreenRecord && IsScreenRecordStartJob(taskSettings.Job))
        {
            return 0; // The recording session owns its countdown and manual Start control.
        }

        return 0;
    }

    /// <summary>
    /// Convert a capture start delay from seconds to milliseconds for Task.Delay without overflow or invalid values.
    /// </summary>
    public static int GetCaptureStartDelayMilliseconds(double delaySeconds)
    {
        if (double.IsNaN(delaySeconds) || double.IsInfinity(delaySeconds) || delaySeconds <= 0)
        {
            return 0;
        }

        var delayMilliseconds = Math.Round(delaySeconds * 1000d, MidpointRounding.AwayFromZero);

        if (delayMilliseconds >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Max(0, (int)delayMilliseconds);
    }

    /// <summary>
    /// Check if a job is image-related
    /// </summary>
    public static bool IsImageJob(WorkflowType job)
    {
        return GetJobMediaType(job) == JobMediaType.Image;
    }

    /// <summary>
    /// Check if a job is video-related
    /// </summary>
    public static bool IsVideoJob(WorkflowType job)
    {
        return GetJobMediaType(job) == JobMediaType.Video;
    }

    /// <summary>
    /// Check if a job is text-related
    /// </summary>
    public static bool IsTextJob(WorkflowType job)
    {
        return GetJobMediaType(job) == JobMediaType.Text;
    }

    /// <summary>
    /// Check if a job is file-related (mixed content)
    /// </summary>
    public static bool IsFileJob(WorkflowType job)
    {
        return GetJobMediaType(job) == JobMediaType.File;
    }

    /// <summary>
    /// Check if a job is a tool
    /// </summary>
    public static bool IsToolJob(WorkflowType job)
    {
        return GetJobMediaType(job) == JobMediaType.Tool;
    }

    /// <summary>
    /// Check if a job is a system/control action
    /// </summary>
    public static bool IsSystemJob(WorkflowType job)
    {
        return GetJobMediaType(job) == JobMediaType.System;
    }

    /// <summary>
    /// Check if a job produces media output (image or video)
    /// </summary>
    public static bool IsMediaProducingJob(WorkflowType job)
    {
        var mediaType = GetJobMediaType(job);
        return mediaType == JobMediaType.Image || mediaType == JobMediaType.Video;
    }

    /// <summary>
    /// Check if TaskSettings is configured for image capture
    /// </summary>
    public static bool IsImageJob(TaskSettings taskSettings)
    {
        return IsImageJob(taskSettings.Job);
    }

    /// <summary>
    /// Check if TaskSettings is configured for video recording
    /// </summary>
    public static bool IsVideoJob(TaskSettings taskSettings)
    {
        return IsVideoJob(taskSettings.Job);
    }

    #endregion

    #region File Naming

    /// <summary>
    /// Generate a file name for a captured image
    /// </summary>
    public static string GetFileName(TaskSettings taskSettings, string extension, SkiaSharp.SKBitmap? bmp = null)
    {
        var metadata = bmp != null ? new TaskMetadata(bmp) : new TaskMetadata();
        return GetFileName(taskSettings, extension, metadata);
    }

    /// <summary>
    /// ShareX's file upload naming: an uploaded file keeps its own name, unless "Use name pattern for file uploaders" is
    /// on; then it is named by the name pattern, with the file's extension.
    /// </summary>
    public static void ApplyFileUploadName(TaskInfo info)
    {
        if (info.TaskSettings.UploadSettings.FileUploadUseNamePattern && !string.IsNullOrEmpty(info.FilePath))
        {
            info.SetFileName(GetFileName(info.TaskSettings, FileHelpers.GetFileNameExtension(info.FilePath), info.Metadata));
        }
    }

    /// <summary>
    /// ShareX's CreateFileUploaderTask: an uploaded file gets the upload name, and with "Process images during file upload"
    /// an image file is loaded when the task starts, to run the after capture tasks on it.
    /// </summary>
    public static void PrepareFileUpload(TaskInfo info)
    {
        ApplyFileUploadName(info);
        var advanced = info.TaskSettings.AdvancedSettings;
        info.LoadImageFromFile = advanced.ProcessImagesDuringFileUpload && !string.IsNullOrEmpty(info.FilePath) &&
            FileHelpers.CheckExtension(info.FilePath, advanced.ImageExtensions);
    }

    /// <summary>
    /// ShareX's upload buffer size: 2 to the power of "BufferSizePower" kibibytes, from 1 KiB to 8 MiB (32 KiB by default).
    /// </summary>
    public static int GetUploadBufferSize() => GetUploadBufferSize(SettingsManager.Settings?.BufferSizePower ?? 5);

    public static int GetUploadBufferSize(int bufferSizePower) => 1024 << Math.Clamp(bufferSizePower, 0, MaxBufferSizePower);

    /// <summary>The largest buffer size in ShareX's list, 8 MiB.</summary>
    public const int MaxBufferSizePower = 13;

    /// <summary>
    /// ShareX's custom text for text uploaded from the clipboard or dropped on the drag and drop upload window: the
    /// "TextCustom" advanced setting with %input replaced by the text, HTML-encoded first when "TextCustomEncodeInput"
    /// is on. Without a template the text is unchanged.
    /// </summary>
    public static string ApplyCustomText(string text, TaskSettings settings)
    {
        string? template = settings.AdvancedSettings?.TextCustom;
        if (string.IsNullOrEmpty(template)) return text;
        if (settings.AdvancedSettings!.TextCustomEncodeInput) text = System.Web.HttpUtility.HtmlEncode(text);
        return template.Replace("%input", text);
    }

    /// <summary>
    /// ShareX's last step before an upload: bidirectional control characters are removed from the name, and with
    /// "Replace potentially problematic characters" on, other characters that are not safe in a URL become underscores.
    /// </summary>
    public static string GetUploadFileName(string fileName, TaskSettings taskSettings)
    {
        fileName = URLHelpers.RemoveBidiControlCharacters(fileName);

        if (taskSettings.UploadSettings.FileUploadReplaceProblematicCharacters)
        {
            fileName = URLHelpers.ReplaceReservedCharacters(fileName, "_");
        }

        return fileName;
    }

    /// <summary>
    /// Generate a file name with metadata
    /// </summary>
    public static string GetFileName(TaskSettings taskSettings, string extension, TaskMetadata? metadata)
    {
        var settings = SettingsManager.Settings;
        string pattern;

        // Use window-specific pattern if available
        if (!string.IsNullOrWhiteSpace(settings.SaveImageSubFolderPatternWindow) &&
            !string.IsNullOrWhiteSpace(metadata?.WindowTitle))
        {
            pattern = taskSettings.UploadSettings.NameFormatPatternActiveWindow;
        }
        else
        {
            pattern = taskSettings.UploadSettings.NameFormatPattern;
        }

        var nameParser = new NameParser(NameParserType.FileName)
        {
            AutoIncrementNumber = settings.NameParserAutoIncrementNumber,
            MaxNameLength = taskSettings.AdvancedSettings.NamePatternMaxLength,
            MaxTitleLength = taskSettings.AdvancedSettings.NamePatternMaxTitleLength,
            WindowText = metadata?.WindowTitle ?? "",
            ProcessName = metadata?.ProcessName ?? ""
        };

        if (metadata?.Image != null)
        {
            nameParser.ImageWidth = metadata.Image.Width;
            nameParser.ImageHeight = metadata.Image.Height;
        }

        // Use custom timezone if configured
        if (taskSettings.UploadSettings.UseCustomTimeZone)
        {
            nameParser.CustomTimeZone = taskSettings.UploadSettings.CustomTimeZone;
        }

        string fileName = nameParser.Parse(pattern);

        if (!string.IsNullOrEmpty(extension))
        {
            fileName += "." + extension.TrimStart('.');
        }

        // Update auto-increment counter (if NameParser supports it)
        // TODO: Add auto-increment tracking when NameParser is enhanced

        return fileName;
    }

    /// <summary>
    /// Resolve a safe history file name using known file info or URL.
    /// </summary>
    public static string GetHistoryFileName(string fileName, string? filePath, string? url)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            // fileName may be a local path on a different platform; reduce to basename
            // to avoid leaking path segments across platforms in history display.
            string basename = fileName;
            int lastSlash = Math.Max(basename.LastIndexOf('/'), basename.LastIndexOf('\\'));
            if (lastSlash >= 0)
            {
                basename = basename.Substring(lastSlash + 1);
            }

            return !string.IsNullOrWhiteSpace(basename) ? basename : fileName;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            string basename = Path.GetFileName(filePath);
            // Path.GetFileName only splits on the platform separator; on non-Windows
            // a Windows-style path would return the full string. Apply multi-separator
            // cleanup so history never stores cross-platform path fragments.
            int lastSlash = Math.Max(basename.LastIndexOf('/'), basename.LastIndexOf('\\'));
            if (lastSlash >= 0)
            {
                basename = basename.Substring(lastSlash + 1);
            }
            return !string.IsNullOrWhiteSpace(basename) ? basename : filePath;
        }

        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var segment = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(segment))
            {
                return segment;
            }

            if (!string.IsNullOrWhiteSpace(uri.Host))
            {
                return uri.Host;
            }
        }

        return "URL";
    }

    #endregion

    #region Screenshots Folder

    /// <summary>
    /// Get the screenshots folder path based on settings and metadata
    /// </summary>
    public static string GetScreenshotsFolder(TaskSettings? taskSettings = null, TaskMetadata? metadata = null)
    {
        var settings = SettingsManager.Settings;
        string screenshotsFolder;

        var nameParser = new NameParser(NameParserType.FilePath);

        if (metadata != null)
        {
            if (metadata.Image != null)
            {
                nameParser.ImageWidth = metadata.Image.Width;
                nameParser.ImageHeight = metadata.Image.Height;
            }

            nameParser.WindowText = metadata.WindowTitle ?? "";
            nameParser.ProcessName = metadata.ProcessName ?? "";
        }

        if (taskSettings != null && taskSettings.OverrideScreenshotsFolder &&
            !string.IsNullOrEmpty(taskSettings.ScreenshotsFolder))
        {
            screenshotsFolder = nameParser.Parse(taskSettings.ScreenshotsFolder);
        }
        else if (!settings.UseSaveImageSubFolderPattern)
        {
            // Subfolder creation disabled - write directly into the screenshots parent folder.
            screenshotsFolder = GetScreenshotsParentFolder(taskSettings);
        }
        else
        {
            string subFolderPattern;

            if (!string.IsNullOrWhiteSpace(settings.SaveImageSubFolderPatternWindow) &&
                !string.IsNullOrWhiteSpace(nameParser.WindowText))
            {
                subFolderPattern = settings.SaveImageSubFolderPatternWindow;
            }
            else
            {
                subFolderPattern = settings.SaveImageSubFolderPattern;
            }

            string subFolderPath = nameParser.Parse(subFolderPattern);
            screenshotsFolder = Path.Combine(GetScreenshotsParentFolder(taskSettings), subFolderPath);
        }

        return FileHelpers.GetAbsolutePath(screenshotsFolder);
    }

    /// <summary>
    /// Get the parent folder for screenshots or screencasts based on job type
    /// </summary>
    public static string GetScreenshotsParentFolder(TaskSettings? taskSettings = null)
    {
        var settings = SettingsManager.Settings;

        // Check if custom path is configured
        if (settings.UseCustomScreenshotsPath)
        {
            string primary = settings.CustomScreenshotsPath;
            string secondary = settings.CustomScreenshotsPath2;
            if (!string.IsNullOrWhiteSpace(primary))
            {
                primary = FileHelpers.GetAbsolutePath(primary);
                if (string.IsNullOrWhiteSpace(secondary) || Directory.Exists(primary)) return primary;
            }
            if (!string.IsNullOrWhiteSpace(secondary))
            {
                secondary = FileHelpers.GetAbsolutePath(secondary);
                if (Directory.Exists(secondary)) return secondary;
            }
        }

        // Determine folder based on job category
        if (taskSettings != null)
        {
            string category = taskSettings.Job.GetHotkeyCategory();
            if (category == EnumExtensions.WorkflowType_Category_ScreenRecord)
            {
                return XerahS.Common.PathsManager.ScreencastsFolder;
            }
        }

        // Default to Screenshots folder
        return XerahS.Common.PathsManager.ScreenshotsFolder;
    }

    #endregion

    #region File Exists Handling

    /// <summary>
    /// Handle file exists scenario based on settings
    /// </summary>
    public static string HandleExistsFile(string folder, string fileName, TaskSettings taskSettings)
    {
        string filePath = Path.Combine(folder, fileName);
        return HandleExistsFile(filePath, taskSettings);
    }

    /// <summary>
    /// Handle file exists scenario - returns final file path
    /// </summary>
    public static string HandleExistsFile(string filePath, TaskSettings taskSettings)
    {
        if (File.Exists(filePath))
        {
            switch (taskSettings.ImageSettings.FileExistAction)
            {
                case FileExistAction.Overwrite:
                    return filePath;

                case FileExistAction.UniqueName:
                    return FileHelpers.GetUniqueFilePath(filePath);

                case FileExistAction.Cancel:
                    return "";

                case FileExistAction.Ask:
                    // Ask requires UI interaction - return empty to signal caller
                    DebugHelper.WriteLine($"File exists, action is Ask: {filePath}");
                    return string.Empty;

                default:
                    DebugHelper.WriteLine($"Unknown FileExistAction, using unique name for: {filePath}");
                    return FileHelpers.GetUniqueFilePath(filePath);
            }
        }

        return filePath;
    }

    #endregion

    #region Image Processing

    /// <summary>
    /// Save image to stream with specified format
    /// </summary>
    public static PreparedImage PrepareImage(SkiaSharp.SKBitmap bmp, TaskSettings settings)
    {
        if (settings.ImageSettings.ImageFormat == EImageFormat.AVIF)
            throw new NotSupportedException("AVIF preparation requires PrepareImageAsync.");
        var stream = SaveImageAsStream(bmp, settings.ImageSettings.ImageFormat, settings)
            ?? throw new NotSupportedException($"The configured {settings.ImageSettings.ImageFormat} image format could not be encoded.");
        return ConvertPreparedImage(bmp, stream, settings);
    }

    public static async Task<PreparedImage> PrepareImageAsync(SkiaSharp.SKBitmap bmp, TaskSettings settings)
    {
        if (settings.ImageSettings.ImageFormat != EImageFormat.AVIF) return PrepareImage(bmp, settings);
        string path = Path.Combine(Path.GetTempPath(), $"xerahs-{Guid.NewGuid():N}.avif");
        try
        {
            await XerahS.Platform.Abstractions.PlatformServices.ImageEncoder.EncodeAsync(bmp, path,
                EImageFormat.AVIF, settings.ImageSettings.ImageJPEGQuality).ConfigureAwait(false);
            var stream = new MemoryStream(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
            return ConvertPreparedImage(bmp, stream, settings);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static PreparedImage ConvertPreparedImage(SkiaSharp.SKBitmap bmp, MemoryStream stream, TaskSettings settings)
    {
        var image = settings.ImageSettings;
        long sizeLimit = Math.Max(0L, image.ImageAutoUseJPEGSize) * 1000;
        if (!image.ImageAutoUseJPEG || image.ImageFormat == EImageFormat.JPEG || stream.Length <= sizeLimit)
            return new PreparedImage(stream, image.ImageFormat);
        stream.Dispose();
        // ShareX starts at quality 100 and decrements by two, stopping at 70 even if still oversized.
        int quality = image.ImageAutoJPEGQuality ? 100 : image.ImageJPEGQuality;
        while (true)
        {
            var jpeg = SaveImageAsStream(bmp, EImageFormat.JPEG, jpegQuality: quality)
                ?? throw new InvalidOperationException("Could not encode JPEG image.");
            if (!image.ImageAutoJPEGQuality || jpeg.Length <= sizeLimit || quality <= 70)
                return new PreparedImage(jpeg, EImageFormat.JPEG);
            jpeg.Dispose();
            quality -= 2;
        }
    }

    public static MemoryStream? SaveImageAsStream(SkiaSharp.SKBitmap bmp, EImageFormat imageFormat, TaskSettings taskSettings)
    {
        return SaveImageAsStream(bmp, imageFormat,
            taskSettings.ImageSettings.ImagePNGBitDepth,
            taskSettings.ImageSettings.ImageJPEGQuality,
            taskSettings.ImageSettings.ImageGIFQuality);
    }

    /// <summary>
    /// Save image to a new MemoryStream with specified format.
    /// </summary>
    /// <param name="bmp">The bitmap to encode</param>
    /// <param name="imageFormat">The image format to use</param>
    /// <param name="pngBitDepth">PNG bit depth (when using PNG format)</param>
    /// <param name="jpegQuality">JPEG quality 0-100 (when using JPEG format)</param>
    /// <param name="gifQuality">GIF quality setting (when using GIF format)</param>
    /// <returns>
    /// A new MemoryStream containing the encoded image, positioned at the start and ready for reading.
    /// Returns null if encoding fails or bmp is null.
    /// IMPORTANT: Caller MUST dispose the returned MemoryStream to prevent memory leaks.
    /// </returns>
    /// <remarks>
    /// The returned stream is owned by the caller and must be disposed when no longer needed.
    /// Consider using 'using' statement or calling Dispose() explicitly.
    /// </remarks>
    public static MemoryStream? SaveImageAsStream(SkiaSharp.SKBitmap bmp, EImageFormat imageFormat,
        PNGBitDepth pngBitDepth = PNGBitDepth.Default,
        int jpegQuality = 90,
        GIFQuality gifQuality = GIFQuality.Default)
    {
        if (bmp == null) return null;

        var ms = new MemoryStream();

        try
        {
            if (imageFormat == EImageFormat.TIFF)
            {
                // SkiaSharp has no TIFF encoder.
                TiffEncoder.Encode(bmp, ms);
                ms.Position = 0;
                return ms;
            }

            // As in ShareX, JPEG has no transparency, so transparent areas are filled with white instead of turning black.
            using var opaque = imageFormat == EImageFormat.JPEG ? FillBackground(bmp, SkiaSharp.SKColors.White) : null;
            using var image = SkiaSharp.SKImage.FromBitmap(opaque ?? bmp);
            using var data = imageFormat switch
            {
                EImageFormat.JPEG => image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, jpegQuality),
                EImageFormat.GIF => image.Encode(SkiaSharp.SKEncodedImageFormat.Gif, 100),
                EImageFormat.BMP => image.Encode(SkiaSharp.SKEncodedImageFormat.Bmp, 100),
                EImageFormat.WEBP => image.Encode(SkiaSharp.SKEncodedImageFormat.Webp, jpegQuality), // WebP uses quality like JPEG
                EImageFormat.AVIF => image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100), // AVIF requires FFmpeg, fallback to PNG for stream
                _ => image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            };

            data.SaveTo(ms);
            ms.Position = 0;
            if (imageFormat == EImageFormat.PNG && SettingsManager.Settings.PNGStripColorSpaceInformation)
            {
                var stripped = ImageHelpers.PNGStripColorSpaceInformation(ms);
                ms.Dispose();
                return stripped;
            }
            return ms;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, $"Failed to encode image as {imageFormat}");
            ms.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Draws the image over a solid color, as ShareX's ImageHelpers.FillBackground does.
    /// </summary>
    public static SkiaSharp.SKBitmap FillBackground(SkiaSharp.SKBitmap bmp, SkiaSharp.SKColor color)
    {
        var result = new SkiaSharp.SKBitmap(bmp.Width, bmp.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque);
        using var canvas = new SkiaSharp.SKCanvas(result);
        canvas.Clear(color);
        canvas.DrawBitmap(bmp, 0, 0, new SkiaSharp.SKSamplingOptions());
        return result;
    }

    /// <summary>
    /// Save image to file
    /// </summary>
    public static async Task SaveImageToPathAsync(SkiaSharp.SKBitmap bmp, string filePath, TaskSettings taskSettings)
    {
        if (taskSettings.ImageSettings.ImageFormat != EImageFormat.AVIF)
        {
            SaveImageToPath(bmp, filePath, taskSettings);
            return;
        }

        // AVIF already has an FFmpeg encoder. Keep its output extension on the temporary file.
        string folder = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        Directory.CreateDirectory(folder);
        string temporaryPath = Path.Combine(folder, $".xerahs-{Guid.NewGuid():N}.avif");
        try
        {
            await XerahS.Platform.Abstractions.PlatformServices.ImageEncoder.EncodeAsync(bmp, temporaryPath,
                EImageFormat.AVIF, taskSettings.ImageSettings.ImageJPEGQuality).ConfigureAwait(false);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static void SaveImageToPath(SkiaSharp.SKBitmap bmp, string filePath, TaskSettings taskSettings)
    {
        if (taskSettings.ImageSettings.ImageFormat == EImageFormat.AVIF)
            throw new NotSupportedException("The stream encoder cannot save AVIF images; AVIF requires the AVIF encoder.");
        using var encoded = SaveImageAsStream(bmp, taskSettings.ImageSettings.ImageFormat, taskSettings)
            ?? throw new NotSupportedException($"The configured {taskSettings.ImageSettings.ImageFormat} image format could not be encoded. Choose PNG, JPEG, or WebP.");
        WriteImageStreamToFile(encoded, filePath, overwrite: true);
    }

    public static void WriteImageStreamToFile(Stream encoded, string filePath, bool overwrite)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        Directory.CreateDirectory(folder);
        string temporaryPath = Path.Combine(folder, $".xerahs-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                encoded.CopyTo(output);
            File.Move(temporaryPath, filePath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <param name="fileName">
    /// The name to save under, with the image format's extension; otherwise the name pattern names the file. ShareX
    /// saves a processed file upload under the uploaded file's name.
    /// </param>
    public static async Task<string?> SaveImageAsFileAsync(SkiaSharp.SKBitmap bmp, TaskSettings taskSettings, bool overwriteFile = false,
        string? fileName = null)
    {
        string screenshotsFolder = GetScreenshotsFolder(taskSettings);
        FileHelpers.CreateDirectory(screenshotsFolder);

        using var prepared = await PrepareImageAsync(bmp, taskSettings).ConfigureAwait(false);
        string extension = EnumExtensions.GetDescription(prepared.Format);
        fileName = string.IsNullOrEmpty(fileName) ? GetFileName(taskSettings, extension, bmp) : Path.ChangeExtension(fileName, extension);
        string filePath = Path.Combine(screenshotsFolder, fileName);

        if (!overwriteFile)
        {
            filePath = HandleExistsFile(filePath, taskSettings);
            if (string.IsNullOrEmpty(filePath)) return null;
        }

        // As in ShareX, the image settings' format and JPEG quality are used, as for Save As and uploads.
        WriteImageStreamToFile(prepared.Stream, filePath, overwrite: true);
        return filePath;
    }

    /// <summary>
    /// Apply configured image effects to the bitmap.
    /// </summary>
    public static SkiaSharp.SKBitmap? ApplyImageEffects(SkiaSharp.SKBitmap? bmp, TaskSettingsImage taskSettingsImage)
    {
        if (bmp == null)
        {
            return null;
        }

        if (taskSettingsImage == null)
        {
            return bmp;
        }

        var presets = taskSettingsImage.ImageEffectPresets;
        var preset = presets is not { Count: > 0 } ? null
            : taskSettingsImage.UseRandomImageEffect ? presets[Random.Shared.Next(presets.Count)]
            : taskSettingsImage.SelectedImageEffectPreset >= 0 && taskSettingsImage.SelectedImageEffectPreset < presets.Count
                ? presets[taskSettingsImage.SelectedImageEffectPreset] : null;
        return ApplyImageEffectPreset(bmp, preset);
    }

    /// <summary>Applies a preset without taking ownership of the caller's source image.</summary>
    public static SkiaSharp.SKBitmap ApplyImageEffectPreset(SkiaSharp.SKBitmap bmp, ImageEffectPreset? preset)
    {
        
        if (preset == null || preset.Effects == null || preset.Effects.Count == 0)
        {
            return bmp;
        }

        var result = bmp;
        var usingOriginal = true;

        try
        {
            foreach (var effect in preset.Effects.Where(effect => effect.Enabled))
            {
                var processed = effect.Apply(result);
                if (!ReferenceEquals(processed, result))
                {
                    if (!usingOriginal) result.Dispose();
                    result = processed;
                    usingOriginal = false;
                }
            }
            return result;
        }
        catch
        {
            if (!usingOriginal) result.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Create thumbnail from image
    /// </summary>
    public static SkiaSharp.SKBitmap? CreateThumbnail(SkiaSharp.SKBitmap bmp, int width, int height)
    {
        if (bmp == null) return null;
        if (width <= 0 && height <= 0) return null;

        // Calculate scale ratio maintaining aspect ratio
        double ratio;
        if (width > 0 && height > 0)
        {
            // Both dimensions specified - fit within bounds
            ratio = Math.Min((double)width / bmp.Width, (double)height / bmp.Height);
        }
        else if (width > 0)
        {
            ratio = (double)width / bmp.Width;
        }
        else
        {
            ratio = (double)height / bmp.Height;
        }

        if (ratio <= 0 || ratio >= 1) return null;

        int newWidth = (int)(bmp.Width * ratio);
        int newHeight = (int)(bmp.Height * ratio);

        return ImageHelpers.ResizeImage(bmp, newWidth, newHeight);
    }

    #endregion

    #region Upload Checks

    /// <summary>
    /// Check if uploading is allowed based on settings
    /// </summary>
    public static bool IsUploadAllowed()
    {
        return !SettingsManager.Settings.DisableUpload;
    }

    #endregion
}
