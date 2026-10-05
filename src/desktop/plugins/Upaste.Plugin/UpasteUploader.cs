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

namespace ShareX.Upaste.Plugin;

/// <summary>ShareX's uPaste uploader: the text goes to upaste.me's API v2 with the user key.</summary>
public sealed class UpasteUploader : TextUploader
{
    internal const string ApiUrl = "https://upaste.me/api/v2/paste";

    private readonly string _userKey;
    private readonly bool _isPublic;

    public UpasteUploader(string? userKey, bool isPublic)
    {
        _userKey = userKey ?? string.Empty;
        _isPublic = isPublic;
        ReturnResponseOnError = true;
    }

    public override UploadResult UploadText(string text, string fileName)
    {
        var result = new UploadResult();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        if (string.IsNullOrWhiteSpace(_userKey))
        {
            Errors.Add(UpasteProvider.MissingUserKeyMessage);
            return result;
        }

        var headers = new System.Collections.Specialized.NameValueCollection { ["Authorization"] = "Bearer " + _userKey };
        result.Response = SendRequestMultiPart(ApiUrl, CreateArguments(text, _isPublic), headers);
        ApplyResponse(result, Errors);
        return result;
    }

    /// <summary>ShareX's arguments: the text, privacy 0 (public) or 1 (unlisted), and expire 0 (never).</summary>
    internal static Dictionary<string, string> CreateArguments(string text, bool isPublic) => new()
    {
        ["paste"] = text,
        ["privacy"] = isPublic ? "0" : "1",
        ["expire"] = "0"
    };

    /// <summary>Sets the paste link from uPaste's response, or records its error.</summary>
    internal static void ApplyResponse(UploadResult result, UploaderErrorManager errors)
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
            errors.Add("uPaste returned a response that is not JSON.");
            return;
        }

        string? link = response["paste"]?.Value<string>("link");
        if (string.Equals(response.Value<string>("status"), "success", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(link))
        {
            result.URL = link;
            return;
        }

        result.IsSuccess = false;
        string? error = response.Value<string>("error");
        errors.Add(string.IsNullOrEmpty(error) ? "uPaste did not return a paste link." : "uPaste: " + error);
    }
}
