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

namespace ShareX.ImageChest.Plugin;

/// <summary>Creates an Image Chest post with the image, using its API v1 (https://imgchest.com/docs/api/1.0).</summary>
public sealed class ImageChestUploader : ImageUploader
{
    internal const string PostUrl = "https://api.imgchest.com/v1/post";
    internal const string PostPageUrl = "https://imgchest.com/p/";
    internal static readonly string[] PrivacyValues = ["hidden", "public", "secret"];

    private readonly ImageChestConfigModel _config;
    private readonly string _accessToken;

    public ImageChestUploader(ImageChestConfigModel config, string? accessToken)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _accessToken = accessToken ?? string.Empty;
        ReturnResponseOnError = true;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            Errors.Add(ImageChestProvider.MissingTokenMessage);
            return new UploadResult();
        }

        // Without Accept: application/json, the API answers unauthenticated requests with a redirect to its front page.
        var headers = new NameValueCollection
        {
            ["Authorization"] = "Bearer " + _accessToken,
            ["Accept"] = "application/json"
        };

        UploadResult result = SendRequestFile(PostUrl, stream, fileName, "images[]", CreateArguments(_config), headers);
        ApplyResponse(result, _config.DirectLink, Errors);
        return result;
    }

    internal static Dictionary<string, string> CreateArguments(ImageChestConfigModel config)
    {
        string privacy = PrivacyValues.Contains(config.Privacy) ? config.Privacy : "hidden";
        var args = new Dictionary<string, string>
        {
            ["privacy"] = privacy,
            ["nsfw"] = config.Nsfw ? "true" : "false",
            ["anonymous"] = config.Anonymous ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(config.Title))
        {
            args["title"] = config.Title.Trim();
        }

        return args;
    }

    /// <summary>Sets the image (or post page) and deletion URLs from Image Chest's response, or records its error.</summary>
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
            errors.Add("Image Chest returned a response that is not JSON.");
            return;
        }

        if (response["data"] is JObject data)
        {
            string? postId = data.Value<string>("id");
            string? imageLink = (data["images"] as JArray)?.FirstOrDefault()?.Value<string>("link");
            string? url = directLink ? imageLink : string.IsNullOrEmpty(postId) ? null : PostPageUrl + postId;
            if (!string.IsNullOrEmpty(url))
            {
                result.URL = url;
                result.DeletionURL = data.Value<string>("delete_url");
                return;
            }
        }

        result.IsSuccess = false;
        string? message = response.Value<string>("message") ?? response["error"]?.ToString();
        if (response["errors"] is JObject fieldErrors)
        {
            string details = string.Join(" ", fieldErrors.Properties().SelectMany(p => p.Value.Values<string>()).Where(m => !string.IsNullOrEmpty(m)));
            if (details.Length > 0) message = string.IsNullOrEmpty(message) ? details : message + " " + details;
        }

        errors.Add(string.IsNullOrEmpty(message) ? "Image Chest did not return an image URL." : "Image Chest: " + message);
    }
}
