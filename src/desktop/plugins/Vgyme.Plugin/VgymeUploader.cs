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

namespace ShareX.Vgyme.Plugin;

/// <summary>ShareX's vgy.me uploader: the image goes to vgy.me/upload, with the user key when there is one.</summary>
public sealed class VgymeUploader : ImageUploader
{
    internal const string UploadUrl = "https://vgy.me/upload";

    private readonly string _userKey;

    public VgymeUploader(string? userKey)
    {
        _userKey = userKey ?? string.Empty;
        ReturnResponseOnError = true;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        var args = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(_userKey))
        {
            args.Add("userkey", _userKey);
        }

        UploadResult result = SendRequestFile(UploadUrl, stream, fileName, "file", args);
        ApplyResponse(result, Errors);
        return result;
    }

    /// <summary>Sets the image and deletion URLs from vgy.me's response, or records its error messages.</summary>
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
            errors.Add("vgy.me returned a response that is not JSON.");
            return;
        }

        string? image = response.Value<string>("image");
        if (response.Value<bool?>("error") != true && !string.IsNullOrEmpty(image))
        {
            result.URL = image;
            result.DeletionURL = response.Value<string>("delete");
            return;
        }

        result.IsSuccess = false;
        IEnumerable<string> messages = response["messages"] switch
        {
            JObject values => values.Properties().Select(p => p.Value.ToString()),
            JValue value => [value.ToString()],
            _ => []
        };
        string message = string.Join(" ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
        errors.Add(string.IsNullOrEmpty(message) ? "vgy.me did not return an image URL." : "vgy.me: " + message);
    }
}
