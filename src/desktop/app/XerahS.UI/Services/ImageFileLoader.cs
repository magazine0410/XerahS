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
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using XerahS.Common;

namespace XerahS.UI.Services;

/// <summary>Loads images through ImageHelpers.LoadBitmap, so TIFF files and EXIF orientation are handled.</summary>
internal static class ImageFileLoader
{
    public static Bitmap Load(string path, int? width = null)
    {
        using var stream = File.OpenRead(path);
        return Load(stream, width);
    }

    public static Bitmap Load(Stream input, int? width = null)
    {
        using var image = ImageHelpers.LoadBitmap(input, minimumWidth: width ?? 0)
            ?? throw new InvalidDataException("The selected image could not be decoded.");
        using var resized = width is > 0 && image.Width > width
            ? ImageHelpers.ResizeImage(image, width.Value, 0) : null;
        return ToAvaloniaBitmap(resized ?? image);
    }

    /// <summary>Copies the decoded pixels into an Avalonia bitmap without encoding them again.</summary>
    internal static Bitmap ToAvaloniaBitmap(SKBitmap source)
    {
        bool supported = source.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888 &&
            source.AlphaType is SKAlphaType.Premul or SKAlphaType.Unpremul or SKAlphaType.Opaque;
        using var converted = supported ? null : source.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidDataException("The image pixels could not be converted.");
        var bitmap = converted ?? source;

        var format = bitmap.ColorType == SKColorType.Rgba8888 ? PixelFormat.Rgba8888 : PixelFormat.Bgra8888;
        var alpha = bitmap.AlphaType switch
        {
            SKAlphaType.Unpremul => AlphaFormat.Unpremul,
            SKAlphaType.Opaque => AlphaFormat.Opaque,
            _ => AlphaFormat.Premul
        };
        return new Bitmap(format, alpha, bitmap.GetPixels(), new PixelSize(bitmap.Width, bitmap.Height),
            new Vector(96, 96), bitmap.RowBytes);
    }
}
