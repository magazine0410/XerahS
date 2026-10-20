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
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.Platform.Abstractions;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

public static class OcrToolService
{
    public static Task HandleWorkflowAsync(WorkflowType job, Window? owner)
    {
        return job switch
        {
            WorkflowType.OCR => PerformOcrAsync(owner),
            _ => Task.CompletedTask
        };
    }

    private static async Task PerformOcrAsync(Window? owner)
    {
        var ocrService = PlatformServices.Ocr;

        if (ocrService == null || !ocrService.IsSupported)
        {
            ShowToast("OCR", ocrService?.UnavailableMessage ?? "OCR is not supported on this platform.");
            return;
        }

        if (!PlatformServices.IsInitialized)
        {
            ShowToast("OCR", "Platform services are not initialized.");
            return;
        }

        // Capture region
        var captureOptions = CreateCaptureOptions();

        var bitmap = await PlatformServices.ScreenCapture.CaptureRegionAsync(captureOptions);

        if (bitmap == null)
        {
            return; // User cancelled, no toast needed
        }

        ShowOcrWindow(bitmap, captureOptions, owner);
    }

    /// <summary>
    /// ShareX's OCRImage(filePath), for the -OCR argument with an image file: the OCR window opens with the image, or with
    /// "Silent" on the text is copied without it. As in ShareX, the text found is also saved next to the image, in a .txt
    /// file of the same name. Completes when the window closes.
    /// </summary>
    public static async Task OcrImageFileAsync(string filePath, Window? owner, TaskSettings? taskSettings)
    {
        var ocrService = PlatformServices.Ocr;
        if (ocrService == null || !ocrService.IsSupported)
        {
            ShowToast("OCR", ocrService?.UnavailableMessage ?? "OCR is not supported on this platform.");
            return;
        }

        var bitmap = ImageHelpers.LoadBitmap(filePath);
        if (bitmap == null)
        {
            ShowToast("OCR", "The image could not be opened. Check that it uses a supported image format.");
            return;
        }

        var ocrOptions = (taskSettings ?? SettingsManager.DefaultTaskSettings)?.CaptureSettings?.OCROptions ?? new OCROptions();
        if (ocrOptions.Silent)
        {
            using (bitmap)
            {
                var result = await ocrService.RecognizeAsync(bitmap, new OcrOptions
                {
                    Language = string.IsNullOrWhiteSpace(ocrOptions.Language) ? "en" : ocrOptions.Language.Trim(),
                    ScaleFactor = float.IsFinite(ocrOptions.ScaleFactor) ? Math.Max(ocrOptions.ScaleFactor, 1f) : 1f,
                    SingleLine = ocrOptions.SingleLine
                });

                if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    PlatformServices.Clipboard.SetText(result.Text);
                    SaveTextNextToImage(filePath, result.Text);
                }
                else
                {
                    PlatformServices.Clipboard.Clear();
                }

                NotificationSoundService.PlayActionCompleted(taskSettings);
            }

            return;
        }

        var window = ShowOcrWindow(bitmap, CreateCaptureOptions(), owner);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        await closed.Task;

        if (window.DataContext is OcrViewModel { ResultText: { } text } && !string.IsNullOrWhiteSpace(text))
        {
            SaveTextNextToImage(filePath, text);
        }
    }

    private static void SaveTextNextToImage(string imageFilePath, string text)
    {
        try
        {
            File.WriteAllText(Path.ChangeExtension(imageFilePath, "txt"), text, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "OCR: could not save the text next to the image");
        }
    }

    private static CaptureOptions CreateCaptureOptions()
    {
        var captureSettings = SettingsManager.DefaultTaskSettings?.CaptureSettings
            ?? new TaskSettingsCapture();

        return new CaptureOptions
        {
            UseModernCapture = captureSettings.UseModernCapture,
            LinuxRegionSelectorPreference = captureSettings.LinuxRegionSelectorPreference,
            MacOSRegionSelectorPreference = captureSettings.MacOSRegionSelectorPreference,
            MacOSPlayCaptureSound = captureSettings.MacOSPlayCaptureSound,
            ShowCursor = captureSettings.ShowCursor,
            CaptureTransparent = captureSettings.CaptureTransparent,
            CaptureShadow = captureSettings.CaptureShadow,
            CaptureClientArea = captureSettings.CaptureClientArea
        };
    }

    private static OcrWindow ShowOcrWindow(SKBitmap bitmap, CaptureOptions captureOptions, Window? owner)
    {
        // Create ViewModel and wire callbacks
        var viewModel = new OcrViewModel(bitmap);

        viewModel.SelectRegionRequested = async () =>
        {
            return await CaptureNewRegionAsync(captureOptions);
        };

        var window = new OcrWindow
        {
            DataContext = viewModel
        };

        if (CanUseOwner(owner))
        {
            window.Show(owner!);
        }
        else
        {
            window.Show();
        }

        return window;
    }

    private static async Task<SKBitmap?> CaptureNewRegionAsync(CaptureOptions captureOptions)
    {
        try
        {
            await Task.Delay(300); // Allow window to minimize
            return await PlatformServices.ScreenCapture.CaptureRegionAsync(captureOptions);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "OCR region capture");
            return null;
        }
    }

    private static bool CanUseOwner(Window? owner)
    {
        return owner != null &&
               owner.IsVisible &&
               owner.WindowState != Avalonia.Controls.WindowState.Minimized &&
               owner.ShowInTaskbar;
    }

    private static void ShowToast(string title, string text)
    {
        try
        {
            if (PlatformServices.IsToastServiceInitialized)
            {
                PlatformServices.Toast.ShowToast(new ToastConfig
                {
                    Title = title,
                    Text = text,
                    Duration = 4f,
                    Size = new SizeI(420, 120),
                    AutoHide = true,
                    LeftClickAction = ToastClickAction.CloseNotification
                });
                return;
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "OCR toast failed");
        }

        try
        {
            PlatformServices.Notification.ShowNotification(title, text);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "OCR notification failed");
        }
    }
}
