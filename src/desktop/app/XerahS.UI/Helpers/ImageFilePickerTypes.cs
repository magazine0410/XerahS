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

using Avalonia.Platform.Storage;

namespace XerahS.UI.Helpers;

/// <summary>File picker filters for opening images.</summary>
internal static class ImageFilePickerTypes
{
    /// <summary>
    /// ShareX's "Image files" filter plus WebP. Avalonia's ImageAll lists no TIFF patterns, and the
    /// Linux portal receives only the patterns when a type has any, so TIFF files were hidden.
    /// </summary>
    public static FilePickerFileType Images { get; } = new("Image files")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.jpe", "*.jfif", "*.gif", "*.bmp", "*.tif", "*.tiff", "*.webp"],
        MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff", "image/webp"],
        AppleUniformTypeIdentifiers = FilePickerFileTypes.ImageAll.AppleUniformTypeIdentifiers
    };
}
