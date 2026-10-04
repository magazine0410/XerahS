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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;

namespace ShareX.YouTube.Plugin;

/// <summary>
/// ShareX's YouTube uploader: the video is uploaded with its file name as the title and the chosen visibility; the
/// result is its watch link or youtu.be link.
/// </summary>
public sealed class YouTubeUploader : FileUploader
{
    private const string UploadUrl = "https://www.googleapis.com/upload/youtube/v3/videos?part=id,snippet,status";

    internal static readonly string[] VideoFileTypes = ["mp4", "mov", "avi", "mkv", "webm", "wmv", "flv", "mpg", "mpeg", "3gp", "m4v"];

    private readonly OAuth2Info _authInfo;
    private readonly Action<OAuth2Token>? _tokenRefreshed;
    private readonly YouTubeConfigModel _config;

    public YouTubeUploader(OAuth2Info authInfo, Action<OAuth2Token>? tokenRefreshed, YouTubeConfigModel config)
    {
        _authInfo = authInfo;
        _tokenRefreshed = tokenRefreshed;
        _config = config;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (!YouTubeProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return new UploadResult();

        UploadResult result = SendRequestFile(UploadUrl, stream, fileName, "file", headers: OAuth2Client.GetAuthHeaders(_authInfo),
            relatedData: CreateMetadata(Path.GetFileNameWithoutExtension(fileName), _config.PrivacyType));
        ApplyResponse(result, _config.UseShortenedLink, Errors);
        return result;
    }

    /// <summary>
    /// ShareX's metadata. ShareX serializes the visibility as a number; the YouTube API documents the strings
    /// "public", "unlisted", and "private", which are sent here.
    /// </summary>
    internal static string CreateMetadata(string title, YouTubeVideoPrivacy privacy) => new JObject
    {
        ["snippet"] = new JObject { ["title"] = title, ["description"] = string.Empty },
        ["status"] = new JObject { ["privacyStatus"] = privacy.ToString().ToLowerInvariant() }
    }.ToString(Formatting.None);

    internal static void ApplyResponse(UploadResult result, bool useShortenedLink, UploaderErrorManager errors)
    {
        if (string.IsNullOrEmpty(result.Response)) return;

        JObject video;
        try { video = JObject.Parse(result.Response); }
        catch (JsonException) { return; }

        string? id = video.Value<string>("id");
        if (string.IsNullOrEmpty(id)) return;

        result.URL = useShortenedLink ? $"https://youtu.be/{id}" : $"https://www.youtube.com/watch?v={id}";
        JToken? status = video["status"];
        switch (status?.Value<string>("uploadStatus"))
        {
            case "failed":
                errors.Add("YouTube upload failed: " + status.Value<string>("failureReason"));
                break;
            case "rejected":
                errors.Add("YouTube upload rejected: " + status.Value<string>("rejectionReason"));
                break;
        }
    }
}
