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

namespace ShareX.Sul.Plugin;

/// <summary>ShareX's s-ul uploader: the file goes to s-ul.eu/api/v1/upload with the API key.</summary>
public sealed class SulUploader : FileUploader
{
    internal const string Host = "https://s-ul.eu";

    private readonly string _apiKey;

    public SulUploader(string? apiKey)
    {
        _apiKey = apiKey ?? string.Empty;
        ReturnResponseOnError = true;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            Errors.Add(SulProvider.MissingApiKeyMessage);
            return new UploadResult();
        }

        UploadResult result = SendRequestFile(URLHelpers.CombineURL(Host, "api/v1/upload"), stream, fileName, "file", CreateArguments(_apiKey));
        ApplyResponse(result, _apiKey, Errors);
        return result;
    }

    /// <summary>ShareX's arguments, including its client name, which s-ul uses to identify the uploader.</summary>
    internal static Dictionary<string, string> CreateArguments(string apiKey) => new()
    {
        ["wizard"] = "true",
        ["key"] = apiKey,
        ["client"] = "sharex-native"
    };

    /// <summary>Builds the file and deletion URLs from s-ul's response, or records its error.</summary>
    internal static void ApplyResponse(UploadResult result, string apiKey, UploaderErrorManager errors)
    {
        if (string.IsNullOrEmpty(result.Response)) return;

        JObject? response = null;
        try { response = JObject.Parse(result.Response); }
        catch (JsonException) { }

        string? protocol = response?.Value<string>("protocol");
        string? error = response?.Value<string>("error") ?? response?.Value<string>("reason");
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(protocol))
        {
            result.IsSuccess = false;
            errors.Add(string.IsNullOrEmpty(error) ? "A generic error occurred. Contact support@s-ul.eu." : "s-ul: " + error);
            return;
        }

        string file = response!.Value<string>("filename") ?? string.Empty;
        result.URL = protocol + response.Value<string>("domain") + "/" + file + response.Value<string>("extension");
        result.DeletionURL = URLHelpers.CombineURL(Host, "delete.php?key=" + apiKey + "&file=" + file);
    }
}
