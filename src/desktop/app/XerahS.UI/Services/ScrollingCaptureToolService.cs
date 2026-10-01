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

using System.Drawing;
using Avalonia.Controls;
using SkiaSharp;
using XerahS.Bootstrap;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Managers;
using XerahS.Core.Services;
using XerahS.Platform.Abstractions;
using XerahS.RegionCapture;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

/// <summary>
/// ShareX's scrolling capture: the hotkey or menu item opens the window, which starts an area
/// selection; invoking it again stops a running capture or starts a new selection.
/// </summary>
public static class ScrollingCaptureToolService
{
    private static ScrollingCaptureWindow? s_window;

    /// <summary>
    /// The view model of the currently open scrolling capture window, if any.
    /// Used so the Scrolling Capture hotkey can stop an in-progress capture.
    /// </summary>
    internal static ScrollingCaptureViewModel? CurrentCapture { get; private set; }

    public static Task HandleWorkflowAsync(
        WorkflowType job,
        Window? owner,
        IDesktopTaskManager taskManager,
        TaskSettings? taskSettings = null)
    {
        return job switch
        {
            WorkflowType.ScrollingCapture => StartStopAsync(taskManager, taskSettings),
            _ => Task.CompletedTask
        };
    }

    /// <summary>
    /// Stops the current scrolling capture if one is active (e.g. when user presses the same hotkey again).
    /// </summary>
    internal static void StopCurrentCapture()
    {
        if (CurrentCapture?.IsCapturing == true)
        {
            CurrentCapture.StopCapture();
        }
    }

    private static async Task StartStopAsync(IDesktopTaskManager taskManager, TaskSettings? taskSettings)
    {
        if (s_window != null && CurrentCapture != null)
        {
            await CurrentCapture.StartStopAsync();
            return;
        }

        // As in ShareX, the options live in the capture settings the workflow uses, so changes are kept.
        TaskSettings settings = taskSettings ?? TaskSettings.GetSafeTaskSettings(SettingsManager.DefaultTaskSettings);
        ScrollingCaptureOptions options = settings.CaptureSettingsReference.ScrollingCaptureOptions ??= new ScrollingCaptureOptions();
        IReadOnlyList<ScrollMethod> methods = PlatformServices.ScrollingCapture?.SupportedScrollMethods ?? Enum.GetValues<ScrollMethod>();

        var viewModel = new ScrollingCaptureViewModel(options, methods)
        {
            SelectTargetRequested = SelectTargetAsync,
            ShowRegionRequested = ShowRegionBorder,
            UploadRequested = image => UploadCapturedImageAsync(taskManager, image, settings),
            SaveOptionsRequested = SettingsManager.SaveWorkflowsConfig
        };
        var window = new ScrollingCaptureWindow { DataContext = viewModel };

        s_window = window;
        CurrentCapture = viewModel;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(s_window, window))
            {
                s_window = null;
                CurrentCapture = null;
            }
        };

        window.Show();
    }

    /// <summary>
    /// ShareX selects the area with region capture and scrolls the window it snapped to, or else the
    /// topmost window under the middle of the area.
    /// </summary>
    private static async Task<ScrollingCaptureTarget?> SelectTargetAsync()
    {
        if (!PlatformServices.IsInitialized)
        {
            return null;
        }

        SKBitmap? background = null;
        try
        {
            background = await PlatformServices.ScreenCapture.CaptureFullScreenAsync(new CaptureOptions { ShowCursor = false });
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "ScrollingCapture background capture");
        }

        try
        {
            var regionCapture = new XerahS.RegionCapture.RegionCaptureService
            {
                Options = new XerahS.RegionCapture.RegionCaptureOptions
                {
                    EnableAnnotations = false,
                    ShowCursor = false,
                    BackgroundImage = background
                }
            };

            var selection = await regionCapture.CaptureRegionAsync();
            if (selection is not { } result || result.Region.Width < 1 || result.Region.Height < 1)
            {
                return null;
            }

            // As in ShareX, whole pixels that cover the selection.
            var region = Rectangle.FromLTRB(
                (int)Math.Floor(result.Region.Left), (int)Math.Floor(result.Region.Top),
                (int)Math.Ceiling(result.Region.Right), (int)Math.Ceiling(result.Region.Bottom));
            IntPtr window = FindWindowUnder(PlatformServices.Window.GetAllWindows(), region);
            return window == IntPtr.Zero ? null : new ScrollingCaptureTarget(window, region);
        }
        finally
        {
            background?.Dispose();
        }
    }

    /// <summary>The topmost visible window containing the middle of the area. Windows are listed topmost first.</summary>
    internal static IntPtr FindWindowUnder(IEnumerable<WindowInfo> windows, Rectangle region)
    {
        var middle = new System.Drawing.Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
        return windows.FirstOrDefault(window => window.IsVisible && !window.IsMinimized && window.Bounds.Contains(middle))?.Handle ?? IntPtr.Zero;
    }

    private static IDisposable? ShowRegionBorder(Rectangle region)
    {
        try
        {
            var border = new ScrollingCaptureRegionWindow(region);
            border.Show();
            return new RegionBorderHandle(border);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "ScrollingCapture region border");
            return null;
        }
    }

    private sealed class RegionBorderHandle(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private static async Task UploadCapturedImageAsync(IDesktopTaskManager taskManager, SKBitmap image, TaskSettings taskSettings)
    {
        try
        {
            // ShareX's "Upload / Save" runs the image through the workflow's after capture tasks.
            await taskManager.StartTask(TaskSettings.GetSafeTaskSettings(taskSettings), image);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "ScrollingCapture upload");
        }
    }
}
