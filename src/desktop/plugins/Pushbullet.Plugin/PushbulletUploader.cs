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

using System.Collections.Specialized;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;

namespace ShareX.Pushbullet.Plugin;

/// <summary>
/// ShareX's Pushbullet uploader: request an upload URL, post the file to it, and push the file to the chosen device.
/// </summary>
public sealed class PushbulletUploader : FileUploader
{
    internal const string PushesPageUrl = "https://www.pushbullet.com/pushes";
    internal const string ApiUrl = "https://api.pushbullet.com/v2";

    private readonly string _accessToken;
    private readonly string _deviceKey;

    public PushbulletUploader(string? accessToken, string? deviceKey)
    {
        _accessToken = accessToken ?? string.Empty;
        _deviceKey = deviceKey ?? string.Empty;
        ReturnResponseOnError = true;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            Errors.Add(PushbulletProvider.MissingAccessTokenMessage);
            return new UploadResult();
        }

        if (string.IsNullOrWhiteSpace(_deviceKey))
        {
            Errors.Add(PushbulletProvider.NoDeviceMessage);
            return new UploadResult();
        }

        var requestArgs = new Dictionary<string, string> { ["file_name"] = fileName };
        JObject? fileInfo = ParseObject(SendRequestMultiPart(ApiUrl + "/upload-request", requestArgs, CreateHeaders(_accessToken)));
        string? uploadUrl = fileInfo?.Value<string>("upload_url");
        if (fileInfo == null || string.IsNullOrEmpty(uploadUrl))
        {
            Errors.Add("Pushbullet did not return an upload URL.");
            return new UploadResult();
        }

        UploadResult result = SendRequestFile(uploadUrl, stream, fileName, "file", CreateUploadArguments(fileInfo));
        if (!result.IsSuccess) return result;

        JObject? push = ParseObject(SendRequestMultiPart(ApiUrl + "/pushes", CreatePushArguments(fileInfo, fileName, _deviceKey), CreateHeaders(_accessToken)));
        string? pushIden = push?.Value<string>("iden");
        if (string.IsNullOrEmpty(pushIden))
        {
            result.IsSuccess = false;
            Errors.Add("Pushbullet uploaded the file but did not create the push.");
            return result;
        }

        result.URL = PushesPageUrl + "?push_iden=" + pushIden;
        return result;
    }

    public List<PushbulletDevice> GetDeviceList()
    {
        JObject? response = ParseObject(SendRequest(XerahS.Uploaders.HttpMethod.GET, ApiUrl + "/devices", headers: CreateHeaders(_accessToken)));
        return ParseDevices(response);
    }

    // Pushbullet's documented header; ShareX sends the token as the Basic authentication user name.
    internal static NameValueCollection CreateHeaders(string accessToken) => new() { ["Access-Token"] = accessToken };

    /// <summary>
    /// ShareX posted the S3 fields from the response's "data" object. Pushbullet's documentation marks that object as
    /// deprecated and its example response has none, so the fields are only sent when it is present.
    /// </summary>
    internal static Dictionary<string, string> CreateUploadArguments(JObject fileInfo)
    {
        var args = new Dictionary<string, string>();
        if (fileInfo["data"] is JObject data)
        {
            foreach (string name in new[] { "awsaccesskeyid", "acl", "key", "signature", "policy", "content-type" })
            {
                if (data.Value<string>(name) is { } value) args[name] = value;
            }
        }

        return args;
    }

    internal static Dictionary<string, string> CreatePushArguments(JObject fileInfo, string fileName, string deviceKey) => new()
    {
        ["file_name"] = fileInfo.Value<string>("file_name") ?? fileName,
        ["device_iden"] = deviceKey,
        ["type"] = "file",
        ["file_url"] = fileInfo.Value<string>("file_url") ?? string.Empty,
        ["body"] = "Sent via XerahS",
        ["file_type"] = fileInfo.Value<string>("file_type") ?? string.Empty
    };

    /// <summary>As in ShareX, devices without a nickname are left out.</summary>
    internal static List<PushbulletDevice> ParseDevices(JObject? response) =>
        (response?["devices"] as JArray ?? [])
            .OfType<JObject>()
            .Where(device => !string.IsNullOrEmpty(device.Value<string>("nickname")))
            .Select(device => new PushbulletDevice { Key = device.Value<string>("iden") ?? string.Empty, Name = device.Value<string>("nickname")! })
            .ToList();

    private static JObject? ParseObject(string? response)
    {
        if (string.IsNullOrEmpty(response)) return null;
        try { return JObject.Parse(response); }
        catch (JsonException) { return null; }
    }
}
