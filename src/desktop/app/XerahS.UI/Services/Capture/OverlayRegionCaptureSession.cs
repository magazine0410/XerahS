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

using Avalonia.Threading;
using SkiaSharp;
using XerahS.Core;
using XerahS.Core.Capture;
using XerahS.Platform.Abstractions;
using XerahS.RegionCapture;
using XerahS.RegionCapture.Models;
using XerahS.RegionCapture.Services;

namespace XerahS.UI.Services.Capture;

internal static class OverlayRegionCaptureSession
{
    internal readonly record struct OverlayRegionCaptureResult(
        SKRectI Selection,
        SKBitmap? AnnotationLayer,
        PixelPoint AnnotationMonitorOrigin);

    public static async Task<SKRectI> SelectRegionAsync(
        IScreenCaptureService platformImpl,
        CaptureOptions? options,
        bool useFastOverlay)
    {
        SKRectI selection = SKRectI.Empty;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                CursorInfo? cursorInfo = null;
                if (options?.ShowCursor == true)
                {
                    try
                    {
                        cursorInfo = await platformImpl.CaptureCursorAsync();
                    }
                    catch
                    {
                        // Ignore cursor capture errors.
                    }
                }

                SKBitmap? backgroundForMagnifier = null;
                if (!useFastOverlay)
                {
                    try
                    {
                        backgroundForMagnifier = await platformImpl.CaptureFullScreenAsync(new CaptureOptions
                        {
                            ShowCursor = false,
                            UseModernCapture = options?.UseModernCapture ?? true,
                            LinuxRegionSelectorPreference = LinuxCaptureOptionsResolver.GetLinuxRegionSelectorPreference(options),
                            MacOSRegionSelectorPreference = options?.MacOSRegionSelectorPreference ??
                                MacOSInteractiveRegionSelectorPreference.Automatic,
                            MacOSPlayCaptureSound = false
                        });
                    }
                    catch
                    {
                        // Ignore background capture errors.
                    }
                }

                var captureService = new RegionCaptureService
                {
                    Options = CreateOverlayOptions(options, backgroundForMagnifier, useFastOverlay, sessionStartUtc: null)
                };

                RegionSelectionResult? result;
                try
                {
                    result = await captureService.CaptureRegionAsync(cursorInfo);
                }
                finally
                {
                    await RegionCaptureAnnotationOptionsStore.PersistAsync();
                }

                if (result is not null)
                {
                    var region = result.Value.Region;
                    selection = new SKRectI((int)region.X, (int)region.Y, (int)region.Right, (int)region.Bottom);
                    RememberLastRegion(selection);
                }
            });
        }
        catch
        {
            // Ignore errors to keep capture resilient.
        }

        return selection;
    }

    public static async Task<OverlayRegionCaptureResult> CaptureRegionAsync(
        CaptureOptions? effectiveOptions,
        DateTime sessionStartUtc,
        bool useFastOverlay,
        SKBitmap? fullScreenBitmap,
        CursorInfo? ghostCursor)
    {
        SKRectI selection = SKRectI.Empty;
        SKBitmap? annotationLayer = null;
        PixelPoint annotationMonitorOrigin = default;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var captureService = new RegionCaptureService
                {
                    Options = CreateOverlayOptions(effectiveOptions, fullScreenBitmap, useFastOverlay, sessionStartUtc)
                };

                RegionSelectionResult? result;
                try
                {
                    result = await captureService.CaptureRegionAsync(ghostCursor);
                }
                finally
                {
                    await RegionCaptureAnnotationOptionsStore.PersistAsync();
                }

                if (result is not null)
                {
                    var region = result.Value.Region;
                    selection = new SKRectI((int)region.X, (int)region.Y, (int)region.Right, (int)region.Bottom);
                    annotationLayer = result.Value.AnnotationLayer;
                    annotationMonitorOrigin = result.Value.MonitorOrigin;
                    RememberLastRegion(selection);
                }
            });
        }
        catch
        {
            // Ignore overlay session errors to preserve existing behavior.
        }

        return new OverlayRegionCaptureResult(selection, annotationLayer, annotationMonitorOrigin);
    }

    internal static XerahS.RegionCapture.RegionCaptureOptions CreateOverlayOptions(
        CaptureOptions? options,
        SKBitmap? backgroundImage,
        bool useFastOverlay,
        DateTime? sessionStartUtc)
    {
        var taskSettings = ResolveTaskSettings(options?.WorkflowId);
        // The settings the workflow runs with: the defaults for each section it does not override.
        var regionOptions = taskSettings?.CaptureSettingsReference.RegionCaptureOptions;
        var advancedSettings = taskSettings is { UseDefaultAdvancedSettings: true } && SettingsManager.DefaultTaskSettings != null
            ? SettingsManager.DefaultTaskSettings.AdvancedSettings
            : taskSettings?.AdvancedSettings;
        IReadOnlyList<CaptureSnapSize> snapSizes = CaptureSnapSize.DefaultPresets;
        if (regionOptions?.SnapSizes is { Count: > 0 } configuredSizes)
        {
            snapSizes = configuredSizes.Select(size => new CaptureSnapSize(size.Width, size.Height)).ToArray();
        }

        return new XerahS.RegionCapture.RegionCaptureOptions
        {
            ActiveMonitorMode = regionOptions?.ActiveMonitorMode ?? false,
            EnableAnnotations = advancedSettings?.RegionCaptureDisableAnnotation != true,
            DimOpacity = regionOptions?.UseDimming == false ? 0 : Math.Clamp(regionOptions?.BackgroundDimStrength ?? 20, 0, 100) / 100d,
            ShowCenterCrosshair = regionOptions?.ShowCenterCrosshair ?? true,
            RightClickAction = MapCaptureAction(regionOptions?.RegionCaptureActionRightClick ?? Core.RegionCaptureAction.RemoveShapeCancelCapture),
            MiddleClickAction = MapCaptureAction(regionOptions?.RegionCaptureActionMiddleClick ?? Core.RegionCaptureAction.SwapToolType),
            X1ClickAction = MapCaptureAction(regionOptions?.RegionCaptureActionX1Click ?? Core.RegionCaptureAction.CaptureFullscreen),
            X2ClickAction = MapCaptureAction(regionOptions?.RegionCaptureActionX2Click ?? Core.RegionCaptureAction.CaptureActiveMonitor),
            LastRegion = LastRegionStore.TryGet(out var lastRegion)
                ? new PixelRect(lastRegion.X, lastRegion.Y, lastRegion.Width, lastRegion.Height)
                : PixelRect.Empty,
            ShowCursor = options?.ShowCursor ?? false,
            BackgroundImage = backgroundImage,
            UseTransparentOverlay = useFastOverlay,
            EditorOptions = RegionCaptureAnnotationOptionsStore.GetEditorOptions(options?.WorkflowId),
            SessionStartUtc = sessionStartUtc,
            QuickCrop = regionOptions?.QuickCrop ?? true,
            DetectControls = regionOptions?.DetectControls ?? true,
            EnableWindowSnapping = regionOptions?.DetectWindows ?? true,
            EnableMagnifier = regionOptions?.ShowMagnifier ?? true,
            UseSquareMagnifier = regionOptions?.UseSquareMagnifier ?? false,
            MagnifierPixelCount = regionOptions?.MagnifierPixelCount ?? 15,
            ShowInfo = regionOptions?.ShowInfo ?? true,
            CustomInfoFormat = regionOptions?.UseCustomInfoText == true ? regionOptions.CustomInfoText : null,
            ShowScreenCrosshair = regionOptions?.ShowScreenCrosshair ?? true,
            SnapSizes = snapSizes,
            SnapDistance = XerahS.Core.RegionCaptureOptions.SnapDistance
        };
    }

    private static XerahS.RegionCapture.RegionCaptureAction MapCaptureAction(Core.RegionCaptureAction action) => action switch
    {
        Core.RegionCaptureAction.CancelCapture => XerahS.RegionCapture.RegionCaptureAction.CancelCapture,
        Core.RegionCaptureAction.RemoveShapeCancelCapture => XerahS.RegionCapture.RegionCaptureAction.RemoveShapeCancelCapture,
        Core.RegionCaptureAction.RemoveShape => XerahS.RegionCapture.RegionCaptureAction.RemoveShape,
        Core.RegionCaptureAction.SwapToolType => XerahS.RegionCapture.RegionCaptureAction.SwapToolType,
        Core.RegionCaptureAction.CaptureFullscreen => XerahS.RegionCapture.RegionCaptureAction.CaptureFullscreen,
        Core.RegionCaptureAction.CaptureActiveMonitor => XerahS.RegionCapture.RegionCaptureAction.CaptureActiveMonitor,
        Core.RegionCaptureAction.CaptureLastRegion => XerahS.RegionCapture.RegionCaptureAction.CaptureLastRegion,
        _ => XerahS.RegionCapture.RegionCaptureAction.None
    };

    private static TaskSettings? ResolveTaskSettings(string? workflowId)
    {
        if (!string.IsNullOrWhiteSpace(workflowId))
        {
            return SettingsManager.GetWorkflowTaskSettings(workflowId);
        }

        return SettingsManager.DefaultTaskSettings;
    }

    private static void RememberLastRegion(SKRectI selection)
    {
        if (selection.Width <= 0 || selection.Height <= 0)
            return;

        LastRegionStore.Set(selection.Left, selection.Top, selection.Width, selection.Height);
    }
}
