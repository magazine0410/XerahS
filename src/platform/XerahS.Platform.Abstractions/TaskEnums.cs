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

using System.ComponentModel;

namespace XerahS.Core;

[Flags]
public enum AfterCaptureTasks // Localized
{
    None = 0,
    [Description("Show quick task menu")]
    ShowQuickTaskMenu = 1,
    [Description("Show after capture window")]
    ShowAfterCaptureWindow = 1 << 1,
    [Description("Beautify image")]
    BeautifyImage = 1 << 2,
    [Description("Add image effects")]
    AddImageEffects = 1 << 3,
    [Description("Annotate media")]
    AnnotateMedia = 1 << 4,
    [Obsolete("Renamed to AnnotateMedia. Kept for JSON backwards-compatibility.")]
    AnnotateImage = AnnotateMedia,
    [Description("Copy image to clipboard")]
    CopyImageToClipboard = 1 << 5,
    [Description("Pin to screen")]
    PinToScreen = 1 << 6,
    [Description("Print image")]
    SendImageToPrinter = 1 << 7,
    [Description("Save image to file")]
    SaveImageToFile = 1 << 8,
    [Description("Save image to file (with dialog)")]
    SaveImageToFileWithDialog = 1 << 9,
    [Description("Save thumbnail image to file")]
    SaveThumbnailImageToFile = 1 << 10,
    [Description("Perform actions")]
    PerformActions = 1 << 11,
    [Description("Copy file to clipboard")]
    CopyFileToClipboard = 1 << 12,
    [Description("Copy file path to clipboard")]
    CopyFilePathToClipboard = 1 << 13,
    [Description("Show in file manager")]
    ShowInExplorer = 1 << 14,
    [Description("Analyze image")]
    AnalyzeImage = 1 << 15,
    [Description("Scan QR code")]
    ScanQRCode = 1 << 16,
    [Description("Recognize text (OCR)")]
    DoOCR = 1 << 17,
    [Description("Show before-upload window")]
    ShowBeforeUploadWindow = 1 << 18,
    [Description("Upload image to host")]
    UploadImageToHost = 1 << 19,
    [Description("Delete file")]
    DeleteFile = 1 << 20,
    [Description("Copy OCR text to clipboard")]
    CopyOcrTextToClipboard = 1 << 21,
    // Keep existing serialized flag values stable.
    [Description("Copy folder path to clipboard")]
    CopyFolderPathToClipboard = 1 << 22
}

[Flags]
public enum AfterUploadTasks // Localized
{
    None = 0,
    ShowAfterUploadWindow = 1,
    UseURLShortener = 1 << 1,
    ShareURL = 1 << 2,
    CopyURLToClipboard = 1 << 3,
    OpenURL = 1 << 4,
    ShowQRCode = 1 << 5
}

public enum AfterCaptureQuickAction
{
    None,
    CopyImage,
    CopyFilePath
}
