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
using System.Text;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Media.Metadata;

namespace XerahS.Tests.Media;

[TestFixture]
public class MetadataTests
{
    private string _directory = null!;
    [SetUp] public void SetUp() => Directory.CreateDirectory(_directory = Path.Combine(Path.GetTempPath(), "xerahs-metadata-" + Guid.NewGuid().ToString("N")));
    [TearDown] public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public async Task StripPng_RemovesText_PreservesEncodedPixels_AndIsIdempotent()
    {
        using var bitmap = new SKBitmap(12, 10);
        bitmap.Erase(SKColors.Coral);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        byte[] original = encoded.ToArray();
        byte[] text = PngChunk("tEXt", "Author\0Private person"u8.ToArray());
        string path = Path.Combine(_directory, "picture.png");
        await File.WriteAllBytesAsync(path, [.. original[..33], .. text, .. original[33..]]);
        Assert.That(await MetadataService.ReadMetadataAsync(path), Has.Some.Matches<MetadataValue>(v => v.Value == "Private person"));
        await MetadataService.StripMetadataAsync(path);
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original), "IDAT data must remain byte-for-byte identical.");
        await MetadataService.StripMetadataAsync(path);
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
    }

    [Test]
    public async Task StripJpeg_KeepsOnlyTheOrientation_AndIsIdempotent()
    {
        using var bitmap = new SKBitmap(16, 8);
        bitmap.Erase(SKColors.Coral);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        byte[] original = encoded.ToArray();
        byte[] exif = ExifWithMakeAndOrientation("PrivateCamera", 6);
        byte[] app1 = [0xFF, 0xE1, (byte)((exif.Length + 2) >> 8), (byte)(exif.Length + 2), .. exif];
        string path = Path.Combine(_directory, "portrait.jpg");
        await File.WriteAllBytesAsync(path, [.. original[..2], .. app1, .. original[2..]]);

        await MetadataService.StripMetadataAsync(path);
        byte[] stripped = await File.ReadAllBytesAsync(path);
        byte[] kept = MetadataStripper.CreateOrientationExif(6);
        byte[] expectedApp1 = [0xFF, 0xE1, 0x00, (byte)(kept.Length + 2), .. kept];
        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(stripped), Does.Not.Contain("PrivateCamera"));
            Assert.That(stripped.AsSpan(2, expectedApp1.Length).ToArray(), Is.EqualTo(expectedApp1),
                "The photo's orientation is kept, so a portrait photo is not shown on its side.");
            Assert.That(MetadataStripper.ReadExifOrientation(kept), Is.EqualTo((ushort)6));
        });

        await MetadataService.StripMetadataAsync(path);
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(stripped));

        byte[] upright = ExifWithMakeAndOrientation("PrivateCamera", 1);
        string uprightPath = Path.Combine(_directory, "upright.jpg");
        await File.WriteAllBytesAsync(uprightPath,
            [.. original[..2], 0xFF, 0xE1, (byte)((upright.Length + 2) >> 8), (byte)(upright.Length + 2), .. upright, .. original[2..]]);
        await MetadataService.StripMetadataAsync(uprightPath);
        Assert.That(await File.ReadAllBytesAsync(uprightPath), Is.EqualTo(original), "Orientation 1 needs no EXIF segment.");
    }

    /// <summary>An EXIF APP1 payload (little-endian) with IFD0's Make and Orientation tags.</summary>
    private static byte[] ExifWithMakeAndOrientation(string make, ushort orientation)
    {
        byte[] makeBytes = [.. Encoding.ASCII.GetBytes(make), 0];
        byte[] tiff = new byte[8 + 2 + 24 + 4 + makeBytes.Length];
        "II*\0"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x010F);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), (uint)makeBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(18), 38);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(22), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(24), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(30), orientation);
        makeBytes.CopyTo(tiff, 38);
        return [.. "Exif\0\0"u8.ToArray(), .. tiff];
    }

    [Test]
    public async Task StripMp4_PadsMetadataWithoutMovingMediaData()
    {
        byte[] header = Box("ftyp", "isom\0\0\0\0isom"u8.ToArray());
        byte[] media = Box("mdat", [1, 2, 3, 4, 5, 6]);
        byte[] metadata = Box("moov", Box("udta", Box("name", "Private title"u8.ToArray())));
        byte[] original = [.. header, .. metadata, .. media];
        string path = Path.Combine(_directory, "video.mp4");
        await File.WriteAllBytesAsync(path, original);
        await MetadataService.StripMetadataAsync(path);
        byte[] stripped = await File.ReadAllBytesAsync(path);
        Assert.That(stripped, Has.Length.EqualTo(original.Length));
        Assert.That(stripped[^media.Length..], Is.EqualTo(media));
        Assert.That(Encoding.ASCII.GetString(stripped), Does.Not.Contain("Private title"));
    }

    [Test]
    public async Task DeeplyNestedAviLists_AreReadWithoutOverflowingTheStack_AndRefusedByStrip()
    {
        // 100,000 nested LIST hdrl chunks, 12 bytes each: deep enough to end the process if each level recursed.
        const int levels = 100_000;
        byte[] file = new byte[12 + 12 * levels];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 4 + 12 * levels);
        "AVI "u8.CopyTo(file.AsSpan(8));
        for (int level = 0; level < levels; level++)
        {
            Span<byte> chunk = file.AsSpan(12 + 12 * level, 12);
            "LIST"u8.CopyTo(chunk);
            BinaryPrimitives.WriteInt32LittleEndian(chunk[4..], 4 + 12 * (levels - 1 - level));
            "hdrl"u8.CopyTo(chunk[8..]);
        }
        string path = Path.Combine(_directory, "nested.avi");
        await File.WriteAllBytesAsync(path, file);

        Assert.That(await MetadataService.ReadMetadataAsync(path), Is.Not.Null);
        Assert.That(async () => await MetadataService.StripMetadataAsync(path), Throws.InstanceOf<InvalidDataException>());
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(file));
    }

    [Test]
    public async Task ATiffWhoseEntriesAllPointAtOneLargeValue_IsReadToALimit()
    {
        // 4,096 entries, each a 1,000,000-value LONG array at the same offset: 16 GB to copy without a limit.
        const int entries = 4096;
        const int values = 1_000_000;
        int valueOffset = 8 + 2 + entries * 12 + 4;
        byte[] file = new byte[valueOffset + values * 4];
        "II"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(8), entries);
        for (int index = 0; index < entries; index++)
        {
            Span<byte> entry = file.AsSpan(10 + index * 12, 12);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)(0xC000 + index));
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 4);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], values);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)valueOffset);
        }
        string path = Path.Combine(_directory, "crafted.tif");
        await File.WriteAllBytesAsync(path, file);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<MetadataValue> metadata = await MetadataService.ReadMetadataAsync(path);
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        Assert.That(metadata.Select(value => value.Value), Does.Contain(TiffMetadataReader.TooMuchMetadataNotice));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.That(async () => await MetadataService.ReadMetadataAsync(path, cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task DeeplyNestedMp4Tracks_AreRefusedByStrip()
    {
        const int levels = 100_000;
        byte[] header = Box("ftyp", "isom\0\0\0\0isom"u8.ToArray());
        byte[] movie = new byte[8 + 8 * levels];
        BinaryPrimitives.WriteInt32BigEndian(movie, movie.Length);
        "moov"u8.CopyTo(movie.AsSpan(4));
        for (int level = 0; level < levels; level++)
        {
            Span<byte> box = movie.AsSpan(8 + 8 * level, 8);
            BinaryPrimitives.WriteInt32BigEndian(box, 8 * (levels - level));
            "trak"u8.CopyTo(box[4..]);
        }
        byte[] file = [.. header, .. movie];
        string path = Path.Combine(_directory, "nested.mp4");
        await File.WriteAllBytesAsync(path, file);

        Assert.That(async () => await MetadataService.StripMetadataAsync(path), Throws.InstanceOf<InvalidDataException>());
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(file));
    }

    [Test]
    public async Task NestedMatroskaSimpleTags_AreReadToALimitedDepth()
    {
        byte[] simpleTag = [];
        for (int level = 39; level >= 0; level--)
        {
            simpleTag = Ebml([0x67, 0xC8], [.. Ebml([0x45, 0xA3], Encoding.UTF8.GetBytes($"Level{level}")),
                .. Ebml([0x44, 0x87], Encoding.UTF8.GetBytes($"value{level}")), .. simpleTag]);
        }
        byte[] tags = Ebml([0x12, 0x54, 0xC3, 0x67], Ebml([0x73, 0x73], simpleTag));
        byte[] file = [.. Ebml([0x1A, 0x45, 0xDF, 0xA3], []), .. Ebml([0x18, 0x53, 0x80, 0x67], tags)];
        string path = Path.Combine(_directory, "nested.mkv");
        await File.WriteAllBytesAsync(path, file);

        IReadOnlyList<MetadataValue> values = await MetadataService.ReadMetadataAsync(path);

        Assert.That(values.Select(value => value.Value), Has.Member("value0").And.Member("value16"));
        Assert.That(values.Select(value => value.Value), Has.No.Member("value17"));
    }

    /// <summary>An EBML element with an eight-byte size.</summary>
    private static byte[] Ebml(byte[] id, byte[] payload)
    {
        byte[] size = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)payload.Length);
        size[0] = 0x01;
        return [.. id, .. size, .. payload];
    }

    [Test]
    public async Task CancellationAndUnsupportedFormat_LeaveOriginalAndNoTemporaryFiles()
    {
        string path = Path.Combine(_directory, "data.bin");
        byte[] original = "unrecognized data"u8.ToArray();
        await File.WriteAllBytesAsync(path, original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await MetadataService.StripMetadataAsync(path, cancellation.Token));
        Assert.That(MetadataService.CanStripMetadata(path), Is.False);
        Assert.ThrowsAsync<NotSupportedException>(async () => await MetadataService.StripMetadataAsync(path));
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task Strip_WritesIntoTheLinkedFile_AndUpdatesTheModifiedTime()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Creating symbolic links needs extra rights on Windows.");
        using var bitmap = new SKBitmap(4, 4);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        byte[] original = encoded.ToArray();
        string path = Path.Combine(_directory, "picture.png");
        await File.WriteAllBytesAsync(path, [.. original[..33], .. PngChunk("tEXt", "Author\0Private person"u8.ToArray()), .. original[33..]]);
        DateTime old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, old);
        string link = Path.Combine(_directory, "link.png");
        File.CreateSymbolicLink(link, path);

        await MetadataService.StripMetadataAsync(link);

        Assert.That(new FileInfo(link).LinkTarget, Is.EqualTo(path), "The link must stay a link.");
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        Assert.That(File.GetLastWriteTimeUtc(path), Is.GreaterThan(old));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(2), "No temporary files remain.");
    }

    [Test]
    public async Task Strip_UsesTheSelectedNamesExtension_ForASymbolicLink()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Creating symbolic links needs extra rights on Windows.");
        string stored = Path.Combine(_directory, "stored.blob");
        await File.WriteAllBytesAsync(stored, Mp4WithTitle());
        string link = Path.Combine(_directory, "movie.mp4");
        File.CreateSymbolicLink(link, stored);
        Assert.That(MetadataService.CanStripMetadata(link), Is.True);

        await MetadataService.StripMetadataAsync(link);

        Assert.That(Encoding.ASCII.GetString(await File.ReadAllBytesAsync(stored)), Does.Not.Contain("Private title"));
        Assert.That(new FileInfo(link).LinkTarget, Is.EqualTo(stored));
    }

    [Test]
    public async Task HardLinkedFile_FailedWrite_RestoresTheOriginal_AndKeepsTheLink()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("The test creates hard links with libc.");
        string path = Path.Combine(_directory, "video.mp4");
        byte[] original = Mp4WithTitle(64 * 1024);
        await File.WriteAllBytesAsync(path, original);
        string other = Path.Combine(_directory, "other.mp4");
        Assert.That(link(path, other), Is.Zero);
        Assert.That(MetadataService.GetHardLinkCount(path), Is.EqualTo(2));

        // Fail the first write after 10 KB; the restore from the backup is allowed.
        int writes = 0;
        MetadataService.WrapWriteIntoStream = stream => ++writes == 1 ? new FailingStream(stream, 10 * 1024) : stream;
        try
        {
            var error = Assert.ThrowsAsync<IOException>(async () => await MetadataService.StripMetadataAsync(path));
            Assert.That(error!.Message, Does.Contain("restored"));
        }
        finally { MetadataService.WrapWriteIntoStream = null; }

        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        Assert.That(await File.ReadAllBytesAsync(other), Is.EqualTo(original), "Both names must still be the same file.");
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(2), "The backup and the stripped copy are removed.");

        await MetadataService.StripMetadataAsync(path);
        Assert.That(Encoding.ASCII.GetString(await File.ReadAllBytesAsync(other)), Does.Not.Contain("Private title"));
        Assert.That(MetadataService.GetHardLinkCount(path), Is.EqualTo(2));
    }

    [Test]
    public async Task HardLinkedFile_FailedWriteAndRestore_KeepsTheBackup()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("The test creates hard links with libc.");
        string path = Path.Combine(_directory, "video.mp4");
        byte[] original = Mp4WithTitle(64 * 1024);
        await File.WriteAllBytesAsync(path, original);
        Assert.That(link(path, Path.Combine(_directory, "other.mp4")), Is.Zero);

        MetadataService.WrapWriteIntoStream = stream => new FailingStream(stream, 10 * 1024);
        try
        {
            var error = Assert.ThrowsAsync<IOException>(async () => await MetadataService.StripMetadataAsync(path));
            string backup = Directory.GetFiles(_directory, "*.backup").Single();
            Assert.That(error!.Message, Does.Contain(backup));
            Assert.That(await File.ReadAllBytesAsync(backup), Is.EqualTo(original));
        }
        finally { MetadataService.WrapWriteIntoStream = null; }
    }

    [Test]
    public async Task SingleLinkFile_IsReplacedAtOnce_KeepingItsPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix permissions only.");
            return;
        }
        string path = Path.Combine(_directory, "video.mp4");
        await File.WriteAllBytesAsync(path, Mp4WithTitle());
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.That(MetadataService.GetHardLinkCount(path), Is.EqualTo(1));

        // The write-into path is not used, so a failing wrapper has no effect.
        MetadataService.WrapWriteIntoStream = stream => new FailingStream(stream, 0);
        try { await MetadataService.StripMetadataAsync(path); }
        finally { MetadataService.WrapWriteIntoStream = null; }

        Assert.That(Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path)), Does.Not.Contain("Private title"));
        Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    private static byte[] Mp4WithTitle(int mediaLength = 6)
    {
        byte[] media = new byte[mediaLength];
        for (int i = 0; i < media.Length; i++) media[i] = (byte)i;
        return [.. Box("ftyp", "isom\0\0\0\0isom"u8.ToArray()), .. Box("moov", Box("udta", Box("name", "Private title"u8.ToArray()))), .. Box("mdat", media)];
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int link(string existing, string created);

    /// <summary>Passes writes through until the limit, writing the part that fits, then fails.</summary>
    private sealed class FailingStream(Stream inner, long limit) : Stream
    {
        private long _written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            int allowed = (int)Math.Clamp(limit - _written, 0, count);
            inner.Write(buffer, offset, allowed);
            _written += allowed;
            if (allowed < count) throw new IOException("Simulated write failure.");
        }
    }

    private static byte[] Box(string type, byte[] data)
    {
        byte[] box = new byte[data.Length + 8];
        BinaryPrimitives.WriteInt32BigEndian(box, box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        data.CopyTo(box, 8);
        return box;
    }

    private static byte[] RiffChunk(string type, byte[] data)
    {
        byte[] chunk = new byte[data.Length + 8 + (data.Length & 1)];
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 0);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        byte[] chunk = new byte[data.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        uint crc = uint.MaxValue;
        foreach (byte b in chunk.AsSpan(4, data.Length + 4))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        return chunk;
    }
}
