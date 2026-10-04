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
using SkiaSharp;

namespace XerahS.Common;

/// <summary>
/// Writes baseline TIFF images, because SkiaSharp has no TIFF encoder. As with the Windows TIFF encoder that
/// ShareX uses, the pixels are LZW-compressed 8-bit RGB, with an unassociated alpha channel when the image has
/// transparent pixels, at 96 DPI.
/// </summary>
public static class TiffEncoder
{
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;

    // Strips of about 64 KB of uncompressed pixels.
    private const int StripSize = 64 * 1024;

    public static byte[] Encode(SKBitmap bitmap)
    {
        using var stream = new MemoryStream();
        Encode(bitmap, stream);
        return stream.ToArray();
    }

    public static void Encode(SKBitmap bitmap, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(stream);

        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 0 || height <= 0) throw new ArgumentException("The image has no pixels.", nameof(bitmap));

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var rgba = new SKBitmap(info);
        using (var image = SKImage.FromBitmap(bitmap))
        {
            if (image == null || !image.ReadPixels(rgba.PeekPixels(), 0, 0))
                throw new InvalidOperationException("The image pixels could not be read.");
        }

        ReadOnlySpan<byte> pixels = rgba.GetPixelSpan();
        bool hasAlpha = HasTransparentPixels(pixels);
        int samplesPerPixel = hasAlpha ? 4 : 3;
        long rowSize = (long)width * samplesPerPixel;
        int rowsPerStrip = (int)Math.Clamp(StripSize / rowSize, 1, height);
        int stripCount = (height + rowsPerStrip - 1) / rowsPerStrip;

        var strips = new List<byte[]>(stripCount);
        var lzw = new LzwEncoder();
        byte[] stripPixels = new byte[rowSize * rowsPerStrip];
        for (int strip = 0; strip < stripCount; strip++)
        {
            int firstRow = strip * rowsPerStrip;
            int rows = Math.Min(rowsPerStrip, height - firstRow);
            int length = 0;
            for (int y = firstRow; y < firstRow + rows; y++)
            {
                ReadOnlySpan<byte> row = pixels.Slice(y * info.RowBytes, width * 4);
                if (hasAlpha)
                {
                    row.CopyTo(stripPixels.AsSpan(length));
                    length += row.Length;
                }
                else
                {
                    for (int x = 0; x < row.Length; x += 4)
                    {
                        stripPixels[length++] = row[x];
                        stripPixels[length++] = row[x + 1];
                        stripPixels[length++] = row[x + 2];
                    }
                }
            }

            strips.Add(lzw.Encode(stripPixels.AsSpan(0, length)));
        }

