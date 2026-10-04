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
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Common;
using XerahS.Uploaders;

namespace ShareX.BackblazeB2.Plugin;

/// <summary>
/// ShareX's Backblaze B2 uploader (B2 API v1): authorize, find the bucket, check the key's permissions, get an upload
/// URL, and upload with retries; the result is the download URL or the custom URL.
/// </summary>
public sealed class BackblazeB2Uploader : FileUploader
{
    internal const string AuthorizeAccountUrl = "https://api.backblazeb2.com/b2api/v1/b2_authorize_account";
    private const string GetUploadUrlPath = "/b2api/v1/b2_get_upload_url";
    private const string ListBucketsPath = "/b2api/v1/b2_list_buckets";
    private const string ApplicationJson = "application/json; charset=utf-8";
    private const int MaxTries = 5;

    private readonly string _applicationKeyId;
    private readonly string _applicationKey;
    private readonly BackblazeB2ConfigModel _config;

    public BackblazeB2Uploader(string? applicationKeyId, string? applicationKey, BackblazeB2ConfigModel config)
    {
        _applicationKeyId = applicationKeyId ?? string.Empty;
        _applicationKey = applicationKey ?? string.Empty;
        _config = config;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        if (string.IsNullOrWhiteSpace(_applicationKeyId) || string.IsNullOrWhiteSpace(_applicationKey))
        {
            Errors.Add(BackblazeB2Provider.MissingKeyMessage);
            return new UploadResult();
        }

        string destinationPath = URLHelpers.CombineURL(NameParser.Parse(NameParserType.FilePath, _config.UploadPath), fileName);

        // Step 1: authorize, and get the auth token, API URL, and download URL.
        B2Response authResponse = Send(XerahS.Uploaders.HttpMethod.GET, AuthorizeAccountUrl,
            CreateBasicAuthHeader(_applicationKeyId, _applicationKey), null, null);
        if (authResponse.Status != HttpStatusCode.OK)
        {
            Errors.Add("Could not authenticate with B2: " + DescribeError(authResponse));
            return new UploadResult();
        }

        JObject auth = JObject.Parse(authResponse.Text);
        string apiUrl = auth.Value<string>("apiUrl") ?? string.Empty;
        string authToken = auth.Value<string>("authorizationToken") ?? string.Empty;

        // Step 1.25: an application key restricted to a bucket names it; otherwise find the bucket by name.
        string? bucketId = auth["allowed"]?.Value<string>("bucketId");
        if (bucketId == null)
        {
            B2Response bucketsResponse = SendJson(apiUrl + ListBucketsPath, authToken, new JObject
            {
                ["accountId"] = auth.Value<string>("accountId"),
                ["bucketName"] = _config.BucketName
            });
            if (bucketsResponse.Status != HttpStatusCode.OK)
            {
                Errors.Add("B2 upload failed: " + DescribeError(bucketsResponse));
                return new UploadResult();
            }

            bucketId = FindBucketId(bucketsResponse.Text, _config.BucketName);
            if (string.IsNullOrWhiteSpace(bucketId))
            {
                Errors.Add($"B2 upload failed: Couldn't find bucket {_config.BucketName}.");
                return new UploadResult();
            }
        }

        // Step 1.5: check whether the key can write to this bucket and path.
        string? permissionError = CheckUploadPermission(auth["allowed"] as JObject, bucketId, destinationPath);
        if (permissionError != null)
        {
            Errors.Add("B2 upload failed: " + permissionError);
            return new UploadResult();
        }

        string sha1 = ComputeSha1(stream);
        JObject? uploadUrl = null;
        for (int attempt = 1; attempt <= MaxTries; attempt++)
        {
            if (attempt > 1)
            {
                Thread.Sleep((int)Math.Pow(2, attempt - 1) * 1000);
            }

            // Step 2: get an upload URL.
            if (uploadUrl == null)
            {
                B2Response uploadUrlResponse = SendJson(apiUrl + GetUploadUrlPath, authToken, new JObject { ["bucketId"] = bucketId });
                if (uploadUrlResponse.Status != HttpStatusCode.OK)
                {
                    Errors.Add("Could not get a B2 upload URL: " + DescribeError(uploadUrlResponse));
                    return new UploadResult();
                }

                uploadUrl = JObject.Parse(uploadUrlResponse.Text);
            }

            // Step 3: upload the file; retry on the failures B2 documents as temporary.
            stream.Seek(0, SeekOrigin.Begin);
            B2Response upload = Send(XerahS.Uploaders.HttpMethod.POST, uploadUrl.Value<string>("uploadUrl") ?? string.Empty,
                CreateUploadHeaders(uploadUrl.Value<string>("authorizationToken") ?? string.Empty, destinationPath, stream.Length, sha1),
                stream, MimeTypes.GetMimeTypeFromFileName(destinationPath));

            switch (GetRetry(upload))
            {
                case Retry.NewUrl:
                    uploadUrl = null;
                    continue;
                case Retry.SameUrl:
                    continue;
                case Retry.Fail:
                    Errors.Add("B2 upload failed: " + DescribeError(upload));
                    return new UploadResult();
            }

            // Step 4: the download URL, or the custom URL.
            string uploadedName = JObject.Parse(upload.Text).Value<string>("fileName") ?? destinationPath;
            return new UploadResult
            {
                IsSuccess = true,
                Response = upload.Text,
                URL = BuildUrl(auth.Value<string>("downloadUrl") ?? string.Empty, _config, uploadedName)
            };
        }

        Errors.Add($"B2 upload failed after {MaxTries} attempts.");
        return new UploadResult();
    }

