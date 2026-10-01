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
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.RegionCapture.ScreenRecording;
using XerahS.UI.Helpers;

namespace XerahS.UI.ViewModels
{
    public partial class TaskSettingsViewModel
    {
        #region Capture Settings

        public bool ShowUseModernCaptureSetting => OperatingSystem.IsWindows();

        public bool IsMacOSPlatform => OperatingSystem.IsMacOS();

        public bool UseModernCapture
        {
            get => CaptureSource.CaptureSettings.UseModernCapture;
            set
            {
                if (CaptureSource.CaptureSettings.UseModernCapture != value)
                {
                    CaptureSource.CaptureSettings.UseModernCapture = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HDRScreenshotColorCorrection
        {
            get => CaptureSource.CaptureSettings.HDRScreenshotColorCorrection;
            set
            {
                if (CaptureSource.CaptureSettings.HDRScreenshotColorCorrection != value)
                {
                    CaptureSource.CaptureSettings.HDRScreenshotColorCorrection = value;
                    OnPropertyChanged();
                }
            }
        }

        public LinuxInteractiveRegionSelectorPreference LinuxRegionSelectorPreference
        {
            get => LinuxRegionSelectorPreferenceSupport.NormalizeForCurrentSession(
                CaptureSource.CaptureSettings.LinuxRegionSelectorPreference);
            set
            {
                if (CaptureSource.CaptureSettings.LinuxRegionSelectorPreference != value)
                {
                    CaptureSource.CaptureSettings.LinuxRegionSelectorPreference = value;
                    OnPropertyChanged();
                }
            }
        }

        public IReadOnlyList<LinuxInteractiveRegionSelectorPreference> LinuxRegionSelectorPreferences =>
            LinuxRegionSelectorPreferenceSupport.GetVisiblePreferences();

        /// <summary>True when the OmaSnap engine passed its probe in this session (XIP0088).</summary>
        public bool IsOmaSnapAvailable => PlatformServices.HostedCaptureEngine?.CurrentStatus.Available == true;

        public bool OmaSnapRegionOnly
        {
            get => CaptureSource.CaptureSettings.OmaSnapRegionOnly;
            set
            {
                if (CaptureSource.CaptureSettings.OmaSnapRegionOnly != value)
                {
                    CaptureSource.CaptureSettings.OmaSnapRegionOnly = value;
                    OnPropertyChanged();
                }
            }
        }

        public MacOSInteractiveRegionSelectorPreference MacOSRegionSelectorPreference
        {
            get => CaptureSource.CaptureSettings.MacOSRegionSelectorPreference;
            set
            {
                if (CaptureSource.CaptureSettings.MacOSRegionSelectorPreference != value)
                {
                    CaptureSource.CaptureSettings.MacOSRegionSelectorPreference = value;
                    OnPropertyChanged();
                }
            }
        }

        public MacOSInteractiveRegionSelectorPreference[] MacOSRegionSelectorPreferences =>
            Enum.GetValues<MacOSInteractiveRegionSelectorPreference>();

        public bool MacOSPlayCaptureSound
        {
            get => CaptureSource.CaptureSettings.MacOSPlayCaptureSound;
            set
            {
                if (CaptureSource.CaptureSettings.MacOSPlayCaptureSound != value)
                {
                    CaptureSource.CaptureSettings.MacOSPlayCaptureSound = value;
                    OnPropertyChanged();
                }
            }
        }

        public LinuxRecordingBackendPreference LinuxRecordingBackendPreference
        {
            get => ResolveLinuxRecordingBackendPreference(CaptureSource.CaptureSettings);
            set
            {
                if (ResolveLinuxRecordingBackendPreference(CaptureSource.CaptureSettings) != value ||
                    CaptureSource.CaptureSettings.LinuxRecordingBackendPreference == null)
                {
                    CaptureSource.CaptureSettings.LinuxRecordingBackendPreference = value;
                    OnPropertyChanged();
                }
            }
        }

        public LinuxRecordingBackendPreference[] LinuxRecordingBackendPreferences =>
            Enum.GetValues<LinuxRecordingBackendPreference>();

        public bool ShowCursor
        {
            get => CaptureSource.CaptureSettings.ShowCursor;
            set
            {
                if (CaptureSource.CaptureSettings.ShowCursor != value)
                {
                    CaptureSource.CaptureSettings.ShowCursor = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureQuickCapture
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.QuickCrop;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.QuickCrop != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.QuickCrop = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureActiveMonitorMode
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureDisableAnnotation
        {
            get => AdvancedSource.AdvancedSettings.RegionCaptureDisableAnnotation;
            set
            {
                if (AdvancedSource.AdvancedSettings.RegionCaptureDisableAnnotation != value)
                {
                    AdvancedSource.AdvancedSettings.RegionCaptureDisableAnnotation = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureUseDimming
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.UseDimming;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.UseDimming != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.UseDimming = value;
                    OnPropertyChanged();
                }
            }
        }

        public int RegionCaptureBackgroundDimStrength
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.BackgroundDimStrength;
            set
            {
                int clamped = Math.Clamp(value, 0, 100);
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.BackgroundDimStrength != clamped)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.BackgroundDimStrength = clamped;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureShowCenterCrosshair
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.ShowCenterCrosshair;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.ShowCenterCrosshair != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.ShowCenterCrosshair = value;
                    OnPropertyChanged();
                }
            }
        }

        public RegionCaptureAction[] RegionCaptureMouseActions => Enum.GetValues<RegionCaptureAction>();

        public RegionCaptureAction RegionCaptureRightClickAction
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionRightClick;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionRightClick != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionRightClick = value;
                    OnPropertyChanged();
                }
            }
        }

