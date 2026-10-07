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

using BitMiracle.LibTiff.Classic;
using SkiaSharp;

namespace XerahS.Common;

public static partial class ImageHelpers
{
    /// <summary>Loads the first image/frame and applies the file orientation when enabled.</summary>
    /// <param name="minimumWidth">When set, formats that support it (JPEG) decode at the smallest
    /// reduced size that is still at least this wide after orientation.</param>
    public static SKBitmap? LoadBitmap(Stream stream, bool? rotateByExif = null, int minimumWidth = 0)
    {
        try
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            bool rotate = rotateByExif ?? HelpersOptions.RotateImageByExifOrientationData;
            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            if (bytes.Length >= 4 && (bytes[..4].SequenceEqual(new byte[] { 73, 73, 42, 0 }) ||
                bytes[..4].SequenceEqual(new byte[] { 77, 77, 0, 42 })))
            {
                return LoadTiff(buffer, rotate);
            }

            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec == null) return null;
            var bitmap = DecodeReduced(codec, rotate, minimumWidth) ?? SKBitmap.Decode(codec);
            if (bitmap == null || !rotate) return bitmap;
            return OrientOwnedBitmap(bitmap, (int)codec.EncodedOrigin);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Could not decode image.");
            return null;
        }
    }

    private static SKBitmap? DecodeReduced(SKCodec codec, bool rotate, int minimumWidth)
    {
        if (minimumWidth <= 0) return null;
        bool transpose = rotate && (int)codec.EncodedOrigin >= 5;
        int Width(SKSizeI size) => transpose ? size.Height : size.Width;
        int fullWidth = Width(codec.Info.Size);
        for (int eighths = 1; eighths < 8; eighths++)
        {
            var size = codec.GetScaledDimensions(eighths / 8f);
            if (Width(size) < minimumWidth) continue;
            if (Width(size) >= fullWidth) return null;
            var info = codec.Info.WithSize(size.Width, size.Height);
            if (info.AlphaType == SKAlphaType.Unpremul) info.AlphaType = SKAlphaType.Premul;
            return SKBitmap.Decode(codec, info);
        }
        return null;
    }

    private static SKBitmap? LoadTiff(Stream stream, bool rotate)
    {
        using var tiff = Tiff.ClientOpen("image", "r", stream, new TiffStream());
        if (tiff == null) return null;
        int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
        int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        if (width <= 0 || height <= 0) return null;
        int count = checked(width * height);
        int orientation = tiff.GetFieldDefaulted(TiffTag.ORIENTATION)[0].ToInt();
        // Decode stored pixels without libtiff's partial orientation handling. All eight EXIF
        // orientations (including transpose) are handled in one place below.
        tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        var pixels = new int[count];
        if (!tiff.ReadRGBAImageOriented(width, height, pixels, Orientation.TOPLEFT, true)) return null;
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        // libtiff returns associated (premultiplied) RGBA, including for unassociated alpha files.
        var destination = bitmap.GetPixelSpan();
        for (int i = 0; i < pixels.Length; i++)
        {
            int pixel = pixels[i];
            destination[i * 4] = (byte)Tiff.GetR(pixel);
            destination[i * 4 + 1] = (byte)Tiff.GetG(pixel);
            destination[i * 4 + 2] = (byte)Tiff.GetB(pixel);
            destination[i * 4 + 3] = (byte)Tiff.GetA(pixel);
        }
        return rotate ? OrientOwnedBitmap(bitmap, orientation) : bitmap;
    }

    /// <summary>Takes ownership of source and returns pixels in display orientation.</summary>
    internal static SKBitmap OrientOwnedBitmap(SKBitmap source, int orientation)
    {
        if (orientation is < 2 or > 8) return source;
        bool transpose = orientation >= 5;
        var result = new SKBitmap(new SKImageInfo(transpose ? source.Height : source.Width,
            transpose ? source.Width : source.Height, source.ColorType, source.AlphaType, source.ColorSpace));
        using var canvas = new SKCanvas(result);
        var matrix = orientation switch
        {
            2 => new SKMatrix(-1, 0, source.Width, 0, 1, 0, 0, 0, 1),
            3 => new SKMatrix(-1, 0, source.Width, 0, -1, source.Height, 0, 0, 1),
            4 => new SKMatrix(1, 0, 0, 0, -1, source.Height, 0, 0, 1),
            5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            6 => new SKMatrix(0, -1, source.Height, 1, 0, 0, 0, 0, 1),
            7 => new SKMatrix(0, -1, source.Height, -1, 0, source.Width, 0, 0, 1),
            _ => new SKMatrix(0, 1, 0, -1, 0, source.Width, 0, 0, 1)
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        source.Dispose();
        return result;
    }

    public static SKBitmap FillBackground(SKBitmap source, SKColor color)
    {
        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(result);
        canvas.Clear(color);
        canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        return result;
    }

    /// <summary>ShareX's alternative clipboard mode retains alpha regardless of the fill setting.</summary>
    public static SKBitmap? CreateClipboardBackground(SKBitmap source) =>
        HelpersOptions.DefaultCopyImageFillBackground && !HelpersOptions.UseAlternativeClipboardCopyImage
            ? FillBackground(source, SKColors.White) : null;
}