    internal enum Retry { None, NewUrl, SameUrl, Fail }

    /// <summary>ShareX's retry rules: a failed connection, an expired token, and 503 get a new URL; 408 and 429 retry the same URL.</summary>
    internal static Retry GetRetry(B2Response response)
    {
        if (response.Status == 0) return Retry.NewUrl;
        if (response.Status == HttpStatusCode.Unauthorized && ReadErrorCode(response.Text) is "expired_auth_token" or "bad_auth_token") return Retry.NewUrl;
        if (response.Status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests) return Retry.SameUrl;
        if (response.Status == HttpStatusCode.ServiceUnavailable) return Retry.NewUrl;
        return response.Status == HttpStatusCode.OK ? Retry.None : Retry.Fail;
    }

    internal static string BuildUrl(string downloadUrl, BackblazeB2ConfigModel config, string uploadedName)
    {
        string encodedFileName = URLHelpers.URLEncode(uploadedName, true);
        if (config.UseCustomUrl)
        {
            return URLHelpers.FixPrefix(URLHelpers.CombineURL(config.CustomUrl, encodedFileName));
        }

        return URLHelpers.CombineURL(downloadUrl, "file", URLHelpers.URLEncode(config.BucketName), encodedFileName);
    }

    internal static string? FindBucketId(string listBucketsResponse, string bucketName) =>
        (JObject.Parse(listBucketsResponse)["buckets"] as JArray)?
            .FirstOrDefault(bucket => bucket.Value<string>("bucketName") == bucketName)?
            .Value<string>("bucketId");

    /// <summary>ShareX's IsAuthorizedForUpload: the key's bucket, name prefix, and writeFiles capability.</summary>
    internal static string? CheckUploadPermission(JObject? allowed, string bucketId, string destinationPath)
    {
        string? allowedBucketId = allowed?.Value<string>("bucketId");
        if (allowedBucketId != null && bucketId != allowedBucketId)
        {
            return "No permission to upload to this bucket. Are you using the right application key?";
        }

        string? allowedPrefix = allowed?.Value<string>("namePrefix");
        if (allowedPrefix != null && !destinationPath.StartsWith(allowedPrefix, StringComparison.Ordinal))
        {
            return "Your upload path conflicts with the key's name prefix setting.";
        }

        if (allowed?["capabilities"] is JArray capabilities && !capabilities.Values<string>().Contains("writeFiles"))
        {
            return "Your key does not allow uploading to this bucket.";
        }

        return null;
    }

    internal static NameValueCollection CreateBasicAuthHeader(string keyId, string key) => new()
    {
        ["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(keyId + ":" + key))
    };

    internal static NameValueCollection CreateUploadHeaders(string authorizationToken, string destinationPath, long length, string sha1)
    {
        var contentDisposition = new ContentDisposition("inline") { FileName = URLHelpers.GetFileName(destinationPath) };
        return new NameValueCollection
        {
            ["Authorization"] = authorizationToken,
            ["X-Bz-File-Name"] = URLHelpers.URLEncode(destinationPath),
            ["X-Bz-Content-Sha1"] = sha1,
            ["X-Bz-Info-src_last_modified_millis"] = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["X-Bz-Info-b2-content-disposition"] = URLHelpers.URLEncode(contentDisposition.ToString())
        };
    }

    internal static string DescribeError(B2Response response)
    {
        if (response.Status == 0) return "Connection failed.";
        try
        {
            JObject error = JObject.Parse(response.Text);
            string? message = error.Value<string>("message");
            string separator = string.IsNullOrWhiteSpace(message) ? string.Empty : ": ";
            return $"Got status {error.Value<int?>("status")} ({error.Value<string>("code")}){separator}{message}";
        }
        catch (JsonException)
        {
            return $"Status {(int)response.Status}, unknown error.";
        }
    }

    private static string? ReadErrorCode(string text)
    {
        try { return JObject.Parse(text).Value<string>("code"); }
        catch (JsonException) { return null; }
    }

    private static string ComputeSha1(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        string hash = Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
        stream.Seek(0, SeekOrigin.Begin);
        return hash;
    }

    private B2Response SendJson(string url, string authToken, JObject body)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
        return Send(XerahS.Uploaders.HttpMethod.POST, url, new NameValueCollection { ["Authorization"] = authToken }, data, ApplicationJson);
    }

    private B2Response Send(XerahS.Uploaders.HttpMethod method, string url, NameValueCollection headers, Stream? data, string? contentType)
    {
        using HttpWebResponse? response = GetResponse(method, url, data, contentType, headers: headers, allowNon2xxResponses: true);
        if (response == null) return new B2Response(0, string.Empty);

        using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
        return new B2Response(response.StatusCode, reader.ReadToEnd());
    }

    internal readonly record struct B2Response(HttpStatusCode Status, string Text);
}