        public RegionCaptureAction RegionCaptureMiddleClickAction
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionMiddleClick;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionMiddleClick != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionMiddleClick = value;
                    OnPropertyChanged();
                }
            }
        }

        public RegionCaptureAction RegionCaptureX1ClickAction
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionX1Click;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionX1Click != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionX1Click = value;
                    OnPropertyChanged();
                }
            }
        }

        public RegionCaptureAction RegionCaptureX2ClickAction
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionX2Click;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionX2Click != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.RegionCaptureActionX2Click = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureShowMagnifier
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.ShowMagnifier;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.ShowMagnifier != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.ShowMagnifier = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureUseSquareMagnifier
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.UseSquareMagnifier;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.UseSquareMagnifier != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.UseSquareMagnifier = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureShowInfo
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.ShowInfo;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.ShowInfo != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.ShowInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureUseCustomInfoText
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.UseCustomInfoText;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.UseCustomInfoText != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.UseCustomInfoText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RegionCaptureShowScreenCrosshair
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.ShowScreenCrosshair;
            set
            {
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.ShowScreenCrosshair != value)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.ShowScreenCrosshair = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Custom HUD text; line breaks are stored as the $n token, as in ShareX.</summary>
        public string RegionCaptureCustomInfoText
        {
            get => (CaptureSource.CaptureSettings.RegionCaptureOptions.CustomInfoText ?? string.Empty).Replace("$n", Environment.NewLine);
            set
            {
                string stored = (value ?? string.Empty).Replace("\r\n", "$n").Replace("\n", "$n");
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.CustomInfoText != stored)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.CustomInfoText = stored;
                    OnPropertyChanged();
                }
            }
        }

        public int RegionCaptureMagnifierPixelCount
        {
            get => CaptureSource.CaptureSettings.RegionCaptureOptions.MagnifierPixelCount;
            set
            {
                int clamped = Math.Clamp(value, RegionCaptureOptions.MagnifierPixelCountMinimum, RegionCaptureOptions.MagnifierPixelCountMaximum) | 1;
                if (CaptureSource.CaptureSettings.RegionCaptureOptions.MagnifierPixelCount != clamped)
                {
                    CaptureSource.CaptureSettings.RegionCaptureOptions.MagnifierPixelCount = clamped;
                    OnPropertyChanged();
                }
            }
        }

        public decimal ScreenshotDelay
        {
            get => CaptureSource.CaptureSettings.ScreenshotDelay;
            set
            {
                if (CaptureSource.CaptureSettings.ScreenshotDelay != value)
                {
                    CaptureSource.CaptureSettings.ScreenshotDelay = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CaptureTransparent
        {
            get => CaptureSource.CaptureSettings.CaptureTransparent;
            set
            {
                if (CaptureSource.CaptureSettings.CaptureTransparent != value)
                {
                    CaptureSource.CaptureSettings.CaptureTransparent = value;
                    // Shadow depends on transparent often, but UI handles enabling.
                    OnPropertyChanged();
                }
            }
        }

        public bool CaptureShadow
        {
            get => CaptureSource.CaptureSettings.CaptureShadow;
            set
            {
                if (CaptureSource.CaptureSettings.CaptureShadow != value)
                {
                    CaptureSource.CaptureSettings.CaptureShadow = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CaptureClientArea
        {
            get => CaptureSource.CaptureSettings.CaptureClientArea;
            set
            {
                if (CaptureSource.CaptureSettings.CaptureClientArea != value)
                {
                    CaptureSource.CaptureSettings.CaptureClientArea = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ScreenRecordFPS
        {
            get => CaptureSource.CaptureSettings.ScreenRecordFPS;
            set
            {
                if (CaptureSource.CaptureSettings.ScreenRecordFPS != value)
                {
                    CaptureSource.CaptureSettings.ScreenRecordFPS = value;
                    OnPropertyChanged();
                }
            }
        }

        public float ScreenRecordDuration
        {
            get => CaptureSource.CaptureSettings.ScreenRecordDuration;
            set
            {
                if (Math.Abs(CaptureSource.CaptureSettings.ScreenRecordDuration - value) > 0.001f)
                {
                    CaptureSource.CaptureSettings.ScreenRecordDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public float ScreenRecordStartDelay
        {
            get => CaptureSource.CaptureSettings.ScreenRecordStartDelay;
            set
            {
                if (Math.Abs(CaptureSource.CaptureSettings.ScreenRecordStartDelay - value) > 0.001f)
                {
                    CaptureSource.CaptureSettings.ScreenRecordStartDelay = value;
                    OnPropertyChanged();
                }
            }
        }

        public IEnumerable<RecordingIntent> RecordingIntents => Enum.GetValues(typeof(RecordingIntent)).Cast<RecordingIntent>();
        public IEnumerable<FFmpegVideoCodec> VideoCodecs => Enum.GetValues(typeof(FFmpegVideoCodec)).Cast<FFmpegVideoCodec>();

        public FFmpegVideoCodec ScreenRecordVideoCodec
        {
            get => CaptureSource.CaptureSettings.FFmpegOptions?.VideoCodec ?? FFmpegVideoCodec.libx264;
            set
            {
                CaptureSource.CaptureSettings.FFmpegOptions ??= new XerahS.Core.FFmpegOptions();
                if (CaptureSource.CaptureSettings.FFmpegOptions.VideoCodec != value)
                {
                    CaptureSource.CaptureSettings.FFmpegOptions.VideoCodec = value;
                    OnPropertyChanged();
                }
            }
        }

        public RecordingIntent RecordingIntent
        {
            get => CaptureSource.CaptureSettings.ScreenRecordingSettings.RecordingIntent;
            set
            {
                if (CaptureSource.CaptureSettings.ScreenRecordingSettings.RecordingIntent != value)
                {
                    CaptureSource.CaptureSettings.ScreenRecordingSettings.RecordingIntent = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CaptureAutoHideTaskbar
        {
            get => CaptureSource.CaptureSettings.CaptureAutoHideTaskbar;
            set
            {
                if (CaptureSource.CaptureSettings.CaptureAutoHideTaskbar != value)
                {
                    CaptureSource.CaptureSettings.CaptureAutoHideTaskbar = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CaptureCustomWindow
        {
            get => CaptureSource.CaptureSettings.CaptureCustomWindow;
            set
            {
                if (CaptureSource.CaptureSettings.CaptureCustomWindow != value)
                {
                    XerahS.Common.DebugHelper.WriteLine($"[DEBUG] Setting CaptureCustomWindow to: '{value}'");
                    CaptureSource.CaptureSettings.CaptureCustomWindow = value;
                    OnPropertyChanged();
                }
            }
        }

        private static LinuxRecordingBackendPreference ResolveLinuxRecordingBackendPreference(TaskSettingsCapture captureSettings)
        {
            return captureSettings.LinuxRecordingBackendPreference ??
                (captureSettings.UseModernCapture
                    ? LinuxRecordingBackendPreference.Automatic
                    : LinuxRecordingBackendPreference.FFmpeg);
        }

        #endregion
    }
}
