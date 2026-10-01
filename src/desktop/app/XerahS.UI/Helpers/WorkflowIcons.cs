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

using ShareX.ImageEditor.Presentation.Theming;
using XerahS.Core;

namespace XerahS.UI.Helpers;

public static class WorkflowIcons
{
    public static string GetIcon(WorkflowType job) => job switch
    {
        WorkflowType.MouseHighlighter => LucideIcons.mouse_pointer_click,
        WorkflowType.None => LucideIcons.circle_dashed,
        WorkflowType.FileUpload => LucideIcons.file_up,
        WorkflowType.FolderUpload => LucideIcons.folder_up,
        WorkflowType.ClipboardUpload => LucideIcons.clipboard,
        WorkflowType.ClipboardUploadWithContentViewer => LucideIcons.clipboard_list,
        WorkflowType.UploadText => LucideIcons.file_text,
        WorkflowType.UploadURL => LucideIcons.link,
        WorkflowType.DragDropUpload => LucideIcons.mouse_pointer_2,
        WorkflowType.ShortenURL => LucideIcons.link_2,
        WorkflowType.StopUploads => LucideIcons.circle_stop,
        WorkflowType.PrintScreen => LucideIcons.monitor,
        WorkflowType.ActiveWindow => LucideIcons.app_window,
        WorkflowType.CustomWindow => LucideIcons.scan,
        WorkflowType.ActiveMonitor => LucideIcons.monitor,
        WorkflowType.RectangleRegion => LucideIcons.scan,
        WorkflowType.CustomRegion => LucideIcons.scan_line,
        WorkflowType.LastRegion => LucideIcons.layers,
        WorkflowType.ScrollingCapture => LucideIcons.scroll_text,
        WorkflowType.AutoCapture => LucideIcons.clock,
        WorkflowType.StartAutoCapture => LucideIcons.circle_play,
        WorkflowType.StopAutoCapture => LucideIcons.timer_off,
        WorkflowType.ScreenRecorder => LucideIcons.video,
        WorkflowType.ScreenRecorderActiveWindow => LucideIcons.app_window,
        WorkflowType.ScreenRecorderCustomRegion => LucideIcons.crop,
        WorkflowType.StartScreenRecorder => LucideIcons.circle_play,
        WorkflowType.ScreenRecorderGIF => LucideIcons.film,
        WorkflowType.ScreenRecorderGIFActiveWindow => LucideIcons.film,
        WorkflowType.ScreenRecorderGIFCustomRegion => LucideIcons.crop,
        WorkflowType.StartScreenRecorderGIF => LucideIcons.circle_play,
        WorkflowType.StopScreenRecording => LucideIcons.square_stop,
        WorkflowType.PauseScreenRecording => LucideIcons.circle_pause,
        WorkflowType.AbortScreenRecording => LucideIcons.circle_x,
        WorkflowType.ColorPicker => LucideIcons.palette,
        WorkflowType.ScreenColorPicker => LucideIcons.pipette,
        WorkflowType.Ruler => LucideIcons.ruler,
        WorkflowType.PinToScreen => LucideIcons.pin,
        WorkflowType.PinToScreenFromScreen => LucideIcons.picture_in_picture,
        WorkflowType.PinToScreenFromClipboard => LucideIcons.clipboard,
        WorkflowType.PinToScreenFromFile => LucideIcons.file_image,
        WorkflowType.PinToScreenCloseAll => LucideIcons.pin_off,
        WorkflowType.ImageEditor => LucideIcons.image,
        WorkflowType.ImageBeautifier => LucideIcons.sparkles,
        WorkflowType.ImageEffects => LucideIcons.wand_sparkles,
        WorkflowType.ImageViewer => LucideIcons.eye,
        WorkflowType.BackgroundRemover => LucideIcons.eraser,
        WorkflowType.ImageComparer => LucideIcons.images,
        WorkflowType.IconConverter => LucideIcons.file_image,
        WorkflowType.ImageCombiner => LucideIcons.combine,
        WorkflowType.ImageSplitter => LucideIcons.split,
        WorkflowType.ImageResizer => LucideIcons.maximize_2,
        WorkflowType.ImageConverter => LucideIcons.refresh_cw,
        WorkflowType.ImageWatermark => LucideIcons.stamp,
        WorkflowType.ImageThumbnailer => LucideIcons.shrink,
        WorkflowType.VideoConverter => LucideIcons.file_video,
        WorkflowType.VideoTrimmer => LucideIcons.scissors,
        WorkflowType.VideoThumbnailer => LucideIcons.clapperboard,
        WorkflowType.AnalyzeImage => LucideIcons.bot,
        WorkflowType.OCR => LucideIcons.scan_text,
        WorkflowType.QRCode => LucideIcons.qr_code,
        WorkflowType.QRCodeDecodeFromScreen => LucideIcons.scan_eye,
        WorkflowType.QRCodeScanRegion => LucideIcons.scan_line,
        WorkflowType.HashCheck => LucideIcons.hash,
        WorkflowType.Metadata => LucideIcons.tags,
        WorkflowType.StripMetadata => LucideIcons.file_x,
        WorkflowType.IndexFolder => LucideIcons.folder_tree,
        WorkflowType.ClipboardViewer => LucideIcons.clipboard_list,
        WorkflowType.BorderlessWindow => LucideIcons.frame,
        WorkflowType.ActiveWindowBorderless => LucideIcons.maximize,
        WorkflowType.ActiveWindowTopMost => LucideIcons.panel_top,
        WorkflowType.InspectWindow => LucideIcons.scan_search,
        WorkflowType.NetworkMonitor => LucideIcons.activity,
        WorkflowType.MonitorTest => LucideIcons.monitor,
        WorkflowType.DisableHotkeys => LucideIcons.keyboard_off,
        WorkflowType.OpenMainWindow => LucideIcons.panel_top_open,
        WorkflowType.OpenScreenshotsFolder => LucideIcons.folder_open,
        WorkflowType.OpenHistory => LucideIcons.history,
        WorkflowType.OpenImageHistory => LucideIcons.images,
        WorkflowType.ToggleActionsToolbar => LucideIcons.panel_top,
        WorkflowType.ToggleTrayMenu => LucideIcons.menu,
        WorkflowType.ExitShareX => LucideIcons.log_out,
        WorkflowType.RectangleTransparent => LucideIcons.scan,
        WorkflowType.VideoEditor => LucideIcons.video,
        WorkflowType.AnimatedGifMaker => LucideIcons.film,
        WorkflowType.MediaBrowser => LucideIcons.images,
        _ => LucideIcons.circle
    };
}
