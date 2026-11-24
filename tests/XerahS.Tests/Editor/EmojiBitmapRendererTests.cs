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
using ShareX.ImageEditor.Presentation.Emoji;
using SkiaSharp;

namespace XerahS.Tests.Editor;

[TestFixture]
public class EmojiBitmapRendererTests
{
    // One code point, a skin tone, a flag, and a keycap: the last three are only one glyph after shaping.
    [TestCase("1F600")]
    [TestCase("1F44D 1F3FD")]
    [TestCase("1F1EB 1F1EE")]
    [TestCase("0031 FE0F 20E3")]
    public void RenderStickerBitmap_OffWindows_DrawsTheEmojiWithTheSystemEmojiFont(string unicodeSequence)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows renders emoji with Direct2D.");
        }

        using SKTypeface? emojiFont = SKFontManager.Default.MatchCharacter(null, SKFontStyle.Normal, ["und-Zsye"], 0x1F600);
        if (emojiFont == null)
        {
            Assert.Ignore("This system has no emoji font.");
        }

        using SKBitmap? sticker = WindowsEmojiBitmapRenderer.RenderStickerBitmap(unicodeSequence, 160);

        Assert.That(sticker, Is.Not.Null);
        Assert.That((sticker!.Width, sticker.Height), Is.EqualTo((160, 160)));

        // A missing glyph is drawn as a thin outlined box: it covers little of the sticker, and
        // with gaps. An emoji fills most of it, and one glyph has no fully transparent column
        // between its left and right edges.
        int covered = 0;
        var columnHasInk = new bool[sticker.Width];
        for (int y = 0; y < sticker.Height; y++)
        {
            for (int x = 0; x < sticker.Width; x++)
            {
                if (sticker.GetPixel(x, y).Alpha > 40)
                {
                    covered++;
                    columnHasInk[x] = true;
                }
            }
        }

        int first = Array.IndexOf(columnHasInk, true);
        int last = Array.LastIndexOf(columnHasInk, true);
        Assert.That(covered, Is.GreaterThan(sticker.Width * sticker.Height / 4), "coverage");
        Assert.That(columnHasInk.Skip(first).Take(last - first + 1), Has.All.True, "one glyph, not several side by side");
    }
}
