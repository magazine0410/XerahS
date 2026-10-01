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
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XerahS.Media.Metadata;

public sealed record MetadataValue(string Group, string Tag, string Value);

public static class MetadataService
{
    public static async Task<IReadOnlyList<MetadataValue>> ReadMetadataAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(filePath);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return MetadataReader.Read(filePath, cancellationToken);
        }, cancellationToken);
    }

    public static bool CanStripMetadata(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            MetadataFormat format = MetadataFormatDetector.Detect(filePath);
            return format == MetadataFormat.IsoBaseMedia
                ? MetadataStripper.IsIsoVideoPath(filePath)
                : format.CanStripMetadata();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task StripMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ValidateFile(filePath);

        FileInfo original = new(Path.GetFullPath(filePath));
        // The selected name decides the format (an .mp4 link to a .blob file is stripped as MP4).
        string extension = original.Extension;
        // Strip the file a symbolic link points to, as ShareX does by writing through the link.
        if (original.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target) original = target;
        string fullPath = original.FullName;
        if ((original.Attributes & FileAttributes.ReadOnly) != 0)
            throw new UnauthorizedAccessException("The selected file is read-only.");
        long originalLength = original.Length;
        DateTime modified = original.LastWriteTimeUtc;
        string directory = original.DirectoryName!;
        string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}{extension}");
        try
        {
            // Some video formats strip in place. Work on a copy so a failure or cancellation
            // cannot leave the user's file partially modified.
            await using (FileStream input = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (FileStream output = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, cancellationToken);

            await Task.Run(() => MetadataStripper.Strip(temporaryPath, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            original.Refresh();
            if (original.Length != originalLength || original.LastWriteTimeUtc != modified)
                throw new IOException("The file changed while its metadata was being stripped. Please try again.");

            // Nothing below is cancellable, so the commit cannot stop halfway on request.
            if (GetHardLinkCount(fullPath) == 1)
                ReplaceAtomically(temporaryPath, fullPath, original);
            else
                await WriteIntoWithRollbackAsync(temporaryPath, fullPath, directory);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Test hook: wraps the stream that receives a write into the original file.</summary>
    internal static Func<Stream, Stream>? WrapWriteIntoStream { get; set; }

    // A rename either replaces the whole file or leaves the original as it was. The modified time
    // becomes now, as in ShareX; permissions (and on Windows, attributes and the creation time) are kept.
    private static void ReplaceAtomically(string strippedPath, string fullPath, FileInfo original)
    {
        using (FileStream stripped = new(strippedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            stripped.Flush(flushToDisk: true);
        if (OperatingSystem.IsWindows())
        {
            File.SetCreationTimeUtc(strippedPath, original.CreationTimeUtc);
            File.SetAttributes(strippedPath, original.Attributes);
        }
        else
        {
            // On Linux, .NET sets the modified time when asked to set the creation time, so it is left alone.
            File.SetUnixFileMode(strippedPath, File.GetUnixFileMode(fullPath));
        }
        File.Move(strippedPath, fullPath, overwrite: true);
    }

    // A file with other hard links (or an unknown link count) is written into, so every link sees the
    // result. A backup of the original is restored if the write fails, and kept if the restore fails too.
    private static async Task WriteIntoWithRollbackAsync(string strippedPath, string fullPath, string directory)
    {
        string backupPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.backup");
        bool keepBackup = false;
        File.Copy(fullPath, backupPath);
        try
        {
            try
            {
                await WriteIntoAsync(strippedPath, fullPath);
            }
            catch (Exception writeError)
            {
                try
                {
                    await WriteIntoAsync(backupPath, fullPath);
                }
                catch (Exception restoreError)
                {
                    keepBackup = true;
                    throw new IOException("Writing the stripped file failed, and restoring the original also failed. " +
                        $"The original file is saved as {backupPath}.", new AggregateException(writeError, restoreError));
                }
                throw new IOException("Writing the stripped file failed. The original file was restored.", writeError);
            }
        }
        finally
        {
            if (!keepBackup && File.Exists(backupPath)) File.Delete(backupPath);
        }
    }

    private static async Task WriteIntoAsync(string sourcePath, string fullPath)
    {
        await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using FileStream output = new(fullPath, FileMode.Open, FileAccess.Write, FileShare.None);
        Stream destination = WrapWriteIntoStream?.Invoke(output) ?? output;
        await source.CopyToAsync(destination, CancellationToken.None);
        await destination.FlushAsync(CancellationToken.None);
        output.SetLength(source.Length);
        output.Flush(flushToDisk: true);
    }

    /// <summary>The number of hard links to the file, or null when the platform does not report it.</summary>
    internal static int? GetHardLinkCount(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return GetFileInformationByHandle(handle, out ByHandleFileInformation info) ? (int)info.NumberOfLinks : null;
            }
            if (OperatingSystem.IsLinux())
            {
                // struct statx has the same layout on every architecture; stx_nlink is at offset 16.
                byte[] buffer = new byte[256];
                const int AtCurrentDirectory = -100;
                const uint StatxNlink = 0x4;
                if (statx(AtCurrentDirectory, path, 0, StatxNlink, buffer) != 0) return null;
                if ((BinaryPrimitives.ReadUInt32LittleEndian(buffer) & StatxNlink) == 0) return null;
                return (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException) { }
        return null;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int directory, string path, int flags, uint mask, byte[] buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    // FILETIME is two 32-bit values, so the fields are 4-byte aligned.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    public static void StripFileMetadata(string filePath)
    {
        StripMetadataAsync(filePath).GetAwaiter().GetResult();
    }

    private static void ValidateFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file could not be found.", filePath);
        }
    }
}
