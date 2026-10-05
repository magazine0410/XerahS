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

using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;

namespace ShareX.Flickr.Plugin;

/// <summary>ShareX's Flickr uploader: OAuth 1.0a with the "oob" verification code, upload, and the largest size's link.</summary>
public sealed class FlickrUploader : ImageUploader
{
    internal const string RequestTokenUrl = "https://www.flickr.com/services/oauth/request_token";
    internal const string AuthorizeUrl = "https://www.flickr.com/services/oauth/authorize";
    internal const string AccessTokenUrl = "https://www.flickr.com/services/oauth/access_token";
    internal const string UploadUrl = "https://up.flickr.com/services/upload/";
    internal const string RestUrl = "https://api.flickr.com/services/rest";

    private readonly FlickrConfigModel _config;
    private readonly string _consumerSecret;
    private readonly string _userToken;
    private readonly string _userSecret;

    public FlickrUploader(FlickrConfigModel config, string? consumerSecret, string? userToken = null, string? userSecret = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _consumerSecret = consumerSecret ?? string.Empty;
        _userToken = userToken ?? string.Empty;
        _userSecret = userSecret ?? string.Empty;
        ReturnResponseOnError = true;
    }

    /// <summary>
    /// Gets a request token and returns the authorization page's URL. As ShareX does, it asks for write permission. With the
    /// "oob" callback, Flickr shows the verification code to enter in the settings instead of redirecting.
    /// </summary>
    public (string AuthorizationUrl, string Token, string TokenSecret)? GetAuthorization()
    {
        var args = new Dictionary<string, string> { ["oauth_callback"] = "oob" };
        Dictionary<string, string> parameters = FlickrOAuth.Sign("GET", RequestTokenUrl, args, _config.ConsumerKey, _consumerSecret);
        Dictionary<string, string> response = FlickrOAuth.ParseResponse(SendRequest(XerahS.Uploaders.HttpMethod.GET, FlickrOAuth.ToQueryUrl(RequestTokenUrl, parameters)));
        if (!response.TryGetValue("oauth_token", out string? token) || !response.TryGetValue("oauth_token_secret", out string? secret))
        {
            AddError(response, "Flickr did not return a request token. Check the app key and secret.");
            return null;
        }

        return ($"{AuthorizeUrl}?oauth_token={FlickrOAuth.Encode(token)}&perms=write", token, secret);
    }

    /// <summary>Exchanges the request token and the verification code for the account's token, secret, and name.</summary>
    public (string Token, string TokenSecret, string UserName)? GetAccessToken(string requestToken, string requestTokenSecret, string verificationCode)
    {
        Dictionary<string, string> parameters = FlickrOAuth.Sign("GET", AccessTokenUrl, null, _config.ConsumerKey, _consumerSecret,
            requestToken, requestTokenSecret, verificationCode.Trim());
        Dictionary<string, string> response = FlickrOAuth.ParseResponse(SendRequest(XerahS.Uploaders.HttpMethod.GET, FlickrOAuth.ToQueryUrl(AccessTokenUrl, parameters)));
        if (!response.TryGetValue("oauth_token", out string? token) || !response.TryGetValue("oauth_token_secret", out string? secret))
        {
            AddError(response, "Flickr did not accept the verification code.");
            return null;
        }

        string userName = response.GetValueOrDefault("username") ?? response.GetValueOrDefault("fullname") ?? string.Empty;
        return (token, secret, userName);
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        string? error = FlickrProvider.GetConfigError(_config, _consumerSecret, _userToken, _userSecret);
        if (error != null)
        {
            Errors.Add(error);
            return new UploadResult();
        }

        Dictionary<string, string> parameters = FlickrOAuth.Sign("POST", UploadUrl, CreateUploadArguments(_config),
            _config.ConsumerKey, _consumerSecret, _userToken, _userSecret);
        UploadResult result = SendRequestFile(UploadUrl, stream, fileName, "photo", parameters);
        if (!result.IsSuccess)
        {
            return result;
        }

        string? photoId = ParseUploadResponse(result.Response, Errors);
        if (photoId == null)
        {
            result.IsSuccess = false;
            return result;
        }

        var args = new Dictionary<string, string>
        {
            ["nojsoncallback"] = "1",
            ["format"] = "json",
            ["method"] = "flickr.photos.getSizes",
            ["photo_id"] = photoId
        };
        Dictionary<string, string> sizeParameters = FlickrOAuth.Sign("GET", RestUrl, args, _config.ConsumerKey, _consumerSecret, _userToken, _userSecret);
        string? url = ParseSizesResponse(SendRequest(XerahS.Uploaders.HttpMethod.GET, FlickrOAuth.ToQueryUrl(RestUrl, sizeParameters)), _config.DirectLink);
        if (string.IsNullOrEmpty(url))
        {
            result.IsSuccess = false;
            Errors.Add("Flickr uploaded the photo (ID " + photoId + ") but did not return its sizes.");
            return result;
        }

        result.URL = url;
        return result;
    }

    /// <summary>As in ShareX, only the settings that are set are sent.</summary>
    internal static Dictionary<string, string> CreateUploadArguments(FlickrConfigModel config)
    {
        var args = new Dictionary<string, string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value)) args[name] = value;
        }

        Add("title", config.Title);
        Add("description", config.Description);
        Add("tags", config.Tags);
        Add("is_public", config.IsPublic);
        Add("is_friend", config.IsFriend);
        Add("is_family", config.IsFamily);
        Add("safety_level", config.SafetyLevel);
        Add("content_type", config.ContentType);
        Add("hidden", config.Hidden);
        return args;
    }

    /// <summary>Returns the photo ID from Flickr's XML upload response, or records its error message.</summary>
    internal static string? ParseUploadResponse(string? response, UploaderErrorManager errors)
    {
        XElement? rsp;
        try
        {
            rsp = string.IsNullOrEmpty(response) ? null : XDocument.Parse(response).Element("rsp");
        }
        catch (System.Xml.XmlException)
        {
            rsp = null;
        }

        if (rsp?.Attribute("stat")?.Value == "ok" && rsp.Element("photoid")?.Value is { Length: > 0 } photoId)
        {
            return photoId;
        }

        string? message = rsp?.Element("err")?.Attribute("msg")?.Value;
        errors.Add(string.IsNullOrEmpty(message) ? "Flickr did not return a photo ID." : "Flickr: " + message);
        return null;
    }

    /// <summary>As in ShareX, the last (largest) size: its file (direct link) or its page.</summary>
    internal static string? ParseSizesResponse(string? response, bool directLink)
    {
        if (string.IsNullOrEmpty(response)) return null;
        try
        {
            JToken? last = (JObject.Parse(response)["sizes"]?["size"] as JArray)?.LastOrDefault();
            return last?.Value<string>(directLink ? "source" : "url");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void AddError(Dictionary<string, string> response, string fallback)
    {
        string? problem = response.GetValueOrDefault("oauth_problem");
        Errors.Add(string.IsNullOrEmpty(problem) ? fallback : fallback + " (" + problem + ")");
    }
}
