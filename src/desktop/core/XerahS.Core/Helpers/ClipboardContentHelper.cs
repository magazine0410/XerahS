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

using SkiaSharp;
using XerahS.Common;
using XerahS.Platform.Abstractions;

namespace XerahS.Core;

/// <summary>
/// Represents parsed clipboard content with detected data type.
/// Shared between headless ClipboardUpload and interactive UploadContentWindow.
/// </summary>
public class ClipboardContent
{
    public EDataType DataType { get; set; }
    public SKBitmap? Image { get; set; }
    public string? Text { get; set; }
    public string[]? Files { get; set; }
}

/// <summary>
/// Shared clipboard parsing utility. Detects clipboard content type
/// using the same priority order as ShareX: image > text > files.
/// </summary>
public static class ClipboardContentHelper
{
    /// <summary>
    /// Parses the current clipboard contents and returns a structured
    /// <see cref="ClipboardContent"/> result, or null if clipboard is empty
    /// or contains unsupported data.
    /// </summary>
    public static ClipboardContent? ParseClipboard(IClipboardService clipboard)
    {
        if (clipboard.ContainsImage())
        {
            var image = clipboard.GetImage();
            if (image != null)
            {
                return new ClipboardContent
                {
                    DataType = EDataType.Image,
                    Image = image
                };
            }
        }

        if (clipboard.ContainsText())
        {
            var text = clipboard.GetText();
            if (!string.IsNullOrEmpty(text))
            {
                if (MayBeCopiedFilesText(text) && GetFilesContent(clipboard.ContainsFileDropList() ? clipboard.GetFileDropList() : null, text) is { } copiedFiles)
                {
                    return copiedFiles;
                }

                return new ClipboardContent
                {
                    DataType = EDataType.Text,
                    Text = text
                };
            }
        }

        return clipboard.ContainsFileDropList() ? GetFilesContent(clipboard.GetFileDropList(), null) : null;
    }

    /// <summary>
    /// Async version of <see cref="ParseClipboard"/> that avoids blocking the UI
    /// thread on platforms where clipboard I/O is inherently asynchronous (Linux/X11).
    /// Must be called on the UI thread.
    /// </summary>
    public static async Task<ClipboardContent?> ParseClipboardAsync(IClipboardService clipboard)
    {
        // Image has highest priority – try it first.
        var image = await clipboard.GetImageAsync();
        if (image != null)
        {
            return new ClipboardContent
            {
                DataType = EDataType.Image,
                Image = image
            };
        }

        var text = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            if (MayBeCopiedFilesText(text) && GetFilesContent(await clipboard.GetFileDropListAsync(), text) is { } copiedFiles)
            {
                return copiedFiles;
            }

            return new ClipboardContent
            {
                DataType = EDataType.Text,
                Text = text
            };
        }

        return GetFilesContent(await clipboard.GetFileDropListAsync(), null);
    }

    /// <summary>
    /// The copied files and folders that exist. As in ShareX, a copied folder is uploaded with the files in it. With
    /// <paramref name="text"/>, only when that text names exactly these files.
    /// </summary>
    private static ClipboardContent? GetFilesContent(string[]? files, string? text)
    {
        if (files == null || files.Length == 0) return null;
        if (text != null && !IsTextOfFiles(text, files)) return null;

        var validFiles = files.Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path))).ToArray();
        return validFiles.Length == 0 ? null : new ClipboardContent { DataType = EDataType.File, Files = validFiles };
    }

    /// <summary>
    /// Linux file managers also put copied files on the clipboard as text, the files' URLs or paths, one per line. On
    /// Windows copied files have no text, so ShareX uploads the files; here they are uploaded when the text is only them.
    /// </summary>
    private static bool MayBeCopiedFilesText(string text)
    {
        var lines = GetLines(text);
        return lines.Length > 0 && lines.All(line => line.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(line));
    }

    private static bool IsTextOfFiles(string text, string[] files)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var named = new HashSet<string>(GetLines(text).Select(line => NormalizePath(ToLocalPath(line))), comparer);
        return named.SetEquals(files.Where(path => !string.IsNullOrWhiteSpace(path)).Select(NormalizePath));
    }

    private static string[] GetLines(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();

    private static string ToLocalPath(string line) =>
        line.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : line;

    private static string NormalizePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
