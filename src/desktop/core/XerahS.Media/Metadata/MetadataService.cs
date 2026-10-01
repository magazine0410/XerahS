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
        // Strip the file a symbolic link points to, as ShareX does by writing through the link.
        if (original.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target) original = target;
        string fullPath = original.FullName;
        if ((original.Attributes & FileAttributes.ReadOnly) != 0)
            throw new UnauthorizedAccessException("The selected file is read-only.");
        long originalLength = original.Length;
        DateTime modified = original.LastWriteTimeUtc;
        string temporaryPath = Path.Combine(original.DirectoryName!, $".{Guid.NewGuid():N}{original.Extension}");
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

            // Write the result into the original file instead of replacing it, so hard links, permissions,
            // ownership, and the creation time are kept, and the modified time becomes now, as in ShareX.
            // This final copy is not cancellable, so it cannot stop halfway.
            await using (FileStream stripped = new(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (FileStream output = new(fullPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                await stripped.CopyToAsync(output, CancellationToken.None);
                output.SetLength(stripped.Length);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
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
