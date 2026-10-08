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

using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core.Services;

namespace XerahS.Tests.Common;

[TestFixture]
public class ImageHelpersCropTests
{
    [Test]
    public void Crop_ReturnsAnIndependentBitmapThatQrDecodingReads()
    {
        Assert.That(QrCodeService.TryGenerate("xerahs-crop-test", 300, out SKBitmap? qr, out string? error), Is.True, error);
        using (qr)
        {
            // A region capture is cut out of a full screenshot.
            using var screen = new SKBitmap(1920, 1080);
            using (var canvas = new SKCanvas(screen))
            {
                canvas.Clear(SKColors.DimGray);
                canvas.DrawBitmap(qr!, 1300, 500, SKSamplingOptions.Default);
            }

            using SKBitmap cropped = ImageHelpers.Crop(screen, new SKRectI(1280, 480, 1620, 820));

            Assert.That(cropped.Width, Is.EqualTo(340));
            Assert.That(cropped.RowBytes, Is.EqualTo(cropped.Width * cropped.BytesPerPixel));
            Assert.That(cropped.GetPixel(0, 0), Is.EqualTo(SKColors.DimGray));
            Assert.That(QrCodeService.Decode(cropped, out string? decodeError), Is.EqualTo(new[] { "xerahs-crop-test" }), decodeError);

            // The crop keeps its pixels when the screenshot changes.
            screen.Erase(SKColors.Red);
            Assert.That(cropped.GetPixel(0, 0), Is.EqualTo(SKColors.DimGray));
        }
    }
}
