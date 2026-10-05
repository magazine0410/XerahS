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

namespace ShareX.ImgBB.Plugin;

/// <summary>Uploads an image with ImgBB's API v1 (https://api.imgbb.com/).</summary>
public sealed class ImgBBUploader : ImageUploader
{
    internal const string UploadUrl = "https://api.imgbb.com/1/upload";
    internal const int MinimumExpiration = 60;
    internal const int MaximumExpiration = 15552000;

    private readonly ImgBBConfigModel _config;
    private readonly string _apiKey;

    public ImgBBUploader(ImgBBConfigModel config, string? apiKey)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _apiKey = apiKey ?? string.Empty;
        ReturnResponseOnError = true;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            Errors.Add(ImgBBProvider.MissingApiKeyMessage);
            return new UploadResult();
        }

        UploadResult result = SendRequestFile(UploadUrl, stream, fileName, "image", CreateArguments(_config, _apiKey));
        ApplyResponse(result, _config.DirectLink, Errors);
        return result;
    }

    internal static Dictionary<string, string> CreateArguments(ImgBBConfigModel config, string apiKey)
    {
        var args = new Dictionary<string, string> { ["key"] = apiKey };
        if (config.Expiration > 0)
        {
            args["expiration"] = Math.Clamp(config.Expiration, MinimumExpiration, MaximumExpiration).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return args;
    }

    /// <summary>Sets the image (or viewer), thumbnail, and deletion URLs from ImgBB's response, or records its error.</summary>
    internal static void ApplyResponse(UploadResult result, bool directLink, UploaderErrorManager errors)
    {
        if (string.IsNullOrEmpty(result.Response))
        {
            return;
        }

        JObject? response;
        try
        {
            response = JObject.Parse(result.Response);
        }
        catch (JsonException)
        {
            result.IsSuccess = false;
            errors.Add("ImgBB returned a response that is not JSON.");
            return;
        }

        if (response["data"] is JObject data)
        {
            string? url = directLink ? data.Value<string>("url") : data.Value<string>("url_viewer");
            if (!string.IsNullOrEmpty(url))
            {
                result.URL = url;
                result.ThumbnailURL = data["thumb"]?.Value<string>("url");
                result.DeletionURL = data.Value<string>("delete_url");
                return;
            }
        }

        result.IsSuccess = false;
        string? message = response["error"]?.Value<string>("message");
        errors.Add(string.IsNullOrEmpty(message) ? "ImgBB did not return an image URL." : "ImgBB: " + message);
    }
}
