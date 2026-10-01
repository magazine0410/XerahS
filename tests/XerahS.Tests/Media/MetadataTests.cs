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
