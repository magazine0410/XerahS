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

using System.Drawing;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux;
using XerahS.Platform.Linux.Capture.Kde;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Tests.Platform.Linux;

[TestFixture]
public class KdeScreenCaptureTests
{
    [TestCase(true, true, false, true), TestCase(false, true, false, false)]
    [TestCase(true, false, false, false), TestCase(true, true, true, false)]
    public void WindowOptionsRespectClientPrecedence(bool transparent, bool shadow, bool client, bool includesShadow)
    {
        var options = KdeDbusScreenCapture.BuildKdeScreenShotOptions(KdeDbusScreenCapture.KdeCaptureKind.Window,
            new CaptureOptions { CaptureTransparent = transparent, CaptureShadow = shadow, CaptureClientArea = client, ShowCursor = false });
        Assert.Multiple(() =>
        {
            Assert.That(options["include-shadow"], Is.EqualTo(includesShadow));
            Assert.That(options["include-decoration"], Is.EqualTo(!client));
            Assert.That(options["include-cursor"], Is.EqualTo(false));
        });
    }

    [Test]
    public void DirectAreaDoesNotHideTheWindowBeingCaptured()
    {
        var options = KdeDbusScreenCapture.BuildKdeScreenShotOptions(KdeDbusScreenCapture.KdeCaptureKind.Area, null);
        Assert.That(options["hide-caller-windows"], Is.EqualTo(false));
    }

    [TestCase(6u, false), TestCase(18u, true)]
    public void DecodePreservesPremultipliedAlphaAndRowPadding(uint format, bool rgba)
    {
        byte[] raw = rgba ? [32, 16, 8, 64, 99, 99, 99, 99] : [8, 16, 32, 64, 99, 99, 99, 99];
        using var bitmap = KdeDbusScreenCapture.DecodeKdeRawBitmap(raw, 1, 1, 8, format);
        Assert.That(bitmap, Is.Not.Null);
        Assert.That(bitmap!.AlphaType, Is.EqualTo(SKAlphaType.Premul));
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var restored = SKBitmap.Decode(png);
        Assert.That(restored.GetPixel(0, 0).Alpha, Is.EqualTo(64));
        Assert.That(restored.GetPixel(0, 0).Red, Is.InRange(127, 129));
    }

    [TestCase(1, 1, 3), TestCase(2, 1, 4), TestCase(int.MaxValue, 1, int.MaxValue)]
    [TestCase(1, 3, 4), TestCase(-1, 1, 4)]
    public void DecodeRejectsMalformedBuffers(int width, int height, int stride) =>
        Assert.That(KdeDbusScreenCapture.DecodeKdeRawBitmap(new byte[8], width, height, stride, 6), Is.Null);

    [Test]
    public void DirectAreaConvertsXwaylandUnitsOnceAndPreservesNegativeOrigins()
    {
        var rect = new SKRect(-301, -15, 299, 285);
        Assert.That(LinuxScreenCaptureService.CreateKdeAreaCaptureRect(rect, false, 1.5),
            Is.EqualTo(Rectangle.FromLTRB(-201, -10, 200, 190)));
        Assert.That(LinuxScreenCaptureService.CreateKdeAreaCaptureRect(rect, true, 1.5),
            Is.EqualTo(Rectangle.FromLTRB(-301, -15, 299, 285)));
        Assert.That(LinuxScreenCaptureService.CreateKdeAreaCaptureRect(new SKRect(float.NaN, 0, 5, 5), false, 1), Is.EqualTo(Rectangle.Empty));
    }

    [Test]
    public void AuthorizationRefusalsStopRetryingAfterOneCaptureUsesEveryRetry()
    {
        var retry = new KdeAuthorizationRetry(maxRetries: 4);
        Assert.That(Enumerable.Range(0, 4).All(attempt => retry.ShouldRetry(true, attempt)), Is.True);
        Assert.That(retry.ShouldRetry(true, 4), Is.False);
        Assert.That(retry.ShouldRetry(true, 0), Is.False, "A later capture falls back at once.");
    }

    [Test]
    public void AuthorizationRefusalsAreNotRetriedAfterACaptureSucceeds()
    {
        var retry = new KdeAuthorizationRetry();
        Assert.That(retry.ShouldRetry(true, 0), Is.True);
        retry.ReportAuthorized();
        Assert.That(retry.ShouldRetry(true, 0), Is.False);
    }

    [Test]
    public void AuthorizationRefusalsAreNotRetriedWithoutARegistration() =>
        Assert.That(new KdeAuthorizationRetry().ShouldRetry(false, 0), Is.False);
}
