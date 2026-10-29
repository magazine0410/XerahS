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
using XerahS.Common;
using System.Collections.Generic;
using System.Linq;

namespace XerahS.Media
{
    public enum ImageCombinerOrientation
    {
        Vertical,
        Horizontal
    }

    public enum ImageCombinerAlignment
    {
        Near,
        Center,
        Far
    }

    public class ImageCombiner
    {
        /// <summary>
        /// ShareX's ImageHelpers.CombineImages. Images go in a row (horizontal) or a column (vertical); with
        /// <paramref name="wrapAfter"/> above 0, a new row or column starts after that many images. Each row is as
        /// high as its highest image (each column as wide as its widest), and <paramref name="alignment"/> places an
        /// image within it. <paramref name="spacing"/> separates both images and rows or columns.
        /// <paramref name="autoFillBackground"/> fills the background with the first image's bottom-right pixel.
        /// </summary>
        public SKBitmap? Combine(IEnumerable<SKBitmap> images, ImageCombinerOrientation orientation, int spacing = 0,
            ImageCombinerAlignment alignment = ImageCombinerAlignment.Center, SKColor? backgroundColor = null,
            int wrapAfter = 0, bool autoFillBackground = false)
        {
            List<SKBitmap> imageList = images?.Where(i => i != null).ToList() ?? new List<SKBitmap>();
            if (imageList.Count == 0)
            {
                return null;
            }

            var imageRects = new List<SKRectI>(imageList.Count);
            int positionX = 0, positionY = 0, currentSize = 0;

            for (int i = 0; i < imageList.Count; i++)
            {
                SKBitmap image = imageList[i];
                int offsetX = 0, offsetY = 0;

                if (orientation == ImageCombinerOrientation.Horizontal)
                {
                    if (wrapAfter > 0)
                    {
                        if (i % wrapAfter == 0)
                        {
                            if (i > 0)
                            {
                                positionX = 0;
                                positionY += currentSize + spacing;
                            }

                            currentSize = imageList.Skip(i).Take(wrapAfter).Max(x => x.Height);
                        }
                    }
                    else if (i == 0)
                    {
                        currentSize = imageList.Max(x => x.Height);
                    }

                    offsetY = alignment switch
                    {
                        ImageCombinerAlignment.Center => (currentSize / 2) - (image.Height / 2),
                        ImageCombinerAlignment.Far => currentSize - image.Height,
                        _ => 0
                    };

                    imageRects.Add(SKRectI.Create(positionX, positionY + offsetY, image.Width, image.Height));
                    positionX += image.Width + spacing;
                }
                else
                {
                    if (wrapAfter > 0)
                    {
                        if (i % wrapAfter == 0)
                        {
                            if (i > 0)
                            {
                                positionY = 0;
                                positionX += currentSize + spacing;
                            }

                            currentSize = imageList.Skip(i).Take(wrapAfter).Max(x => x.Width);
                        }
                    }
                    else if (i == 0)
                    {
                        currentSize = imageList.Max(x => x.Width);
                    }

                    offsetX = alignment switch
                    {
                        ImageCombinerAlignment.Center => (currentSize / 2) - (image.Width / 2),
                        ImageCombinerAlignment.Far => currentSize - image.Width,
                        _ => 0
                    };

                    imageRects.Add(SKRectI.Create(positionX + offsetX, positionY, image.Width, image.Height));
                    positionY += image.Height + spacing;
                }
            }

            // As ShareX's Combine: the canvas is the union of the image rectangles.
            int left = imageRects.Min(r => r.Left), top = imageRects.Min(r => r.Top);
            int width = imageRects.Max(r => r.Right) - left, height = imageRects.Max(r => r.Bottom) - top;

            var result = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(result))
            {
                SKColor background = backgroundColor ?? SKColors.Transparent;
                if (autoFillBackground)
                {
                    SKBitmap first = imageList[0];
                    background = first.GetPixel(first.Width - 1, first.Height - 1);
                }
                canvas.Clear(background);

                for (int i = 0; i < imageList.Count; i++)
                {
                    canvas.DrawBitmap(imageList[i], imageRects[i].Left - left, imageRects[i].Top - top);
                }
            }

            return result;
        }

        public SKBitmap? Combine(IEnumerable<string> filePaths, ImageCombinerOrientation orientation, int spacing = 0, ImageCombinerAlignment alignment = ImageCombinerAlignment.Center, SKColor? backgroundColor = null,
            int wrapAfter = 0, bool autoFillBackground = false)
        {
            var bitmaps = new List<SKBitmap>();
            try
            {
                foreach (var path in filePaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        var bmp = XerahS.Common.ImageHelpers.LoadBitmap(path);
                        if (bmp != null)
                        {
                            bitmaps.Add(bmp);
                        }
                    }
                }

                return Combine(bitmaps, orientation, spacing, alignment, backgroundColor, wrapAfter, autoFillBackground);
            }
            finally
            {
                // Cleanup loaded bitmaps
                foreach (var bmp in bitmaps)
                {
                    bmp.Dispose();
                }
            }
        }
    }
}
