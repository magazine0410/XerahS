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

namespace ShareX.Chevereto.Plugin;

/// <summary>ShareX's Chevereto uploader: the image goes to the server's API with the API key, as JSON.</summary>
public sealed class CheveretoUploader : ImageUploader
{
    private readonly CheveretoConfigModel _config;
    private readonly string _apiKey;

    public CheveretoUploader(CheveretoConfigModel config, string? apiKey)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _apiKey = apiKey ?? string.Empty;
        ReturnResponseOnError = true;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        string? error = CheveretoProvider.GetConfigError(_config, _apiKey);
        if (error != null)
        {
            Errors.Add(error);
            return new UploadResult();
        }

        var args = new Dictionary<string, string>
        {
            ["key"] = _apiKey,
            ["format"] = "json"
        };

        UploadResult result = SendRequestFile(URLHelpers.FixPrefix(_config.UploadURL.Trim()), stream, fileName, "source", args);
        ApplyResponse(result, _config.DirectURL, Errors);
        return result;
    }

    /// <summary>Sets the image (or viewer) and thumbnail URLs from Chevereto's response, or records its error.</summary>
    internal static void ApplyResponse(UploadResult result, bool directURL, UploaderErrorManager errors)
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
            errors.Add("The Chevereto server returned a response that is not JSON. Check the upload URL.");
            return;
        }

        if (response["image"] is JObject image)
        {
            string? url = directURL ? image.Value<string>("url") : image.Value<string>("url_viewer");
            if (!string.IsNullOrEmpty(url))
            {
                result.URL = url;
                result.ThumbnailURL = image["thumb"]?.Value<string>("url");
                return;
            }
        }

        result.IsSuccess = false;
        string? message = response["error"]?.Value<string>("message");
        errors.Add(string.IsNullOrEmpty(message) ? "The Chevereto server did not return an image URL." : "Chevereto: " + message);
    }
}
