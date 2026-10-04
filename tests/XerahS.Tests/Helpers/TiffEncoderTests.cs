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

using System.Buffers.Binary;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Services.Abstractions;

namespace XerahS.Tests.Helpers;

public class TiffEncoderTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-tiff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void OpaqueImage_IsWrittenAsLzwRgbAt96Dpi()
    {
        using var bitmap = CreateBitmap(37, 23, (x, y) => new SKColor((byte)(x * 7), (byte)(y * 11), (byte)(x + y), 255));

        var tiff = TiffImage.Read(TiffEncoder.Encode(bitmap));

        Assert.That(tiff.Compression, Is.EqualTo(5));
        Assert.That(tiff.Photometric, Is.EqualTo(2));
        Assert.That(tiff.SamplesPerPixel, Is.EqualTo(3));
        Assert.That(tiff.BitsPerSample, Is.EqualTo(new[] { 8, 8, 8 }));
        Assert.That(tiff.ExtraSamples, Is.Null);
        Assert.That(tiff.XResolution, Is.EqualTo(96.0));
        Assert.That(tiff.YResolution, Is.EqualTo(96.0));
        Assert.That(tiff.ResolutionUnit, Is.EqualTo(2));
        AssertPixels(tiff, bitmap);
    }

    [Test]
    public void TransparentImage_KeepsAnUnassociatedAlphaChannel()
    {
        using var bitmap = CreateBitmap(16, 9, (x, y) => new SKColor((byte)(x * 15), 200, (byte)(y * 25), (byte)(x * 16 + y)));

        var tiff = TiffImage.Read(TiffEncoder.Encode(bitmap));

        Assert.That(tiff.SamplesPerPixel, Is.EqualTo(4));
        Assert.That(tiff.BitsPerSample, Is.EqualTo(new[] { 8, 8, 8, 8 }));
        Assert.That(tiff.ExtraSamples, Is.EqualTo(2));
        AssertPixels(tiff, bitmap);
    }

    [Test]
    public void NoisyImage_SplitsIntoStripsAndRestartsTheCodeTable()
    {
        // Random pixels fill the 4094-code table many times, so every code width and the clear code are used.
        var random = new Random(1234);
        using var bitmap = CreateBitmap(301, 211, (_, _) => new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255));

        var tiff = TiffImage.Read(TiffEncoder.Encode(bitmap));

        Assert.That(tiff.StripCount, Is.GreaterThan(1));
        AssertPixels(tiff, bitmap);
    }

    [Test]
    public void SingleColorImage_EncodesLongRepeatedSequences()
    {
        using var bitmap = CreateBitmap(1920, 1080, (_, _) => new SKColor(30, 60, 90, 255));

        byte[] data = TiffEncoder.Encode(bitmap);

        Assert.That(data.Length, Is.LessThan(1920 * 1080 * 3 / 20));
        AssertPixels(TiffImage.Read(data), bitmap);
    }

    [Test]
    public void SaveBitmap_WritesTiffForTifAndTiffExtensions()
    {
        using var bitmap = CreateBitmap(5, 4, (x, y) => new SKColor((byte)x, (byte)y, 0, 255));
        foreach (string name in new[] { "a.tif", "b.TIFF" })
        {
            string path = Path.Combine(_directory, name);
            ImageHelpers.SaveBitmap(bitmap, path);
            AssertPixels(TiffImage.Read(File.ReadAllBytes(path)), bitmap);
        }
    }

    [Test]
    public void SaveImageAsStream_EncodesTiffForUploads()
    {
        using var bitmap = CreateBitmap(5, 4, (x, y) => new SKColor((byte)x, (byte)y, 9, 255));

        using var stream = TaskHelpers.SaveImageAsStream(bitmap, EImageFormat.TIFF);

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream!.Position, Is.Zero);
        AssertPixels(TiffImage.Read(stream.ToArray()), bitmap);
    }

    [Test]
    public void SaveImageToPath_ReplacesTheFileWithATiff()
    {
        var settings = new TaskSettings();
        settings.ImageSettings.ImageFormat = EImageFormat.TIFF;
        string path = Path.Combine(_directory, "saved.tif");
        File.WriteAllText(path, "original");
        using var bitmap = CreateBitmap(6, 3, (x, y) => new SKColor(1, (byte)x, (byte)y, 255));

        TaskHelpers.SaveImageToPath(bitmap, path, settings);

        AssertPixels(TiffImage.Read(File.ReadAllBytes(path)), bitmap);
    }

    private static SKBitmap CreateBitmap(int width, int height, Func<int, int, SKColor> color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, color(x, y));
            }
        }

        return bitmap;
    }

    private static void AssertPixels(TiffImage tiff, SKBitmap expected)
    {
        Assert.That(tiff.Width, Is.EqualTo(expected.Width));
        Assert.That(tiff.Height, Is.EqualTo(expected.Height));
        int samples = tiff.SamplesPerPixel;
        Assert.That(tiff.Pixels.Length, Is.EqualTo(expected.Width * expected.Height * samples));
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                SKColor color = expected.GetPixel(x, y);
                int i = (y * expected.Width + x) * samples;
                var actual = new SKColor(tiff.Pixels[i], tiff.Pixels[i + 1], tiff.Pixels[i + 2], samples == 4 ? tiff.Pixels[i + 3] : (byte)255);
                if (actual != color)
                {
                    Assert.Fail($"Pixel ({x}, {y}) is {actual}, expected {color}.");
                }
            }
        }
    }

    /// <summary>
    /// A reader for the little-endian, LZW-compressed, chunky 8-bit TIFF files the encoder writes.
    /// </summary>
    private sealed class TiffImage
    {
        public int Width, Height, Compression, Photometric, SamplesPerPixel, ResolutionUnit, StripCount;
        public int[] BitsPerSample = [];
        public int? ExtraSamples;
        public double XResolution, YResolution;
        public byte[] Pixels = [];

        public static TiffImage Read(byte[] data)
        {
            Assert.That(data[0], Is.EqualTo((byte)'I'));
            Assert.That(data[1], Is.EqualTo((byte)'I'));
            Assert.That(U16(data, 2), Is.EqualTo(42));
            int directory = (int)U32(data, 4);
            Assert.That(directory % 2, Is.Zero, "The directory must start on a word boundary.");

            var tiff = new TiffImage();
            int rowsPerStrip = 0;
            uint[] stripOffsets = [], stripByteCounts = [];
            int count = U16(data, directory);
            int previousTag = -1;
            for (int e = 0; e < count; e++)
            {
                int entry = directory + 2 + e * 12;
                int tag = U16(data, entry);
                Assert.That(tag, Is.GreaterThan(previousTag), "Directory entries must be sorted.");
                previousTag = tag;
                uint[] values = Values(data, entry);
                switch (tag)
                {
                    case 256: tiff.Width = (int)values[0]; break;
                    case 257: tiff.Height = (int)values[0]; break;
                    case 258: tiff.BitsPerSample = values.Select(v => (int)v).ToArray(); break;
                    case 259: tiff.Compression = (int)values[0]; break;
                    case 262: tiff.Photometric = (int)values[0]; break;
                    case 273: stripOffsets = values; break;
                    case 277: tiff.SamplesPerPixel = (int)values[0]; break;
                    case 278: rowsPerStrip = (int)values[0]; break;
                    case 279: stripByteCounts = values; break;
                    case 282: tiff.XResolution = (double)values[0] / values[1]; break;
                    case 283: tiff.YResolution = (double)values[0] / values[1]; break;
                    case 284: Assert.That(values[0], Is.EqualTo(1)); break;
                    case 296: tiff.ResolutionUnit = (int)values[0]; break;
                    case 338: tiff.ExtraSamples = (int)values[0]; break;
                }
            }

            Assert.That(U32(data, directory + 2 + count * 12), Is.Zero, "Only one image is expected.");
            Assert.That(stripOffsets.Length, Is.EqualTo((tiff.Height + rowsPerStrip - 1) / rowsPerStrip));
            Assert.That(stripByteCounts.Length, Is.EqualTo(stripOffsets.Length));
            tiff.StripCount = stripOffsets.Length;

            using var pixels = new MemoryStream();
            int rowSize = tiff.Width * tiff.SamplesPerPixel;
            for (int s = 0; s < stripOffsets.Length; s++)
            {
                byte[] strip = LzwDecode(data.AsSpan((int)stripOffsets[s], (int)stripByteCounts[s]));
                int rows = Math.Min(rowsPerStrip, tiff.Height - s * rowsPerStrip);
                Assert.That(strip.Length, Is.EqualTo(rows * rowSize), $"Strip {s} has the wrong size.");
                pixels.Write(strip);
            }

            tiff.Pixels = pixels.ToArray();
            return tiff;
        }

        private static uint[] Values(byte[] data, int entry)
        {
            int type = U16(data, entry + 2);
            int count = (int)U32(data, entry + 4);
            int size = type switch { 3 => 2, 4 => 4, 5 => 8, _ => throw new AssertionException($"Unexpected type {type}.") };
            int offset = size * count <= 4 ? entry + 8 : (int)U32(data, entry + 8);
            return type switch
            {
                3 => Enumerable.Range(0, count).Select(i => (uint)U16(data, offset + i * 2)).ToArray(),
                4 => Enumerable.Range(0, count).Select(i => U32(data, offset + i * 4)).ToArray(),
                _ => [U32(data, offset), U32(data, offset + 4)]
            };
        }

        private static int U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

        // TIFF LZW as described in the TIFF 6.0 specification, with the code width growing one code early.
        private static byte[] LzwDecode(ReadOnlySpan<byte> data)
        {
            var output = new List<byte>();
            var table = new List<byte[]>();
            int bits = 9;
            int bitPosition = 0;
            byte[]? previous = null;
            bool sawClear = false;
            while (true)
            {
                Assert.That(bitPosition + bits, Is.LessThanOrEqualTo(data.Length * 8), "The strip ended without an end code.");
                int code = 0;
                for (int i = 0; i < bits; i++, bitPosition++)
                {
                    code = (code << 1) | ((data[bitPosition >> 3] >> (7 - (bitPosition & 7))) & 1);
                }

                if (code == 256)
                {
                    sawClear = true;
                    table.Clear();
                    for (int i = 0; i < 256; i++) table.Add([(byte)i]);
                    table.Add([]);
                    table.Add([]);
                    bits = 9;
                    previous = null;
                    continue;
                }

                Assert.That(sawClear, Is.True, "A strip must start with a clear code.");
                if (code == 257) break;

                byte[] entry;
                if (previous == null)
                {
                    entry = table[code];
                }
                else
                {
                    Assert.That(code, Is.LessThanOrEqualTo(table.Count), "Code outside the table.");
                    entry = code < table.Count ? table[code] : [.. previous, previous[0]];
                    table.Add([.. previous, entry[0]]);
                }

                output.AddRange(entry);
                previous = entry;
                if (table.Count + 1 >= 1 << bits && bits < 12) bits++;
            }

            return output.ToArray();
        }
    }
}
