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

using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;

namespace ShareX.ImageShack.Plugin;

/// <summary>ShareX's ImageShack uploader (API v2): logs in for an auth token and uploads with the API key.</summary>
public sealed class ImageShackUploader : ImageUploader
{
    internal const string ApiUrl = "https://api.imageshack.com/v2/";
    internal const string LoginUrl = ApiUrl + "user/login";
    internal const string UploadUrl = ApiUrl + "images";

    private readonly ImageShackConfigModel _config;
    private readonly string _apiKey;
    private readonly string _authToken;

    public ImageShackUploader(ImageShackConfigModel config, string? apiKey, string? authToken)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _apiKey = apiKey ?? string.Empty;
        _authToken = authToken ?? string.Empty;
        ReturnResponseOnError = true;
    }

    /// <summary>ShareX's GetAccessTokenAsync: returns the auth token, or null with the error in <see cref="Uploader.Errors"/>.</summary>
    public string? Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            Errors.Add("Enter the ImageShack username and password.");
            return null;
        }

        var args = new Dictionary<string, string> { ["user"] = username, ["password"] = password };
        return ParseLoginResponse(SendRequestMultiPart(LoginUrl, args), Errors);
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        string? error = ImageShackProvider.GetConfigError(_apiKey, _authToken);
        if (error != null)
        {
            Errors.Add(error);
            return new UploadResult();
        }

        var args = new Dictionary<string, string>
        {
            ["api_key"] = _apiKey,
            ["auth_token"] = _authToken,
            ["public"] = _config.IsPublic ? "y" : "n"
        };

        UploadResult result = SendRequestFile(UploadUrl, stream, fileName, "file", args);
        ApplyResponse(result, _config, Errors);
        return result;
    }

    internal static string? ParseLoginResponse(string? response, UploaderErrorManager errors)
    {
        JObject? json = ParseJson(response);
        string? token = json?["result"]?.Value<string>("auth_token");
        if (json?.Value<bool?>("success") == true && !string.IsNullOrEmpty(token))
        {
            return token;
        }

        errors.Add(GetErrorMessage(json) ?? "ImageShack login failed.");
        return null;
    }

    /// <summary>Builds ShareX's image and thumbnail URLs from ImageShack's response, or records its error.</summary>
    internal static void ApplyResponse(UploadResult result, ImageShackConfigModel config, UploaderErrorManager errors)
    {
        if (string.IsNullOrEmpty(result.Response))
        {
            return;
        }

        JObject? json = ParseJson(result.Response);
        if (json?.Value<bool?>("success") == true && json["result"]?["images"] is JArray { Count: > 0 } images)
        {
            JToken image = images[0];
            string server = image.Value<string>("server") ?? string.Empty;
            string bucket = image.Value<string>("bucket") ?? string.Empty;
            string filename = image.Value<string>("filename") ?? string.Empty;
            if (filename.Length > 0)
            {
                result.URL = string.Format(CultureInfo.InvariantCulture, "https://imagizer.imageshack.com/a/img{0}/{1}/{2}", server, bucket, filename);
                result.ThumbnailURL = string.Format(CultureInfo.InvariantCulture, "https://imagizer.imageshack.us/v2/{0}x{1}q90/{2}/{3}",
                    config.ThumbnailWidth, config.ThumbnailHeight, server, filename);
                return;
            }
        }

        result.IsSuccess = false;
        errors.Add(GetErrorMessage(json) ?? "ImageShack did not return an image URL.");
    }

    private static JObject? ParseJson(string? response)
    {
        if (string.IsNullOrEmpty(response)) return null;
        try { return JObject.Parse(response); }
        catch (JsonException) { return null; }
    }

    /// <summary>ShareX's ImageShackErrorInfo text: the error message and code.</summary>
    private static string? GetErrorMessage(JObject? json)
    {
        string? message = json?["error"]?.Value<string>("error_message");
        if (string.IsNullOrEmpty(message)) return null;
        string? code = json!["error"]!.Value<string>("error_code");
        return string.IsNullOrEmpty(code) ? "ImageShack: " + message : $"ImageShack: {message} (error code {code})";
    }
}
