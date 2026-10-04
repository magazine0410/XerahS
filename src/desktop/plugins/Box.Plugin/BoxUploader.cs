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

using Newtonsoft.Json.Linq;
using XerahS.Uploaders;

namespace ShareX.Box.Plugin;

/// <summary>
/// ShareX's Box uploader: the file goes to the folder, then gets a shared link with the access level; without the
/// shared link, the result is the file's page on Box.
/// </summary>
public sealed class BoxUploader : FileUploader
{
    private readonly OAuth2Info _authInfo;
    private readonly Action<OAuth2Token>? _tokenRefreshed;
    private readonly BoxConfigModel _config;

    public BoxUploader(OAuth2Info authInfo, Action<OAuth2Token>? tokenRefreshed, BoxConfigModel config)
    {
        _authInfo = authInfo;
        _tokenRefreshed = tokenRefreshed;
        _config = config;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (!BoxProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return new UploadResult();

        string folderID = string.IsNullOrEmpty(_config.FolderID) ? "0" : _config.FolderID;
        UploadResult result = SendRequestFile("https://upload.box.com/api/2.0/files/content", stream, fileName, "filename",
            new Dictionary<string, string> { ["parent_id"] = folderID }, OAuth2Client.GetAuthHeaders(_authInfo));
        if (!result.IsSuccess || string.IsNullOrEmpty(result.Response)) return result;

        JObject? entry = (JObject.Parse(result.Response)["entries"] as JArray)?.FirstOrDefault() as JObject;
        if (entry == null) return result;

        string id = entry.Value<string>("id") ?? string.Empty;
        if (_config.Share)
        {
            AllowReportProgress = false;
            result.URL = CreateSharedLink(id, _config.ShareAccessLevel);
        }
        else
        {
            result.URL = GetFilePageUrl(entry["parent"]?.Value<string>("id") ?? folderID, id);
        }

        return result;
    }

    internal static string GetFilePageUrl(string parentID, string fileID) => $"https://app.box.com/files/0/f/{parentID}/1/f_{fileID}";

    private string? CreateSharedLink(string id, BoxShareAccessLevel accessLevel)
    {
        string? response = SendRequest(XerahS.Uploaders.HttpMethod.PUT, "https://api.box.com/2.0/files/" + id,
            "{\"shared_link\": {\"access\": \"" + accessLevel.ToString().ToLowerInvariant() + "\"}}", headers: OAuth2Client.GetAuthHeaders(_authInfo));
        return string.IsNullOrEmpty(response) ? null : JObject.Parse(response)["shared_link"]?.Value<string>("url");
    }

    /// <summary>ShareX's GetFiles, keeping the folders. Returns null on failure.</summary>
    public List<BoxFolder>? GetFolders(string folderID)
    {
        if (!BoxProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return null;

        string? response = SendRequest(XerahS.Uploaders.HttpMethod.GET, $"https://api.box.com/2.0/folders/{folderID}/items",
            headers: OAuth2Client.GetAuthHeaders(_authInfo));
        if (string.IsNullOrEmpty(response)) return null;

        return (JObject.Parse(response)["entries"] as JArray ?? [])
            .OfType<JObject>()
            .Where(item => item.Value<string>("type") == "folder")
            .Select(item => new BoxFolder { ID = item.Value<string>("id") ?? string.Empty, Name = item.Value<string>("name") ?? string.Empty })
            .ToList();
    }
}
