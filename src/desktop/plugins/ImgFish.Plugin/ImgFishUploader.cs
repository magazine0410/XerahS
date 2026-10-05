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
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;

namespace ShareX.ImgFish.Plugin;

/// <summary>ShareX's img.fish uploader: the file goes to img.fish/up with the file ID length and, when set, the API key.</summary>
public sealed class ImgFishUploader : FileUploader
{
    internal const string UploadUrl = "https://img.fish/up";

    private readonly string _apiKey;
    private readonly int _fileIDLength;

    public ImgFishUploader(string? apiKey, int fileIDLength)
    {
        _apiKey = apiKey ?? string.Empty;
        _fileIDLength = fileIDLength;
        ReturnResponseOnError = true;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        var args = new Dictionary<string, string> { ["length"] = _fileIDLength.ToString(CultureInfo.InvariantCulture) };
        UploadResult result = SendRequestFile(UploadUrl, stream, fileName, "file", args, CreateHeaders(_apiKey));
        ApplyResponse(result, Errors);
        return result;
    }

    internal static NameValueCollection CreateHeaders(string apiKey)
    {
        var headers = new NameValueCollection { ["Accept"] = "application/json" };
        if (!string.IsNullOrWhiteSpace(apiKey)) headers["x-api-key"] = apiKey;
        return headers;
    }

    /// <summary>Sets the link and deletion URL from img.fish's response, or records its error.</summary>
    internal static void ApplyResponse(UploadResult result, UploaderErrorManager errors)
    {
        if (string.IsNullOrEmpty(result.Response)) return;

        JObject? response = null;
        try { response = JObject.Parse(result.Response); }
        catch (JsonException) { }

        string? error = response?.Value<string>("error");
        string? link = response?.Value<string>("link");
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(link))
        {
            result.IsSuccess = false;
            errors.Add(string.IsNullOrEmpty(error) ? "img.fish did not return a link." : "img.fish: " + error);
            return;
        }

        result.URL = link;
        result.DeletionURL = response!.Value<string>("destroy");
    }
}