        WriteFile(stream, width, height, samplesPerPixel, rowsPerStrip, strips);
    }

    private static bool HasTransparentPixels(ReadOnlySpan<byte> rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 255) return true;
        }

        return false;
    }

    private static void WriteFile(Stream stream, int width, int height, int samplesPerPixel, int rowsPerStrip, List<byte[]> strips)
    {
        // Layout: header, strip data, the values that do not fit in their directory entries, then the directory.
        long offset = 8;
        var stripOffsets = new uint[strips.Count];
        for (int i = 0; i < strips.Count; i++)
        {
            stripOffsets[i] = CheckedOffset(offset);
            offset = Align(offset + strips[i].Length);
        }

        long bitsPerSampleOffset = offset;
        offset = Align(offset + 2L * samplesPerPixel);
        long resolutionOffset = offset;
        offset += 16;
        long stripOffsetsOffset = offset;
        offset += 4L * strips.Count;
        long stripByteCountsOffset = offset;
        offset += 4L * strips.Count;
        long directoryOffset = Align(offset);

        var entries = new List<(ushort Tag, ushort Type, uint Count, uint Value)>
        {
            (256, TypeLong, 1, (uint)width),
            (257, TypeLong, 1, (uint)height),
            (258, TypeShort, (uint)samplesPerPixel, CheckedOffset(bitsPerSampleOffset)),
            (259, TypeShort, 1, 5), // LZW
            (262, TypeShort, 1, 2), // RGB
            (273, TypeLong, (uint)strips.Count, strips.Count == 1 ? stripOffsets[0] : CheckedOffset(stripOffsetsOffset)),
            (277, TypeShort, 1, (uint)samplesPerPixel),
            (278, TypeLong, 1, (uint)rowsPerStrip),
            (279, TypeLong, (uint)strips.Count, strips.Count == 1 ? (uint)strips[0].Length : CheckedOffset(stripByteCountsOffset)),
            (282, TypeRational, 1, CheckedOffset(resolutionOffset)),
            (283, TypeRational, 1, CheckedOffset(resolutionOffset + 8)),
            (284, TypeShort, 1, 1), // chunky
            (296, TypeShort, 1, 2) // inch
        };
        if (samplesPerPixel == 4)
        {
            entries.Add((338, TypeShort, 1, 2)); // unassociated alpha
        }

        CheckedOffset(directoryOffset + 2 + 12L * entries.Count + 4);

        var writer = new BinaryWriter(stream);
        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write((uint)directoryOffset);

        foreach (byte[] strip in strips)
        {
            writer.Write(strip);
            if (strip.Length % 2 != 0) writer.Write((byte)0);
        }

        for (int i = 0; i < samplesPerPixel; i++) writer.Write((ushort)8);
        for (int i = 0; i < 2; i++)
        {
            writer.Write(96u);
            writer.Write(1u);
        }

        foreach (uint stripOffset in stripOffsets) writer.Write(stripOffset);
        foreach (byte[] strip in strips) writer.Write((uint)strip.Length);

        writer.Write((ushort)entries.Count);
        Span<byte> value = stackalloc byte[4];
        foreach (var entry in entries)
        {
            writer.Write(entry.Tag);
            writer.Write(entry.Type);
            writer.Write(entry.Count);
            // A single SHORT is stored in the first two bytes of the value field.
            value.Clear();
            if (entry.Type == TypeShort && entry.Count == 1)
                BinaryPrimitives.WriteUInt16LittleEndian(value, (ushort)entry.Value);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(value, entry.Value);
            writer.Write(value);
        }

        writer.Write(0u);
        writer.Flush();
    }

    private static long Align(long offset) => (offset + 1) & ~1L;

    private static uint CheckedOffset(long offset) =>
        offset <= uint.MaxValue ? (uint)offset : throw new NotSupportedException("The image is too large for a TIFF file (over 4 GB).");

    /// <summary>
    /// TIFF's LZW compression: codes of 9 to 12 bits written most significant bit first, starting with a clear code
    /// and ending with an end-of-information code. The code width changes at the same points as libtiff's encoder.
    /// </summary>
    private sealed class LzwEncoder
    {
        private const int ClearCode = 256;
        private const int EndOfInformation = 257;
        private const int FirstCode = 258;
        private const int MinBits = 9;
        private const int LastCode = 4094;

        // The code for each prefix code followed by a byte, or 0 when there is none.
        private readonly int[] _table = new int[LastCode * 256];
        private readonly List<int> _usedEntries = new(LastCode);
        private MemoryStream _output = new();
        private ulong _bitBuffer;
        private int _bitCount;

        public byte[] Encode(ReadOnlySpan<byte> data)
        {
            _output = new MemoryStream(data.Length / 2 + 16);
            _bitBuffer = 0;
            _bitCount = 0;
            ResetTable();

            int bits = MinBits;
            int maxCode = (1 << bits) - 1;
            int nextCode = FirstCode;
            WriteCode(ClearCode, bits);

            if (!data.IsEmpty)
            {
                int prefix = data[0];
                for (int i = 1; i < data.Length; i++)
                {
                    byte c = data[i];
                    int key = (prefix << 8) | c;
                    int code = _table[key];
                    if (code != 0)
                    {
                        prefix = code;
                        continue;
                    }

                    WriteCode(prefix, bits);
                    _table[key] = nextCode++;
                    _usedEntries.Add(key);
                    prefix = c;

                    if (nextCode == LastCode)
                    {
                        WriteCode(ClearCode, bits);
                        ResetTable();
                        nextCode = FirstCode;
                        bits = MinBits;
                        maxCode = (1 << bits) - 1;
                    }
                    else if (nextCode > maxCode)
                    {
                        bits++;
                        maxCode = (1 << bits) - 1;
                    }
                }

                WriteCode(prefix, bits);
                // The decoder adds a table entry for this code too, so the end code may need a wider code.
                nextCode++;
                if (nextCode == LastCode)
                {
                    WriteCode(ClearCode, bits);
                    bits = MinBits;
                }
                else if (nextCode > maxCode)
                {
                    bits++;
                }
            }

            WriteCode(EndOfInformation, bits);
            if (_bitCount > 0)
            {
                _output.WriteByte((byte)(_bitBuffer << (8 - _bitCount)));
            }

            return _output.ToArray();
        }

        private void WriteCode(int code, int bits)
        {
            _bitBuffer = (_bitBuffer << bits) | (uint)code;
            _bitCount += bits;
            while (_bitCount >= 8)
            {
                _bitCount -= 8;
                _output.WriteByte((byte)(_bitBuffer >> _bitCount));
            }

            _bitBuffer &= (1UL << _bitCount) - 1;
        }

        private void ResetTable()
        {
            foreach (int key in _usedEntries) _table[key] = 0;
            _usedEntries.Clear();
        }
    }
}
