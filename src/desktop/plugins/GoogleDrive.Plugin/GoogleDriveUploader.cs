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

using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;

namespace ShareX.GoogleDrive.Plugin;

/// <summary>
/// ShareX's Google Drive uploader: a multipart upload into the folder or shared drive, then, when public, a permission
/// for anyone with the link; the result is the file's page or, with the direct link, its download link.
/// </summary>
public sealed class GoogleDriveUploader : FileUploader
{
    private const string UploadUrl = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,webViewLink,webContentLink&supportsAllDrives=true";
    private const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const string DrivesUrl = "https://www.googleapis.com/drive/v3/drives";

    private readonly OAuth2Info _authInfo;
    private readonly Action<OAuth2Token>? _tokenRefreshed;
    private readonly GoogleDriveConfigModel _config;

    public GoogleDriveUploader(OAuth2Info authInfo, Action<OAuth2Token>? tokenRefreshed, GoogleDriveConfigModel config)
    {
        _authInfo = authInfo;
        _tokenRefreshed = tokenRefreshed;
        _config = config;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (!GoogleDriveProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return new UploadResult();

        string metadata = CreateMetadata(fileName, _config.UseFolder ? _config.FolderID : null, _config.DriveID);
        UploadResult result = SendRequestFile(UploadUrl, stream, fileName, "file", headers: OAuth2Client.GetAuthHeaders(_authInfo),
            contentType: "multipart/related", relatedData: metadata);
        if (string.IsNullOrEmpty(result.Response)) return result;

        JObject upload = JObject.Parse(result.Response);
        string? id = upload.Value<string>("id");
        if (string.IsNullOrEmpty(id))
        {
            result.IsSuccess = false;
            return result;
        }

        AllowReportProgress = false;
        if (_config.IsPublic)
        {
            SetPublicPermission(id);
        }

        result.URL = _config.DirectLink ? GetDirectLink(upload.Value<string>("webContentLink")) : upload.Value<string>("webViewLink");
        return result;
    }

    /// <summary>ShareX's metadata: without a folder, the shared drive is the parent.</summary>
    internal static string CreateMetadata(string name, string? parentID, string? driveID)
    {
        if (string.IsNullOrEmpty(parentID)) parentID = driveID;
        object metadata = string.IsNullOrEmpty(parentID)
            ? new { name }
            : new { name, driveId = driveID ?? string.Empty, parents = new[] { parentID } };
        return JsonConvert.SerializeObject(metadata);
    }

    /// <summary>ShareX's direct link: the webContentLink without its "export" argument.</summary>
    internal static string? GetDirectLink(string? webContentLink)
    {
        if (string.IsNullOrEmpty(webContentLink)) return webContentLink;
        var uri = new Uri(webContentLink);
        var query = HttpUtility.ParseQueryString(uri.Query);
        query.Remove("export");
        return $"{uri.GetLeftPart(UriPartial.Path)}?{query}";
    }

    private void SetPublicPermission(string fileID)
    {
        string json = JsonConvert.SerializeObject(new { role = "reader", type = "anyone", allowFileDiscovery = false });
        SendRequest(XerahS.Uploaders.HttpMethod.POST, $"{FilesUrl}/{fileID}/permissions?supportsAllDrives=true", json, "application/json",
            headers: OAuth2Client.GetAuthHeaders(_authInfo));
    }

    /// <summary>ShareX's GetDrives: every page of the shared drive list. Returns null on failure.</summary>
    public List<GoogleDriveItem>? GetDrives()
    {
        if (!GoogleDriveProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return null;
        return GetPages(DrivesUrl, new Dictionary<string, string>(), "drives");
    }

    /// <summary>ShareX's GetFolders: the folders that are not in the trash and, in My Drive, that you can write to.</summary>
    public List<GoogleDriveItem>? GetFolders(string driveID)
    {
        if (!GoogleDriveProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return null;

        string query = "mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        if (string.IsNullOrEmpty(driveID)) query += " and 'me' in writers";

        var args = new Dictionary<string, string> { ["q"] = query, ["fields"] = "nextPageToken,files(id,name,description)" };
        if (!string.IsNullOrEmpty(driveID))
        {
            args["driveId"] = driveID;
            args["corpora"] = "drive";
            args["supportsAllDrives"] = "true";
            args["includeItemsFromAllDrives"] = "true";
        }

        return GetPages(FilesUrl, args, "files");
    }

    private List<GoogleDriveItem>? GetPages(string url, Dictionary<string, string> args, string listName)
    {
        var items = new List<GoogleDriveItem>();
        string pageToken = string.Empty;
        do
        {
            args["pageToken"] = pageToken;
            string? response = SendRequest(XerahS.Uploaders.HttpMethod.GET, url, args, OAuth2Client.GetAuthHeaders(_authInfo));
            if (string.IsNullOrEmpty(response)) return null;

            JObject page = JObject.Parse(response);
            items.AddRange((page[listName] as JArray ?? []).OfType<JObject>()
                .Select(item => new GoogleDriveItem { ID = item.Value<string>("id") ?? string.Empty, Name = item.Value<string>("name") ?? string.Empty }));
            pageToken = page.Value<string>("nextPageToken") ?? string.Empty;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return items;
    }
}
