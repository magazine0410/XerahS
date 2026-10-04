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

/// <summary>Opens ShareX's Analyze image window, from the Tools menu, a hotkey, or the after-capture task.</summary>
internal static class AnalyzeImageToolService
{
    public static Task HandleWorkflowAsync(Window? owner, TaskSettings? taskSettings)
    {
        Show(owner, taskSettings, filePath: null, image: null);
        return Task.CompletedTask;
    }

    /// <param name="image">Copied; the caller keeps ownership.</param>
    public static void Show(Window? owner, TaskSettings? taskSettings, string? filePath, SKBitmap? image)
    {
        // As in ShareX, the options live in the tools settings the workflow saves, so changes are kept.
        TaskSettings settings = taskSettings ?? TaskSettings.GetSafeTaskSettings(SettingsManager.DefaultTaskSettings);
        AIOptions options = settings.ToolsSettingsReference.AIOptions ??= new AIOptions();
        var service = new AnalyzeImageService();
        var viewModel = new AnalyzeImageViewModel(options, service, AIApiKeys.Get, image, filePath);
        var window = new AnalyzeImageWindow(viewModel, () => CaptureRegionAsync(settings.CaptureSettings), service)
        {
            OptionsSaved = SettingsManager.SaveWorkflowsConfig
        };
        // ShareX keeps the last prompt; it is saved with the options when the window closes.
        window.Closed += (_, _) => SettingsManager.SaveWorkflowsConfig();

        if (owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized) window.Show(owner);
        else window.Show();
    }

    private static async Task<SKBitmap?> CaptureRegionAsync(TaskSettingsCapture captureSettings)
    {
        try
        {
            return await PlatformServices.ScreenCapture.CaptureRegionAsync(new CaptureOptions
            {
                UseModernCapture = captureSettings.UseModernCapture,
                LinuxRegionSelectorPreference = captureSettings.LinuxRegionSelectorPreference,
                MacOSRegionSelectorPreference = captureSettings.MacOSRegionSelectorPreference,
                MacOSPlayCaptureSound = captureSettings.MacOSPlayCaptureSound,
                ShowCursor = captureSettings.ShowCursor,
                CaptureTransparent = captureSettings.CaptureTransparent,
                CaptureShadow = captureSettings.CaptureShadow,
                CaptureClientArea = captureSettings.CaptureClientArea
            });
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Analyze image region capture");
            return null;
        }
    }
}
