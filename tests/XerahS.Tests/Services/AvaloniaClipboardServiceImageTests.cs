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

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input.Platform;
using NUnit.Framework;
using SkiaSharp;
using XerahS.UI.Services;

namespace XerahS.Tests.Services;

[TestFixture, NonParallelizable]
public class AvaloniaClipboardServiceImageTests
{
    [AvaloniaTest]
    public async Task ImageCopiedByXerahS_CanBeReadRepeatedly()
    {
        var window = new Window();
        window.Show();
        try
        {
            IClipboard clipboard = window.Clipboard!;
            var service = new AvaloniaClipboardService(clipboard);
            await AvaloniaClipboardService.SetImageBytesAsync(clipboard, CreatePng());

            // While XerahS owns the clipboard, every reader gets the stored value. Each read disposes
            // what it got, so a shared bitmap broke the next read and the next paste in another app.
            for (int i = 0; i < 3; i++)
            {
                using SKBitmap? image = await service.GetImageAsync();
                Assert.That(image, Is.Not.Null, $"read {i + 1}");
                Assert.That(image!.Width, Is.EqualTo(30));
                Assert.That(image.GetPixel(15, 10), Is.EqualTo(SKColors.Orange));
            }

            using var raw = await clipboard.TryGetBitmapAsync();
            Assert.That(raw, Is.Not.Null);
            Assert.That(raw!.PixelSize.Width, Is.EqualTo(30));
        }
        finally
        {
            window.Close();
        }
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(30, 20);
        bitmap.Erase(SKColors.Orange);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
