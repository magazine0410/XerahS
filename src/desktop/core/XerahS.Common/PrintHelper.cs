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

using SkiaSharp;

namespace XerahS.Common
{
    /// <summary>Where the image goes on the page, in hundredths of an inch.</summary>
    public readonly record struct PrintImageLayout(bool Rotate, SKRect Destination, SKRect Source);

    /// <summary>
    /// ShareX's PrintHelper.PrintImage page layout, drawn to PDF for the platform print service.
    /// As in ShareX (GDI+ printing), page units are hundredths of an inch and one image pixel is one unit.
    /// </summary>
    public static class PrintHelper
    {
        public const float PointsPerUnit = 0.72f;

        public static PrintImageLayout GetLayout(int imageWidth, int imageHeight, float pageWidth, float pageHeight, PrintSettings settings)
        {
            var rect = new SKRect(settings.Margin, settings.Margin, pageWidth - settings.Margin, pageHeight - settings.Margin);
            if (rect.Width <= 0 || rect.Height <= 0) rect = new SKRect(0, 0, pageWidth, pageHeight);

            bool rotate = settings.AutoRotateImage &&
                ((rect.Width > rect.Height && imageWidth < imageHeight) || (rect.Width < rect.Height && imageWidth > imageHeight));
            float width = rotate ? imageHeight : imageWidth;
            float height = rotate ? imageWidth : imageHeight;

            if (!settings.AutoScaleImage)
            {
                // ShareX draws the image at its own size from the top left of the margin, cut off at the margin.
                float visibleWidth = Math.Min(width, rect.Width), visibleHeight = Math.Min(height, rect.Height);
                return new PrintImageLayout(rotate, SKRect.Create(rect.Left, rect.Top, visibleWidth, visibleHeight),
                    SKRect.Create(0, 0, visibleWidth, visibleHeight));
            }

            double ratio;
            float newWidth, newHeight;
            if (!settings.AllowEnlargeImage && width <= rect.Width && height <= rect.Height)
            {
                ratio = 1.0;
                newWidth = width;
                newHeight = height;
            }
            else
            {
                ratio = Math.Min(rect.Width / width, rect.Height / height);
                newWidth = (float)(width * ratio);
                newHeight = (float)(height * ratio);
            }

            float x = rect.Left, y = rect.Top;
            if (settings.CenterImage)
            {
                x += (float)((rect.Width - width * ratio) / 2);
                y += (float)((rect.Height - height * ratio) / 2);
            }

            return new PrintImageLayout(rotate, SKRect.Create(x, y, newWidth, newHeight), SKRect.Create(0, 0, width, height));
        }

        /// <summary>Draws the page onto a canvas whose units are hundredths of an inch.</summary>
        public static void DrawPage(SKCanvas canvas, SKBitmap image, PrintSettings settings, float pageWidth, float pageHeight)
        {
            PrintImageLayout layout = GetLayout(image.Width, image.Height, pageWidth, pageHeight, settings);
            using SKBitmap? rotated = layout.Rotate ? Rotate90(image) : null;
            using SKImage source = SKImage.FromBitmap(rotated ?? image);
            canvas.DrawImage(source, layout.Source, layout.Destination, new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        /// <summary>A one-page PDF of the given size in points (1/72 inch), as the print portal and CUPS expect.</summary>
        public static byte[] CreatePdf(SKBitmap image, PrintSettings settings, double pageWidthPoints, double pageHeightPoints, string title)
        {
            using var stream = new MemoryStream();
            // Start from Skia's defaults: a new SKDocumentPdfMetadata has JPEG quality 0. Screenshots are stored lossless.
            SKDocumentPdfMetadata metadata = SKDocumentPdfMetadata.Default;
            metadata.Title = title;
            metadata.Creator = "XerahS";
            metadata.EncodingQuality = 101;
            using (var document = SKDocument.CreatePdf(stream, metadata))
            {
                SKCanvas canvas = document.BeginPage((float)pageWidthPoints, (float)pageHeightPoints);
                canvas.Scale(PointsPerUnit);
                DrawPage(canvas, image, settings, (float)(pageWidthPoints / PointsPerUnit), (float)(pageHeightPoints / PointsPerUnit));
                document.EndPage();
                document.Close();
            }
            return stream.ToArray();
        }

        /// <summary>The page as an image for the print preview, with a white sheet and a grey border.</summary>
        public static SKBitmap RenderPreview(SKBitmap image, PrintSettings settings, double pageWidthPoints, double pageHeightPoints, int maxSize = 1400)
        {
            float pageWidth = (float)(pageWidthPoints / PointsPerUnit), pageHeight = (float)(pageHeightPoints / PointsPerUnit);
            float scale = maxSize / Math.Max(pageWidth, pageHeight);
            var preview = new SKBitmap(Math.Max(1, (int)Math.Round(pageWidth * scale)), Math.Max(1, (int)Math.Round(pageHeight * scale)));
            using var canvas = new SKCanvas(preview);
            canvas.Clear(SKColors.White);
            canvas.Save();
            canvas.Scale(scale);
            DrawPage(canvas, image, settings, pageWidth, pageHeight);
            canvas.Restore();
            using var border = new SKPaint { Color = new SKColor(160, 160, 160), Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawRect(1, 1, preview.Width - 2, preview.Height - 2, border);
            return preview;
        }

        /// <summary>Rotates 90 degrees clockwise, like GDI+'s Rotate90FlipNone that ShareX uses.</summary>
        private static SKBitmap Rotate90(SKBitmap image)
        {
            var rotated = new SKBitmap(new SKImageInfo(image.Height, image.Width, image.ColorType, image.AlphaType));
            using var canvas = new SKCanvas(rotated);
            canvas.Translate(image.Height, 0);
            canvas.RotateDegrees(90);
            canvas.DrawBitmap(image, 0, 0, new SKSamplingOptions());
            return rotated;
        }
    }
}
