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
using Avalonia.Threading;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

/// <summary>ShareX's TaskHelpers.PrintImage: the print options window, or printing straight away.</summary>
internal static class ImagePrintService
{
    private const string DocumentTitle = "XerahS image";

    /// <summary>Prints with the saved print settings. Returns when printing is done or the window is closed.</summary>
    /// <param name="image">Copied; the caller keeps ownership.</param>
    public static Task PrintImageAsync(SKBitmap image, Window? owner = null) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using SKBitmap copy = image.Copy();
            if (SettingsManager.Settings.DontShowPrintSettingsDialog) await PrintAsync(copy, SettingsManager.Settings.PrintSettings);
            else await ShowPrintOptionsAsync(copy, previewOnly: false, owner);
        });

    /// <summary>ShareX's print options window. <paramref name="previewOnly"/> is the settings page's "Image print settings...".</summary>
    public static async Task ShowPrintOptionsAsync(SKBitmap image, bool previewOnly, Window? owner)
    {
        PrintSettings settings = SettingsManager.Settings.PrintSettings;
        var window = new PrintWindow(new PrintOptionsViewModel(settings, previewOnly))
        {
            PreviewRequested = () => ShowPreviewAsync(image, settings, owner),
            PrintRequested = () => PrintAsync(image, settings)
        };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();

        if (owner is { IsVisible: true } && owner.WindowState != WindowState.Minimized) _ = window.ShowDialog(owner);
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Show();
        }

        await closed.Task;
        await SettingsManager.SaveApplicationConfigAsync();
    }

    /// <summary>Prints and reports problems. Returns false when printing failed, so the print options window stays open.</summary>
    internal static async Task<bool> PrintAsync(SKBitmap image, PrintSettings settings)
    {
        IPrintService? service = PlatformServices.Print;
        if (service == null || !service.IsSupported)
        {
            ShowMessage("Printing unavailable", service?.UnavailableMessage ?? "Printing is not supported on this platform yet.");
            return false;
        }

        try
        {
            PrintResult result = await service.PrintAsync(DocumentTitle,
                page => PrintHelper.CreatePdf(image, settings, page.WidthPoints, page.HeightPoints, DocumentTitle),
                settings.ShowPrintDialog, settings.DefaultPrinterOverride);
            if (result.Warning != null) ShowMessage("Invalid printer name", result.Warning);
            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Print image");
            ShowMessage("Print failed", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// ShareX opens the Windows print preview. XerahS draws the page itself, on the paper of the printer used without
    /// the print dialog (or A4/Letter by region), because the print dialog chooses the paper only when printing.
    /// </summary>
    private static async Task ShowPreviewAsync(SKBitmap image, PrintSettings settings, Window? owner)
    {
        try
        {
            PrintPageSize page = PlatformServices.Print is { IsSupported: true } service
                ? await service.GetDefaultPageSizeAsync(settings.DefaultPrinterOverride)
                : PrintPageSize.A4;
            byte[] png = await Task.Run(() =>
            {
                using SKBitmap preview = PrintHelper.RenderPreview(image, settings, page.WidthPoints, page.HeightPoints);
                using SKData data = preview.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            });
            var viewer = new ImageViewerWindow(png, "Print preview");
            if (owner is { IsVisible: true }) viewer.Show(owner);
            else viewer.Show();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Print preview");
            ShowMessage("Print preview failed", ex.Message);
        }
    }

    private static void ShowMessage(string title, string text)
    {
        DebugHelper.WriteLine($"Print: {title}: {text}");
        if (!PlatformServices.IsToastServiceInitialized) return;
        PlatformServices.Toast.ShowToast(new ToastConfig { Title = title, Text = text, Duration = 6f, AutoHide = true });
    }
}
