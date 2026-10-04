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
using XerahS.Common;
using XerahS.Uploaders;

namespace ShareX.OneDrive.Plugin;

/// <summary>
/// ShareX's OneDrive uploader: an upload session in the folder, the file in 64 MiB segments, then an anonymous view
/// (or embed) link; without the shareable link, the result is the file's OneDrive page.
/// </summary>
public sealed class OneDriveUploader : FileUploader
{
    private const string GraphUrl = "https://graph.microsoft.com/v1.0";
    private const int MaxSegmentSize = 64 * 1024 * 1024;

    private readonly OAuth2Info _authInfo;
    private readonly Action<OAuth2Token>? _tokenRefreshed;
    private readonly OneDriveConfigModel _config;

    public OneDriveUploader(OAuth2Info authInfo, Action<OAuth2Token>? tokenRefreshed, OneDriveConfigModel config)
    {
        _authInfo = authInfo;
        _tokenRefreshed = tokenRefreshed;
        _config = config;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (!OneDriveProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return new UploadResult();

        string? sessionUrl = CreateSession(fileName);
        if (string.IsNullOrEmpty(sessionUrl))
        {
            Errors.Add("OneDrive did not create an upload session.");
            return new UploadResult();
        }

        UploadResult result;
        long position = 0;
        do
        {
            result = SendRequestFileRange(sessionUrl, stream, fileName, position, MaxSegmentSize);
            if (!result.IsSuccess)
            {
                SendRequest(XerahS.Uploaders.HttpMethod.DELETE, sessionUrl);
                return result;
            }

            position += MaxSegmentSize;
        }
        while (position < stream.Length);

        JObject uploaded = JObject.Parse(result.Response ?? "{}");
        if (_config.AutoCreateShareableLink)
        {
            AllowReportProgress = false;
            result.URL = CreateShareableLink(uploaded.Value<string>("id") ?? string.Empty, _config.UseDirectLink ? "embed" : "view");
        }
        else
        {
            result.URL = uploaded.Value<string>("webUrl");
        }

        return result;
    }

    internal static string GetFolderPath(string? folderID) =>
        string.IsNullOrEmpty(folderID) ? "me/drive/root" : URLHelpers.CombineURL("me/drive/items", folderID);

    private string? CreateSession(string fileName)
    {
        string json = JsonConvert.SerializeObject(new { item = new Dictionary<string, string> { ["@microsoft.graph.conflictBehavior"] = "replace" } });
        string url = URLHelpers.BuildUri("https://graph.microsoft.com", $"/v1.0/{GetFolderPath(_config.FolderID)}:/{fileName}:/createUploadSession");
        AllowReportProgress = false;
        string? response = SendRequest(XerahS.Uploaders.HttpMethod.POST, url, json, "application/json", headers: OAuth2Client.GetAuthHeaders(_authInfo));
        AllowReportProgress = true;
        return string.IsNullOrEmpty(response) ? null : JObject.Parse(response).Value<string>("uploadUrl");
    }

    /// <summary>ShareX's CreateShareableLink with an anonymous scope.</summary>
    private string? CreateShareableLink(string id, string linkType)
    {
        string json = JsonConvert.SerializeObject(new { type = linkType, scope = "anonymous" });
        string? response = SendRequest(XerahS.Uploaders.HttpMethod.POST, $"{GraphUrl}/me/drive/items/{id}/createLink", json, "application/json",
            headers: OAuth2Client.GetAuthHeaders(_authInfo));
        return string.IsNullOrEmpty(response) ? null : JObject.Parse(response)["link"]?.Value<string>("webUrl");
    }

    /// <summary>ShareX's GetPathInfo: the folders in a folder. Returns null on failure.</summary>
    public List<OneDriveFolder>? GetFolders(string folderID)
    {
        if (!OneDriveProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return null;

        var args = new Dictionary<string, string> { ["select"] = "id,name,folder" };
        string? response = SendRequest(XerahS.Uploaders.HttpMethod.GET, $"{GraphUrl}/{GetFolderPath(folderID)}/children", args,
            OAuth2Client.GetAuthHeaders(_authInfo));
        if (string.IsNullOrEmpty(response)) return null;

        return (JObject.Parse(response)["value"] as JArray ?? [])
            .OfType<JObject>()
            .Where(item => item["folder"] != null)
            .Select(item => new OneDriveFolder { ID = item.Value<string>("id") ?? string.Empty, Name = item.Value<string>("name") ?? string.Empty })
            .ToList();
    }
}
