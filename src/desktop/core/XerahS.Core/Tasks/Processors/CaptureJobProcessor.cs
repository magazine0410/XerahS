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
using XerahS.History;
using XerahS.Platform.Abstractions;
using XerahS.Services;
using XerahS.Uploaders;
using ShareX.ImageEditor.Core.Persistence;
using ShareX.ImageEditor.Hosting;
using XerahS.Core.Services;
using SkiaSharp;

namespace XerahS.Core.Tasks.Processors
{
    public class CaptureJobProcessor : IJobProcessor
    {
        /// <summary>
        /// Callback to pin an image to the desktop. Set by the UI layer to dispatch to PinToScreenManager.
        /// Takes bitmap, location (object for cross-layer safety), and options.
        /// </summary>
        public static Func<SKBitmap, object?, PinToScreenOptions, Task>? PinToScreenCallback { get; set; }

        /// <summary>
        /// Opens the Analyze image window for an image file with the task's AI options. Set by the UI layer.
        /// </summary>
        public static Func<string, TaskSettings, Task>? ShowAnalyzeImageCallback { get; set; }

        /// <summary>
        /// Prints an image with the print settings, as ShareX's TaskHelpers.PrintImage. Completes when printing is
        /// done or the print options window is closed. Set by the UI layer.
        /// </summary>
        public static Func<SKBitmap, Task>? PrintImageCallback { get; set; }

        public static Func<TaskSettings, CancellationToken, Task<QuickTaskMenuResult>>? ShowQuickTaskMenuCallback { get; set; }
        public static Func<TaskInfo, CancellationToken, Task<string?>>? SaveImageWithDialogCallback { get; set; }

