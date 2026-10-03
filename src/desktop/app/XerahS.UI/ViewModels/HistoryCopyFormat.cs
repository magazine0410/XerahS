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
using System.Net;
using XerahS.Common;
using XerahS.History;

namespace XerahS.UI.ViewModels;

public enum HistoryCopyFormat
{
    URL, ShortenedURL, ThumbnailURL, DeletionURL,
    File, Image, TextContents, FilePath, FileName, FileNameWithExtension, Folder, ImageDimensions,
    HtmlLink, HtmlImage, HtmlLinkedImage,
    ForumLink, ForumImage, ForumLinkedImage,
    MarkdownLink, MarkdownImage, MarkdownLinkedImage
}

internal static class HistoryCopyText
{
    public static string? GetValue(HistoryItem item, HistoryCopyFormat format)
    {
        string url = item.URL;
        string thumbnail = item.ThumbnailURL;
        bool hasUrl = !string.IsNullOrWhiteSpace(url);
        bool hasImageUrl = hasUrl && (FileHelpers.IsImageFile(url) ||
            item.Type.Equals("Image", StringComparison.OrdinalIgnoreCase));
        bool hasLinkedImage = hasImageUrl && !string.IsNullOrWhiteSpace(thumbnail);
        string name = EscapeMarkdown(item.FileName);
        return format switch
        {
            HistoryCopyFormat.URL => url,
            HistoryCopyFormat.ShortenedURL => item.ShortenedURL,
            HistoryCopyFormat.ThumbnailURL => thumbnail,
            HistoryCopyFormat.DeletionURL => item.DeletionURL,
            HistoryCopyFormat.FilePath => item.FilePath,
            HistoryCopyFormat.FileName when !string.IsNullOrWhiteSpace(item.FilePath) => Path.GetFileNameWithoutExtension(item.FilePath),
            HistoryCopyFormat.FileNameWithExtension when !string.IsNullOrWhiteSpace(item.FilePath) => Path.GetFileName(item.FilePath),
            HistoryCopyFormat.Folder when !string.IsNullOrWhiteSpace(item.FilePath) => Path.GetDirectoryName(item.FilePath),
            HistoryCopyFormat.HtmlLink when hasUrl => $"<a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(url)}</a>",
            HistoryCopyFormat.HtmlImage when hasImageUrl => $"<img src=\"{WebUtility.HtmlEncode(url)}\"/>",
            HistoryCopyFormat.HtmlLinkedImage when hasLinkedImage => $"<a href=\"{WebUtility.HtmlEncode(url)}\"><img src=\"{WebUtility.HtmlEncode(thumbnail)}\"/></a>",
            HistoryCopyFormat.ForumLink when hasUrl => $"[url]{url}[/url]",
            HistoryCopyFormat.ForumImage when hasImageUrl => $"[img]{url}[/img]",
            HistoryCopyFormat.ForumLinkedImage when hasLinkedImage => $"[url={url}][img]{thumbnail}[/img][/url]",
            HistoryCopyFormat.MarkdownLink when hasUrl => $"[{name}]({MarkdownUrl(url)})",
            HistoryCopyFormat.MarkdownImage when hasImageUrl => $"![{name}]({MarkdownUrl(url)})",
            HistoryCopyFormat.MarkdownLinkedImage when hasLinkedImage => $"[![{name}]({MarkdownUrl(thumbnail)})]({MarkdownUrl(url)})",
            _ => null
        };
    }

    private static string EscapeMarkdown(string value) => value.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
    private static string MarkdownUrl(string value) => value.IndexOfAny([' ', '(', ')']) >= 0
        ? $"<{value.Replace("<", "%3C").Replace(">", "%3E")}>" : value;
}
