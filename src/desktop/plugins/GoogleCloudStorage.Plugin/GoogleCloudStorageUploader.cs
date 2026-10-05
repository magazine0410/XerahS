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

namespace ShareX.GoogleCloudStorage.Plugin;

/// <summary>
/// ShareX's Google Cloud Storage uploader: a multipart upload to the bucket under the object prefix, with a public
/// ACL when set; the result is the object's URL on the domain.
/// </summary>
public sealed class GoogleCloudStorageUploader : FileUploader
{
    private readonly OAuth2Info _authInfo;
    private readonly Action<OAuth2Token>? _tokenRefreshed;
    private readonly GoogleCloudStorageConfigModel _config;

    public GoogleCloudStorageUploader(OAuth2Info authInfo, Action<OAuth2Token>? tokenRefreshed, GoogleCloudStorageConfigModel config)
    {
        _authInfo = authInfo;
        _tokenRefreshed = tokenRefreshed;
        _config = config;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (!GoogleCloudStorageProvider.OAuth.CheckAuthorization(this, _authInfo, _tokenRefreshed)) return new UploadResult();

        string uploadPath = GetUploadPath(_config, fileName);
        OnEarlyURLCopyRequested(GenerateURL(_config, uploadPath));

        UploadResult result = SendRequestFile(
            $"https://www.googleapis.com/upload/storage/v1/b/{_config.Bucket}/o?uploadType=multipart&fields=name",
            stream, fileName, "file", headers: OAuth2Client.GetAuthHeaders(_authInfo), contentType: "multipart/related",
            relatedData: CreateMetadata(uploadPath, _config.SetPublicACL));

        string? name = string.IsNullOrEmpty(result.Response) ? null : JObject.Parse(result.Response).Value<string>("name");
        if (string.IsNullOrEmpty(name))
        {
            result.IsSuccess = false;
            return result;
        }

        result.URL = GenerateURL(_config, name);
        return result;
    }

    internal static string CreateMetadata(string uploadPath, bool setPublicACL)
    {
        var metadata = new JObject { ["name"] = uploadPath };
        if (setPublicACL)
        {
            metadata["acl"] = new JArray(new JObject { ["entity"] = "allUsers", ["role"] = "READER" });
        }

        return metadata.ToString(Formatting.None);
    }

    /// <summary>ShareX's GetUploadPath: the parsed prefix, and the file name without its extension when set for its type.</summary>
    internal static string GetUploadPath(GoogleCloudStorageConfigModel config, string fileName)
    {
        string uploadPath = NameParser.Parse(NameParserType.FilePath, config.ObjectPrefix.Trim('/'));
        if ((config.RemoveExtensionImage && FileHelpers.IsImageFile(fileName)) ||
            (config.RemoveExtensionText && FileHelpers.IsTextFile(fileName)) ||
            (config.RemoveExtensionVideo && FileHelpers.IsVideoFile(fileName)))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        return URLHelpers.CombineURL(uploadPath, fileName);
    }

    /// <summary>ShareX's GenerateURL: the domain, or storage.googleapis.com/bucket, and the encoded path.</summary>
    internal static string GenerateURL(GoogleCloudStorageConfigModel config, string uploadPath)
    {
        if (string.IsNullOrEmpty(config.Bucket)) return string.Empty;

        string domain = string.IsNullOrEmpty(config.Domain) ? URLHelpers.CombineURL("storage.googleapis.com", config.Bucket) : config.Domain;
        return URLHelpers.FixPrefix(URLHelpers.CombineURL(domain, URLHelpers.URLEncode(uploadPath, true, true)));
    }

    public static string GetPreviewURL(GoogleCloudStorageConfigModel config) => GenerateURL(config, GetUploadPath(config, "example.png"));
}
