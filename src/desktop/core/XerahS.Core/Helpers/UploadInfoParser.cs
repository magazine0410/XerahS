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

using System.Text.RegularExpressions;
using XerahS.Common;
using XerahS.Uploaders;

namespace XerahS.Core;

/// <summary>ShareX upload-result placeholders used by automatic tasks and the after-upload window.</summary>
public static class UploadInfoParser
{
    // Longer names come first. Replace in one pass so a URL containing "$filename", for
    // example, is not interpreted as another placeholder after it has been substituted.
    private static readonly Regex Tokens = new(
        @"\$(thumbnailfilenamenoext|thumbnailfilename|thumbnailurl|deletionurl|filenamenoext|folderpath|foldername|filepath|filename|uploadtime|shortened|shorturl|thumbnail|deletion|result|url)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Parse(TaskInfo info, string? pattern) => Parse(pattern, info.Result,
        info.FileName, info.FilePath, info.ThumbnailFilePath, info.UploadDuration?.ElapsedMilliseconds);

    public static string Parse(string? pattern, UploadResult result, string? fileName, string? filePath,
        string? thumbnailFilePath = null, long? uploadTime = null)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;
        string primary = result.ToString();
        if (string.IsNullOrEmpty(primary)) primary = filePath ?? string.Empty;

        return Tokens.Replace(NameParser.Parse(NameParserType.Default, pattern), match =>
            match.Groups[1].Value.ToLowerInvariant() switch
            {
                "result" => primary,
                "url" => result.URL ?? string.Empty,
                "shorturl" or "shortened" => result.ShortenedURL ?? string.Empty,
                "thumbnailurl" or "thumbnail" => result.ThumbnailURL ?? string.Empty,
                "deletionurl" or "deletion" => result.DeletionURL ?? string.Empty,
                "filenamenoext" => Path.GetFileNameWithoutExtension(fileName) ?? string.Empty,
                "filename" => fileName ?? string.Empty,
                "filepath" => filePath ?? string.Empty,
                "folderpath" => Path.GetDirectoryName(filePath) ?? string.Empty,
                "foldername" => Path.GetFileName(Path.GetDirectoryName(filePath)) ?? string.Empty,
                "thumbnailfilenamenoext" => Path.GetFileNameWithoutExtension(thumbnailFilePath) ?? string.Empty,
                "thumbnailfilename" => Path.GetFileName(thumbnailFilePath) ?? string.Empty,
                "uploadtime" => uploadTime?.ToString() ?? match.Value,
                _ => match.Value
            });
    }
}
