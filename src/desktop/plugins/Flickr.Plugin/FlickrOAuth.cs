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
using System.Security.Cryptography;
using System.Text;

namespace ShareX.Flickr.Plugin;

/// <summary>OAuth 1.0a request signing with HMAC-SHA1, the only method Flickr supports (ported from ShareX's OAuthManager).</summary>
internal static class FlickrOAuth
{
    /// <summary>
    /// Returns the request's parameters with the OAuth parameters and their signature added. The nonce and timestamp are
    /// generated unless given.
    /// </summary>
    public static Dictionary<string, string> Sign(string method, string url, IReadOnlyDictionary<string, string>? args,
        string consumerKey, string consumerSecret, string? token = null, string? tokenSecret = null, string? verifier = null,
        string? nonce = null, string? timestamp = null)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_version"] = "1.0",
            ["oauth_nonce"] = nonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
            ["oauth_timestamp"] = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["oauth_consumer_key"] = consumerKey,
            ["oauth_signature_method"] = "HMAC-SHA1"
        };

        if (!string.IsNullOrEmpty(token)) parameters["oauth_token"] = token;
        if (!string.IsNullOrEmpty(verifier)) parameters["oauth_verifier"] = verifier;
        if (args != null)
        {
            foreach (KeyValuePair<string, string> arg in args) parameters[arg.Key] = arg.Value;
        }

        string signatureBase = method.ToUpperInvariant() + "&" + Encode(NormalizeUrl(url)) + "&" + Encode(NormalizeParameters(parameters));
        string key = Encode(consumerSecret) + "&" + Encode(tokenSecret ?? string.Empty);
        using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(key));
        parameters["oauth_signature"] = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(signatureBase)));
        return parameters;
    }

    /// <summary>The URL with the signed parameters as its query string.</summary>
    public static string ToQueryUrl(string url, IReadOnlyDictionary<string, string> parameters) =>
        NormalizeUrl(url) + "?" + NormalizeParameters(parameters);

    /// <summary>Parses a form-encoded token response, such as oauth_token=…&amp;oauth_token_secret=….</summary>
    public static Dictionary<string, string> ParseResponse(string? response)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(response)) return values;
        foreach (string pair in response.Trim().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string name = Uri.UnescapeDataString((separator < 0 ? pair : pair[..separator]).Replace('+', ' '));
            string value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            values[name] = value;
        }

        return values;
    }

    /// <summary>RFC 3986 percent-encoding, as OAuth 1.0a requires.</summary>
    public static string Encode(string value) => Uri.EscapeDataString(value);

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return url;
        bool defaultPort = (uri.Scheme == Uri.UriSchemeHttp && uri.Port == 80) || (uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443);
        return uri.Scheme + "://" + uri.Host + (defaultPort ? string.Empty : ":" + uri.Port) + uri.AbsolutePath;
    }

    private static string NormalizeParameters(IReadOnlyDictionary<string, string> parameters) =>
        string.Join("&", parameters
            .Select(p => (Key: Encode(p.Key), Value: Encode(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => p.Key + "=" + p.Value));
}
