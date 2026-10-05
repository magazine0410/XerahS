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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.Core.Tasks.Processors;
using XerahS.Platform.Abstractions;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

public static class PinToScreenToolService
{
    private static readonly FilePickerFileType ImageFileType = new("Image files")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tiff", "*.tif" }
    };

    public static async Task HandleWorkflowAsync(WorkflowType job, Window? owner, TaskSettings? taskSettings = null)
    {
        bool done = job switch
        {
            WorkflowType.PinToScreen => await PinToScreenAsync(owner),
            WorkflowType.PinToScreenFromScreen => await PinFromScreenAsync(),
            WorkflowType.PinToScreenFromClipboard => await PinFromClipboardAsync(),
            WorkflowType.PinToScreenFromFile => await PinFromFileAsync(owner),
            WorkflowType.PinToScreenCloseAll => CloseAll(),
            _ => false
        };

        // As in ShareX, after an image is pinned and after closing all pinned images.
        if (done)
        {
            NotificationSoundService.PlayActionCompleted(taskSettings);
        }
    }

    private static bool CloseAll()
    {
        PinToScreenManager.CloseAll();
        return true;
    }

    public readonly record struct PinFilesResult(int PinnedCount, int SkippedCount);

    /// <summary>
    /// Pins with OmaSnap on Omarchy-like Hyprland sessions (its Upload button uses the XerahS
    /// destination through omaxerahs), otherwise with the XerahS pin window (XIP0088).
    /// </summary>
    private static async Task PinAsync(SKBitmap bitmap, PixelPoint? location)
    {
        if (await HostedEditorAndPinService.TryPinAsync(bitmap))
        {
            return;
        }

        PinToScreenManager.PinImage(bitmap, location, GetOptions());
    }

    public static async Task<PinFilesResult> PinFilesAsync(IEnumerable<string>? filePaths, bool showToast = true)
    {
        int pinnedCount = 0;
        int skippedCount = 0;

        foreach (string filePath in filePaths ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                skippedCount++;
                continue;
            }

            try
            {
                using SKBitmap? bitmap = SKBitmap.Decode(filePath);
                if (bitmap == null)
                {
                    skippedCount++;
                    continue;
                }

                if (!await HostedEditorAndPinService.TryPinFileAsync(filePath))
                {
                    PinToScreenManager.PinImage(bitmap, null, GetOptions());
                }

                pinnedCount++;
            }
            catch (Exception ex)
            {
                skippedCount++;
                DebugHelper.WriteException(ex, $"PinToScreen: Failed to pin '{filePath}'.");
            }
        }

        if (showToast)
        {
            if (pinnedCount == 0)
            {
                ShowToast("Pin to Screen", "No compatible image files were available to pin.");
            }
            else if (skippedCount > 0)
            {
                ShowToast("Pin to Screen", $"Pinned {pinnedCount} image(s); skipped {skippedCount} item(s).");
            }
        }

        return new PinFilesResult(pinnedCount, skippedCount);
    }

    private static async Task<bool> PinToScreenAsync(Window? owner)
    {
        var dialog = new PinToScreenStartupDialog
        {
            BrowseFileRequested = () => BrowseImageFileAsync(null, owner)
        };

        var fromScreen = false;
        dialog.SelectRegionRequested = () =>
        {
            fromScreen = true;
            return Task.FromResult<(SKBitmap? Bitmap, PixelPoint? Location)>((null, null));
        };

        await ModalDialogHost.ShowUntilClosedAsync(
            dialog,
            set => dialog.CloseRequested = () => set(),
            debugSource: nameof(PinToScreenStartupDialog));

        if (fromScreen)
        {
            var (bitmap, location) = await SelectRegionWithLocationAsync();
            if (bitmap != null)
            {
                try
                {
                    await PinAsync(bitmap, location);
                    return true;
                }
                finally
                {
                    bitmap.Dispose();
                }
            }

            return false;
        }

        if (dialog.Result != null)
        {
            var bitmap = dialog.Result.Image;
            try
            {
                await PinAsync(bitmap, dialog.Result.Location);
                return true;
            }
            finally
            {
                bitmap.Dispose();
            }
        }

        return false;
    }

    private static async Task<bool> PinFromScreenAsync()
    {
        if (!PlatformServices.IsInitialized) return false;

        var captureOptions = BuildCaptureOptions();

        var rect = await PlatformServices.ScreenCapture.SelectRegionAsync(captureOptions);
        if (rect == SKRectI.Empty) return false;

        var bitmap = await PlatformServices.ScreenCapture.CaptureRectAsync(
            new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), captureOptions);
        if (bitmap == null) return false;

        var location = new PixelPoint(rect.Left, rect.Top);
        try
        {
            await PinAsync(bitmap, location);
            return true;
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static async Task<bool> PinFromClipboardAsync()
    {
        if (!PlatformServices.IsInitialized) return false;

        var bitmap = PlatformServices.Clipboard.GetImage();

        if (bitmap == null)
        {
            ShowToast("Pin to Screen", "Clipboard does not contain an image.");
            return false;
        }

        try
        {
            await PinAsync(bitmap, null);
            return true;
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static async Task<bool> PinFromFileAsync(Window? owner)
    {
        var path = await BrowseImageFileAsync(null, owner);
        if (string.IsNullOrEmpty(path)) return false;

        if (await HostedEditorAndPinService.TryPinFileAsync(path))
        {
            return true;
        }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap == null)
        {
            ShowToast("Pin to Screen", "Failed to load image file.");
            return false;
        }

        try
        {
            PinToScreenManager.PinImage(bitmap, null, GetOptions());
            return true;
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    internal static async Task<(SKBitmap? Bitmap, PixelPoint? Location)> SelectRegionWithLocationAsync()
    {
        if (!PlatformServices.IsInitialized) return (null, null);

        var captureOptions = BuildCaptureOptions();

        await Task.Delay(300); // Allow dialog to hide

        var rect = await PlatformServices.ScreenCapture.SelectRegionAsync(captureOptions);
        if (rect == SKRectI.Empty) return (null, null);

        var bitmap = await PlatformServices.ScreenCapture.CaptureRectAsync(
            new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), captureOptions);

        if (bitmap == null) return (null, null);

        return (bitmap, new PixelPoint(rect.Left, rect.Top));
    }

    private static async Task<string?> BrowseImageFileAsync(Window? window, Window? owner)
    {
        var storageProvider = StorageProviderResolver.Resolve(window, owner);
        if (storageProvider == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = "Select Image to Pin",
            AllowMultiple = false,
            FileTypeFilter = new[] { ImageFileType }
        };

        var files = await storageProvider.OpenFilePickerAsync(options);
        if (files.Count < 1) return null;

        return files[0].TryGetLocalPath();
    }

    private static PinToScreenOptions GetOptions()
    {
        return SettingsManager.DefaultTaskSettings?.ToolsSettings?.PinToScreenOptions
            ?? new PinToScreenOptions();
    }

    private static CaptureOptions BuildCaptureOptions()
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
            DebugHelper.WriteException(ex, "PinToScreen toast failed");
        }

        try
        {
            PlatformServices.Notification.ShowNotification(title, text);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "PinToScreen notification failed");
        }
    }
}