        /// <summary>
        /// Executes after-capture tasks for the current job.
        /// </summary>
        /// <returns><c>true</c> to continue the pipeline; <c>false</c> if the user cancelled.</returns>
        public async Task<bool> ProcessAsync(TaskInfo info, CancellationToken token)
        {
            if (info.Metadata?.Image == null)
            {
                if (info.Job == TaskJob.Job) await AfterCaptureFileTasks.ProcessAsync(info, token);
                return true;
            }

            var settings = info.TaskSettings;
            DebugHelper.WriteLine(
                $"AfterCaptureJob={settings.AfterCaptureJob}, " +
                $"UploadImageToHost={settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.UploadImageToHost)}");
            ImageEditorSessionResult? editorResult = null;
            string? annotationSidecarPath = null;
            bool annotationSidecarSaveAttempted = false;

            try
            {
                token.ThrowIfCancellationRequested();
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowQuickTaskMenu))
                {
                    var showMenu = ShowQuickTaskMenuCallback ?? throw new InvalidOperationException("The quick task menu is unavailable in this host.");
                    var selection = await showMenu(settings, token);
                    if (selection == QuickTaskMenuResult.Cancel) return false;
                    if (selection == QuickTaskMenuResult.Preset && info.Job == TaskJob.DataUpload)
                        info.Job = TaskJob.Job; // A clipboard image preset can choose saving without uploading.
                }

                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowAfterCaptureWindow))
                {
                    if (!PlatformServices.IsInitialized)
                    {
                        DebugHelper.WriteLine("ShowAfterCaptureWindow requested but UI service is not initialized.");
                    }
                    else
                    {
                        var originalAfterCapture = settings.AfterCaptureJob;
                        var result = await PlatformServices.UI.ShowAfterCaptureWindowAsync(
                            info.Metadata.Image,
                            settings.AfterCaptureJob,
                            settings.AfterUploadJob);
                        if (result.Cancel)
                        {
                            DebugHelper.WriteLine("After capture window cancelled; aborting workflow.");
                            return false;
                        }

                        if (info.Job == TaskJob.DataUpload) info.Job = TaskJob.Job;
                        settings.AfterCaptureJob = GetAfterCaptureTasksForRun(result);
                        settings.AfterUploadJob = result.Upload;
                        info.SuppressCompletionNotification = result.QuickAction != AfterCaptureQuickAction.None;

                        // Persist "Show after capture window" setting if user unchecked it
                        if (result.QuickAction == AfterCaptureQuickAction.None &&
                            originalAfterCapture.HasFlag(AfterCaptureTasks.ShowAfterCaptureWindow) &&
                            !result.Capture.HasFlag(AfterCaptureTasks.ShowAfterCaptureWindow))
                        {
                            PersistShowAfterCaptureWindowSetting(settings.WorkflowId, false);
                        }
                    }
                }

                token.ThrowIfCancellationRequested();
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.BeautifyImage))
                {
                    var beautified = await PlatformServices.UI.ShowEditorSessionAsync(info.Metadata.Image,
                        settings.ToolsSettingsReference.ImageEditorOptions ??= new ImageEditorOptions(),
                        taskMode: true, openBackgroundPanel: true);
                    beautified?.SourceImage?.Dispose();
                    if (beautified?.RenderedImage == null) return false;
                    if (!ReferenceEquals(info.Metadata.Image, beautified.RenderedImage)) info.Metadata.Image.Dispose();
                    info.Metadata.Image = beautified.RenderedImage;
                }

                // Annotation should happen BEFORE save, so the saved file includes annotations
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.AddImageEffects))
                {
                    if (info.Metadata?.Image != null)
                    {
                        var processed = TaskHelpers.ApplyImageEffects(info.Metadata.Image, settings.ImageSettings);
                        if (processed == null)
                        {
                            DebugHelper.WriteLine("Error: Applying image effects resulted in null image.");
                            return false;
                        }

                        if (!ReferenceEquals(processed, info.Metadata.Image))
                        {
                            info.Metadata.Image.Dispose();
                        }

                        info.Metadata.Image = processed;
                    }
                }

                // Annotation should happen BEFORE save, so the saved file includes annotations
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.AnnotateMedia))
                {
                    // OmaSnap editor on Hyprland when selected in settings (XIP0088); otherwise the XerahS editor.
                    var hostedAnnotation = info.Metadata?.Image != null
                        ? await HostedEditorAndPinService.TryAnnotateAsync(info.Metadata.Image, token)
                        : (Handled: false, Annotated: null);
                    if (hostedAnnotation.Handled)
                    {
                        if (hostedAnnotation.Annotated == null) return false;
                        if (hostedAnnotation.Annotated != null)
                        {
                            info.Metadata!.Image!.Dispose();
                            info.Metadata.Image = hostedAnnotation.Annotated;
                        }
                    }
                    else if (info.Metadata?.Image != null && PlatformServices.UI != null)
                    {
                        editorResult = await PlatformServices.UI.ShowEditorSessionAsync(info.Metadata.Image,
                            settings.ToolsSettingsReference.ImageEditorOptions ??= new ImageEditorOptions(), taskMode: true);
                        if (editorResult?.RenderedImage == null) return false;
                        if (editorResult.RenderedImage != null)
                        {
                            if (info.Metadata.Image != editorResult.RenderedImage)
                            {
                                info.Metadata.Image.Dispose();
                            }
                            info.Metadata.Image = editorResult.RenderedImage;
                        }
                    }
                }

                token.ThrowIfCancellationRequested();
                // As in ShareX, "Analyze image" saves the image too, because the analysis window opens the file.
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveImageToFile) ||
                    settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.AnalyzeImage))
                {
                    await SaveImageToFileAsync(info);
                }

                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveImageToFileWithDialog))
                {
                    var saveDialog = SaveImageWithDialogCallback ?? throw new InvalidOperationException("The save image dialog is unavailable in this host.");
                    string? path = await saveDialog(info, token);
                    // Like ShareX, cancelling Save As skips that save and continues the remaining tasks.
                    if (!string.IsNullOrEmpty(path)) info.FilePath = path;
                }

                if (!string.IsNullOrEmpty(info.FilePath))
                {
                    annotationSidecarPath = await SaveAnnotationSidecarAsync(info, editorResult);
                    annotationSidecarSaveAttempted = true;
                }

                const AfterCaptureTasks prepareImageTasks = AfterCaptureTasks.SaveImageToFile | AfterCaptureTasks.SaveImageToFileWithDialog |
                    AfterCaptureTasks.DoOCR | AfterCaptureTasks.UploadImageToHost | AfterCaptureTasks.AnalyzeImage;
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveThumbnailImageToFile) && (settings.AfterCaptureJob & prepareImageTasks) != 0)
                {
                    EnsureImageFileName(info);
                    info.ThumbnailFilePath = await AfterCaptureFileTasks.SaveThumbnailAsync(info, token) ?? string.Empty;
                }

                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyImageToClipboard))
                {
                    if (PlatformServices.IsInitialized && info.Metadata?.Image != null)
                    {
                        PlatformServices.Clipboard.SetImage(info.Metadata.Image);
                        DebugHelper.WriteLine("Image copied to clipboard.");
                    }
                }

                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.DoOCR))
                {
                    await PerformOCRAsync(info);

                    if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyOcrTextToClipboard))
                    {
                        TryCopyOcrTextToClipboard(info.Metadata?.OcrText);
                    }
                }

                // ScanQRCode
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ScanQRCode))
                {
                    if (info.Metadata?.Image == null)
                    {
                        DebugHelper.WriteLine("ScanQRCode skipped: no image in metadata.");
                    }
                    else
                    {
                        try
                        {
                            var results = QrCodeService.Decode(info.Metadata.Image, out var error);
                            if (!string.IsNullOrEmpty(error))
                            {
                                DebugHelper.WriteLine($"ScanQRCode error: {error}");
                            }
                            else if (results.Count > 0)
                            {
                                PlatformServices.Clipboard.SetText(string.Join(Environment.NewLine, results));
                                DebugHelper.WriteLine($"ScanQRCode decoded {results.Count} code(s) and copied to clipboard.");
                            }
                            else
                            {
                                DebugHelper.WriteLine("ScanQRCode: no QR codes detected.");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugHelper.WriteException(ex, "ScanQRCode");
                        }
                    }
                }

                // PinToScreen
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.PinToScreen))
                {
                    if (info.Metadata?.Image == null)
                    {
                        DebugHelper.WriteLine("PinToScreen skipped: no image in metadata.");
                    }
                    else if (PinToScreenCallback == null)
                    {
                        DebugHelper.WriteLine("PinToScreen skipped: callback not set.");
                    }
                    else
                    {
                        try
                        {
                            if (await HostedEditorAndPinService.TryPinAsync(info.Metadata.Image, token))
                            {
                                DebugHelper.WriteLine("PinToScreen: image pinned with OmaSnap.");
                            }
                            else
                            {
                                var options = SettingsManager.DefaultTaskSettings?.ToolsSettings?.PinToScreenOptions ?? new PinToScreenOptions();
                                await PinToScreenCallback(info.Metadata.Image, null, options);
                                DebugHelper.WriteLine("PinToScreen: image pinned to desktop.");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugHelper.WriteException(ex, "PinToScreen");
                        }
                    }
                }

                // As in ShareX, print after pinning; cancelling the print dialog does not stop the other tasks.
                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SendImageToPrinter) && info.Metadata?.Image != null)
                {
                    if (PrintImageCallback == null) DebugHelper.WriteLine("SendImageToPrinter skipped: no UI to print with.");
                    else await PrintImageCallback(info.Metadata.Image);
                }

                await AfterCaptureFileTasks.ProcessAsync(info, token);

                if (settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.UploadImageToHost))
                {
                    await UploadImageAsync(info, token);
                    if (!annotationSidecarSaveAttempted)
                    {
                        annotationSidecarPath = await SaveAnnotationSidecarAsync(info, editorResult);
                        annotationSidecarSaveAttempted = true;
                    }
                }
                else
                {
                    DebugHelper.WriteLine("UploadImageToHost flag not set; skipping upload.");
                }

                // Add to History (after all tasks, including upload, are complete)
                if (!string.IsNullOrEmpty(info.FilePath))
                {
                    try
                    {
                        DebugHelper.WriteLine("Trace: History pipeline - Starting history item creation.");

                        // Use centralized history file path
                        var historyPath = SettingsManager.GetHistoryFilePath();

                        DebugHelper.WriteLine($"Trace: History pipeline - History file path: {historyPath}");

                        using var historyManager = new HistoryManagerSQLite(historyPath);
                        var historyItem = new HistoryItem
                        {
                            FilePath = info.FilePath,
                            FileName = Path.GetFileName(info.FilePath),
                            DateTime = DateTime.Now,
                            Type = "Image",
                            URL = info.Metadata?.UploadURL ?? string.Empty
                        };
                        historyItem.AnnotationSidecarPath = annotationSidecarPath;

                        var tags = info.GetTags();
                        if (tags != null)
                        {
                            historyItem.Tags = new Dictionary<string, string?>(tags.Count);
                            foreach (var pair in tags)
                            {
                                historyItem.Tags[pair.Key] = pair.Value;
                            }
                        }

                        // Screenshots uploaded by this workflow used to lose host, deletion URL and
                        // upload metadata here; record them like upload jobs do.
                        if (!string.IsNullOrWhiteSpace(historyItem.URL))
                        {
                            UploadJobProcessor.ApplyUploadResult(historyItem, info);
                        }

                        bool appended = historyManager.AppendHistoryItem(historyItem);
                        DebugHelper.WriteLine($"Trace: History pipeline - AppendHistoryItem called for: {historyItem.FileName} (URL: {historyItem.URL})");
                        if (appended)
                        {
                            info.HistoryItemId = historyItem.Id;
                            DebugHelper.WriteLine($"Added to history: {historyItem.FileName}");

                            if (!string.IsNullOrWhiteSpace(info.Metadata?.OcrText))
                            {
                                await OcrIndexingService.PersistRecognizedTextAsync(
                                    historyItem,
                                    info.Metadata.OcrText,
                                    "after-capture-ocr",
                                    NormalizeOcrLanguage(info.TaskSettings.CaptureSettings.OCROptions?.Language),
                                    token);
                            }
                            else
                            {
                                OcrIndexingService.QueueIndexHistoryItem(historyItem);
                            }
                        }
                        else
                        {
                            DebugHelper.WriteLine($"Failed to append history item: {historyItem.FileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugHelper.WriteLine($"Failed to add to history: {ex.Message}");
                        DebugHelper.WriteException(ex);
                    }
                }

                return true;
            }
            finally
            {
                editorResult?.SourceImage?.Dispose();
            }
        }

        public static void EnsureImageFileName(TaskInfo info)
        {
            if (string.IsNullOrEmpty(info.FileName))
                info.SetFileName(TaskHelpers.GetFileName(info.TaskSettings,
                    EnumExtensions.GetDescription(info.TaskSettings.ImageSettings.ImageFormat), info.Metadata));
        }

        private static async Task<string?> SaveAnnotationSidecarAsync(TaskInfo info, ImageEditorSessionResult? editorResult)
        {
            if (editorResult == null)
            {
                return null;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(info.FilePath) ||
                    editorResult.Annotations.Count == 0 ||
                    editorResult.SourceImage == null)
                {
                    return null;
                }

                string? sidecarPath = await XannProjectFileService.SaveAsync(
                    info.FilePath,
                    editorResult.SourceImage,
                    editorResult.Annotations);
                DebugHelper.WriteLine($"Annotation sidecar saved: {sidecarPath}");
                return sidecarPath;
            }
            catch (Exception ex)
            {
                DebugHelper.WriteLine($"Failed to save annotation sidecar: {ex.Message}");
                DebugHelper.WriteException(ex);
                return null;
            }
        }

        private async Task SaveImageToFileAsync(TaskInfo info)
        {
            if (info.Metadata?.Image == null) return;

            SkiaSharp.SKBitmap bmp = info.Metadata.Image;

            // TaskHelpers contains the logic for folder resolution, naming, and file exists handling.
            string? filePath = await TaskHelpers.SaveImageAsFileAsync(bmp, info.TaskSettings);
            if (!string.IsNullOrEmpty(filePath))
            {
                var directory = Path.GetDirectoryName(filePath) ?? "";
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath);
                DebugHelper.WriteLine($"[PathTrace {info.CorrelationId}] SaveImageToFile: dir=\"{directory}\", fileName=\"{fileName}\", ext=\"{extension}\", fullPath=\"{filePath}\"");
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                info.FilePath = filePath;
                DebugHelper.WriteLine($"Image saved: {filePath}");
            }
            else
            {
                DebugHelper.WriteLine("Failed to save image.");
                // info.Status = TaskStatus.Failed; // Logic to handle failure
            }
        }

        private async Task UploadImageAsync(TaskInfo info, CancellationToken token)
        {
            if (string.IsNullOrEmpty(info.FilePath) && info.Metadata?.Image != null)
            {
                info.FilePath = await TaskHelpers.SaveImageAsFileAsync(info.Metadata.Image, info.TaskSettings) ?? string.Empty;
            }

            if (string.IsNullOrEmpty(info.FilePath))
            {
                DebugHelper.WriteLine("Upload failed: No file to upload.");
                return;
            }

            DebugHelper.WriteLine($"Uploading image: {info.FilePath}");

            try
            {
                info.DataType = EDataType.Image;
                info.UploadAttemptedDuringCapture = true;
                var pluginResult = await new UploadJobProcessor().UploadAsync(info, token);
                if (info.UploadCancelled) return;
                if (pluginResult == null)
                {
                    DebugHelper.WriteLine("Plugin upload did not return a result.");
                    return;
                }

                HandleUploadResult(info, pluginResult);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Upload error");
            }
        }

        private async Task PerformOCRAsync(TaskInfo info)
        {
            if (info.Metadata?.Image == null)
            {
                DebugHelper.WriteLine("OCR skipped: no image in metadata.");
                return;
            }

            var ocrService = PlatformServices.Ocr;
            if (ocrService == null || !ocrService.IsSupported)
            {
                // ShareX reports this as an error instead of skipping the task without a word.
                string message = ocrService?.UnavailableMessage ?? "OCR is not supported on this platform.";
                DebugHelper.WriteLine("OCR skipped: " + message);
                if (PlatformServices.IsToastServiceInitialized)
                    PlatformServices.Toast.ShowToast(new ToastConfig { Title = "OCR unavailable", Text = message, Duration = 6f, AutoHide = true });
                return;
            }

            try
            {
                DebugHelper.WriteLine("Starting OCR on captured image...");
                var taskOcrOptions = info.TaskSettings.CaptureSettings.OCROptions ?? new OCROptions();
                var options = new OcrOptions
                {
                    Language = NormalizeOcrLanguage(taskOcrOptions.Language),
                    ScaleFactor = NormalizeOcrScaleFactor(taskOcrOptions.ScaleFactor),
                    SingleLine = taskOcrOptions.SingleLine
                };
                var result = await ocrService.RecognizeAsync(info.Metadata.Image, options);

                if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    info.Metadata.OcrText = result.Text;
                    DebugHelper.WriteLine($"OCR completed. Text length: {result.Text.Length} chars.");
                }
                else
                {
                    DebugHelper.WriteLine($"OCR completed but no text recognized: {result.ErrorMessage}");
                }

                if (taskOcrOptions.Silent)
                {
                    // As in ShareX: silent OCR copies the text without opening the window, and clears the clipboard when nothing was found.
                    if (!string.IsNullOrWhiteSpace(info.Metadata.OcrText)) PlatformServices.Clipboard.SetText(info.Metadata.OcrText);
                    else PlatformServices.Clipboard.Clear();
                }
                else if (PlatformServices.IsInitialized)
                {
                    // Show OCR window so user can review/adjust the result
                    await PlatformServices.UI.ShowOcrWindowAsync(info.Metadata.Image);
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "OCR error");
            }

            await Task.CompletedTask;
        }

        private static void TryCopyOcrTextToClipboard(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                DebugHelper.WriteLine("CopyOcrTextToClipboard skipped: OCR text is empty.");
                return;
            }

            XerahS.Platform.Abstractions.IClipboardService? clipboardService;
            try
            {
                clipboardService = PlatformServices.Clipboard;
            }
            catch (InvalidOperationException)
            {
                DebugHelper.WriteLine("CopyOcrTextToClipboard skipped: clipboard service unavailable.");
                return;
            }

            if (clipboardService == null)
            {
                DebugHelper.WriteLine("CopyOcrTextToClipboard skipped: clipboard service unavailable.");
                return;
            }

            try
            {
                clipboardService.SetText(text);
                DebugHelper.WriteLine($"CopyOcrTextToClipboard: copied {text.Length} chars.");
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "CopyOcrTextToClipboard");
            }
        }

        private static string NormalizeOcrLanguage(string? language)
        {
            string? trimmedLanguage = language?.Trim();
            return string.IsNullOrEmpty(trimmedLanguage) ? "en" : trimmedLanguage;
        }

        private static float NormalizeOcrScaleFactor(float scaleFactor)
        {
            return float.IsFinite(scaleFactor) ? Math.Max(scaleFactor, 1f) : 1f;
        }

        private static void HandleUploadResult(TaskInfo info, UploadResult? result)
        {
            if (result != null && !result.IsError && !string.IsNullOrEmpty(result.URL))
            {
                info.Metadata!.UploadURL = result.URL;
                info.Result = result;
                info.DataType = EDataType.Image;
                DebugHelper.WriteLine($"Upload successful: {result.URL}");
                DebugHelper.WriteLine("Upload complete.");
                return;
            }

            if (result != null) info.Result = result;
            string? errorText = result?.Errors?.Errors?.FirstOrDefault()?.Text ?? result?.Errors?.ToString();
            DebugHelper.WriteLine($"Upload failed: {errorText ?? "Unknown upload error."}");
        }

        /// <summary>
        /// Maps a terminal After Capture quick action to the tasks for this run while preserving
        /// the workflow's ShowAfterCaptureWindow flag for future captures.
        /// </summary>
        internal static AfterCaptureTasks GetAfterCaptureTasksForRun(
            (AfterCaptureTasks Capture, AfterUploadTasks Upload, bool Cancel, AfterCaptureQuickAction QuickAction) result)
        {
            if (result.QuickAction == AfterCaptureQuickAction.None)
            {
                return result.Capture;
            }

            return result.Capture | AfterCaptureTasks.ShowAfterCaptureWindow;
        }

        /// <summary>
        /// Persists the "Show after capture window" setting change back to the workflow configuration.
        /// Uses synchronous save to ensure settings are written before app exit.
        /// </summary>
        private static void PersistShowAfterCaptureWindowSetting(string? workflowId, bool showWindow)
        {
            try
            {
                // Find the workflow by ID
                var workflow = !string.IsNullOrEmpty(workflowId) ? SettingsManager.GetWorkflowById(workflowId) : null;
                if (workflow?.TaskSettings == null)
                {
                    // Fall back to default task settings if no workflow found
                    if (SettingsManager.DefaultTaskSettings != null)
                    {
                        if (showWindow)
                        {
                            SettingsManager.DefaultTaskSettings.AfterCaptureJob |= AfterCaptureTasks.ShowAfterCaptureWindow;
                        }
                        else
                        {
                            SettingsManager.DefaultTaskSettings.AfterCaptureJob &= ~AfterCaptureTasks.ShowAfterCaptureWindow;
                        }
                        // Use synchronous save to ensure the setting is persisted immediately
                        // Async save is fire-and-forget and may not complete before app exit
                        SettingsManager.SaveWorkflowsConfig();
                        DebugHelper.WriteLine($"Updated DefaultTaskSettings.AfterCaptureJob (ShowAfterCaptureWindow={showWindow})");
                    }
                    return;
                }

                // Update the after capture tasks the workflow runs with: its own, or the defaults it uses.
                TaskSettings target = workflow.TaskSettings.UseDefaultAfterCaptureJob && SettingsManager.DefaultTaskSettings != null
                    ? SettingsManager.DefaultTaskSettings
                    : workflow.TaskSettings;
                if (showWindow)
                {
                    target.AfterCaptureJob |= AfterCaptureTasks.ShowAfterCaptureWindow;
                }
                else
                {
                    target.AfterCaptureJob &= ~AfterCaptureTasks.ShowAfterCaptureWindow;
                }

                // Use synchronous save to ensure the setting is persisted immediately
                // Async save is fire-and-forget and may not complete before app exit
                SettingsManager.SaveWorkflowsConfig();
                DebugHelper.WriteLine($"Persisted ShowAfterCaptureWindow={showWindow} to workflow '{workflowId}'");
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Failed to persist ShowAfterCaptureWindow setting");
            }
        }

    }
}
