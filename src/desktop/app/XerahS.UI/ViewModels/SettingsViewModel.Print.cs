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

using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;

namespace XerahS.UI.ViewModels
{
    // ShareX's Print settings page.
    public partial class SettingsViewModel
    {
        public bool DontShowPrintSettingsDialog
        {
            get => SettingsManager.Settings.DontShowPrintSettingsDialog;
            set
            {
                SettingsManager.Settings.DontShowPrintSettingsDialog = value;
                OnPropertyChanged();
            }
        }

        public bool DontShowPrintDialog
        {
            get => !SettingsManager.Settings.PrintSettings.ShowPrintDialog;
            set
            {
                SettingsManager.Settings.PrintSettings.ShowPrintDialog = !value;
                OnPropertyChanged();
            }
        }

        public string DefaultPrinterOverride
        {
            get => SettingsManager.Settings.PrintSettings.DefaultPrinterOverride;
            set
            {
                SettingsManager.Settings.PrintSettings.DefaultPrinterOverride = value?.Trim() ?? string.Empty;
                OnPropertyChanged();
            }
        }

        /// <summary>As in ShareX, opens the print options with a screenshot to preview them on, without printing.</summary>
        [RelayCommand]
        private async Task ShowImagePrintSettingsAsync()
        {
            SKBitmap? image = null;
            try
            {
                image = await PlatformServices.ScreenCapture.CaptureFullScreenAsync(new CaptureOptions { ShowCursor = false });
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Capture for image print settings");
            }

            if (image == null)
            {
                // Without a screenshot, preview the layout on a plain landscape image.
                image = new SKBitmap(1920, 1080);
                image.Erase(new SKColor(200, 205, 215));
            }

            using (image)
            {
                var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                await ImagePrintService.ShowPrintOptionsAsync(image, previewOnly: true, owner);
            }
        }
    }
}
