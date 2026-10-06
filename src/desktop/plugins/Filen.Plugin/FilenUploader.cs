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
using System.Security.Cryptography;
using System.Text;
using Blake3;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Common;
using XerahS.Uploaders;
using UploadMethod = XerahS.Uploaders.HttpMethod;

namespace ShareX.Filen.Plugin;

/// <summary>
/// Uploads files to Filen. Filen encrypts everything on the client: the file is encrypted in 1 MiB chunks with a new
/// file key, its metadata is encrypted with the account's keys, and the result is a public link that carries the file
/// key after "#", so that Filen's page can decrypt the file in the browser. The protocol follows Filen's API guides and
/// its SDKs.
/// </summary>
public sealed class FilenUploader : FileUploader
{
    internal const string GatewayURL = "https://gateway.filen.io";
    internal const string IngestURL = "https://ingest.filen.io";

    // The link format of Filen's web and mobile apps: "d" is a file, and the fragment is the file key as hex.
    internal const string PublicFileLinkURL = "https://app.filen.io/#/d/";
    internal const int ChunkSize = 1024 * 1024;

    internal static readonly string[] LinkExpirations = ["never", "1h", "6h", "1d", "3d", "7d", "14d", "30d"];

    private readonly string? apiKey;
    private readonly FilenKeys? keys;
    private volatile bool stopRequested;

    public string Email { get; } = string.Empty;
    public string Password { get; } = string.Empty;
    public string? FolderID { get; set; }
    public string LinkExpiration { get; set; } = "never";
    public bool ShowDownloadButton { get; set; } = true;

    public FilenUploader(string email, string password)
    {
        Email = email?.Trim() ?? string.Empty;
        Password = password ?? string.Empty;
    }

    internal FilenUploader(string? apiKey, FilenKeys? keys)
    {
        this.apiKey = apiKey;
        this.keys = keys;
    }

    public override void StopUpload()
    {
        stopRequested = true;
        base.StopUpload();
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        var result = new UploadResult();
        bool wasProgressReportingEnabled = AllowReportProgress;
        AllowReportProgress = false;
        stopRequested = false;

        try
        {
            FilenKeys accountKeys = RequireLogin();
            string name = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(name)) throw new FilenRequestException("The file has no name.");

            string parentID = string.IsNullOrWhiteSpace(FolderID) ? GetBaseFolderID() : FolderID;
            var file = new FilenUploadedFile(accountKeys.FileEncryptionVersion, name, MimeTypes.GetMimeTypeFromFileName(name), parentID);
            string uploadKey = FilenCrypto.RandomString(32);

            long? totalSize = stream.CanSeek ? stream.Length - stream.Position : null;
            ProgressManager? progress = wasProgressReportingEnabled && totalSize > 0 ? new ProgressManager(totalSize.Value) : null;

            using var hasher = Hasher.New();
            byte[] buffer = new byte[ChunkSize];
            long size = 0;
            long chunks = 0;
            int read;
            while ((read = stream.ReadAtLeast(buffer, ChunkSize, throwOnEndOfStream: false)) > 0)
            {
                ThrowIfStopped();
                hasher.Update(buffer.AsSpan(0, read));
                UploadChunk(file, chunks, parentID, uploadKey, FilenCrypto.EncryptData(file.Key, buffer.AsSpan(0, read)));
                size += read;
                chunks++;
                if (progress != null && progress.UpdateProgress(read)) OnProgressChanged(progress);
                if (read < ChunkSize) break;
            }

            ThrowIfStopped();
            file.Size = size;
            file.Hash = hasher.Finalize().ToString();
            string metadata = file.CreateMetadata();
            JObject request = CreateUploadRequest(accountKeys, file, metadata);
            if (size == 0)
            {
                Request(UploadMethod.POST, "/v3/upload/empty", request);
            }
            else
            {
                request["chunks"] = chunks;
                request["rm"] = FilenCrypto.RandomString(32);
                request["uploadKey"] = uploadKey;
                Request(UploadMethod.POST, "/v3/upload/done", request);
            }

            AddToConnectedParent(accountKeys, file, metadata);

            string linkID = Guid.NewGuid().ToString();
            JToken linkResponse = Request(UploadMethod.POST, "/v3/file/link/edit", CreateLinkRequest(linkID, file.ID, LinkExpiration, ShowDownloadButton));

            result.Response = linkResponse.ToString(Formatting.None);
            result.URL = CreatePublicLinkURL(linkID, file.KeyString);
            result.IsSuccess = true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (Errors.Count == 0)
            {
                Errors.Add(e.Message);
            }
        }
        finally
        {
            AllowReportProgress = wasProgressReportingEnabled;
        }

        return result;
    }

    /// <summary>
    /// Logs in with the email and password (and a two-factor code when the account has one), and returns the API key and
    /// the account's keys. Filen accounts use login version 2 (PBKDF2) or 3 (Argon2id); the first version, which only a
    /// few very old accounts still use, is not supported.
    /// </summary>
    internal (string ApiKey, FilenKeys Keys) Login(string? twoFactorCode = null)
    {
        JToken info = Request(UploadMethod.POST, "/v3/auth/info", new JObject { ["email"] = Email }, authorized: false);
        int authVersion = info.Value<int?>("authVersion") ?? 0;
        string salt = info.Value<string>("salt") ?? string.Empty;

        string loginPassword;
        byte[]? keyEncryptionKey = null;
        string? masterKey = null;
        switch (authVersion)
        {
            case 2:
                (masterKey, loginPassword) = FilenCrypto.DeriveV2(Password, salt);
                break;
            case 3:
                (keyEncryptionKey, loginPassword) = FilenCrypto.DeriveV3(Password, salt);
                break;
            default:
                throw new FilenRequestException($"This Filen account uses login version {authVersion}, which XerahS does not support.");
        }

        JToken login = Request(UploadMethod.POST, "/v3/login", new JObject
        {
            ["email"] = Email,
            ["password"] = loginPassword,
            ["twoFactorCode"] = string.IsNullOrWhiteSpace(twoFactorCode) ? "XXXXXX" : twoFactorCode.Trim(),
            ["authVersion"] = authVersion
        }, authorized: false);

        string newApiKey = login.Value<string>("apiKey") ?? throw new FilenRequestException("Filen did not return an API key.");
        var session = new FilenUploader(newApiKey, (FilenKeys?)null);
        var accountKeys = new FilenKeys { AuthVersion = authVersion };
        if (authVersion == 2)
        {
            byte[] masterKeyBytes = Encoding.UTF8.GetBytes(masterKey!);
            JToken masterKeys = session.Request(UploadMethod.POST, "/v3/user/masterKeys",
                new JObject { ["masterKeys"] = FilenCrypto.EncryptMetadataV2(masterKeyBytes, masterKey!) });
            string encryptedKeys = masterKeys.Value<string>("keys") ?? throw new FilenRequestException("Filen did not return the master keys.");
            string decryptedKeys = encryptedKeys.StartsWith("U2FsdGVk", StringComparison.Ordinal)
                ? FilenCrypto.DecryptMetadataV1(masterKeyBytes, encryptedKeys)
                : FilenCrypto.DecryptMetadataV2(masterKeyBytes, encryptedKeys);
            accountKeys.MasterKeys = [masterKey!, .. decryptedKeys.Split('|').Where(key => key.Length > 0 && key != masterKey).Distinct()];
        }
        else
        {
            JToken dek = session.Request(UploadMethod.GET, "/v3/user/dek", null);
            string encryptedDek = dek.Value<string>("dek") ?? throw new FilenRequestException("Filen did not return the data encryption key.");
            accountKeys.DataEncryptionKey = FilenCrypto.DecryptMetadataV3(keyEncryptionKey!, encryptedDek);
        }

        JToken keyPair = session.Request(UploadMethod.GET, "/v3/user/keyPair/info", null);
        string encryptedPrivateKey = keyPair.Value<string>("privateKey") ?? throw new FilenRequestException("The Filen account has no key pair yet. Log in to one of Filen's apps once, then log in here again.");
        accountKeys.HmacKey = FilenCrypto.ToHex(FilenCrypto.CreateHmacKey(accountKeys.DecryptMetadata(encryptedPrivateKey)));

        if (!accountKeys.IsValid()) throw new FilenRequestException("Filen returned incomplete account keys.");
        return (newApiKey, accountKeys);
    }

    /// <summary>Every folder in the account, named by its path from the cloud drive's root.</summary>
    internal IReadOnlyList<FilenFolderInfo> GetAllFolders()
    {
        FilenKeys accountKeys = RequireLogin();
        string baseFolderID = GetBaseFolderID();
        JToken content = Request(UploadMethod.POST, "/v3/dir/download", new JObject { ["uuid"] = baseFolderID });

        var folders = new Dictionary<string, (string Parent, string? Name)>(StringComparer.Ordinal);
        foreach (JToken folder in content["folders"]?.Children() ?? Enumerable.Empty<JToken>())
        {
            string? id = folder.Value<string>("uuid");
            string parent = folder.Value<string>("parent") ?? string.Empty;
            // The folder that was listed is returned too, with the parent "base".
            if (string.IsNullOrEmpty(id) || id == baseFolderID || parent == "base") continue;
            folders[id] = (parent, DecryptFolderName(accountKeys, folder.Value<string>("name")));
        }

        string? PathOf(string id, int depth)
        {
            if (depth > 64 || !folders.TryGetValue(id, out var folder) || string.IsNullOrEmpty(folder.Name)) return null;
            if (folder.Parent == baseFolderID) return folder.Name;
            string? parentPath = PathOf(folder.Parent, depth + 1);
            return parentPath == null ? null : parentPath + "/" + folder.Name;
        }

        return folders.Keys
            .Select(id => new FilenFolderInfo { ID = id, Name = PathOf(id, 0) ?? string.Empty })
            .Where(folder => folder.Name.Length > 0)
            .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    internal static string CreatePublicLinkURL(string linkID, string fileKey) =>
        PublicFileLinkURL + linkID + "%23" + FilenCrypto.ToHex(Encoding.UTF8.GetBytes(fileKey));

    /// <summary>The request that enables a file's public link, with the defaults of Filen's apps: no password.</summary>
    internal static JObject CreateLinkRequest(string linkID, string fileID, string expiration, bool downloadButton) => new()
    {
        ["uuid"] = linkID,
        ["fileUUID"] = fileID,
        ["expiration"] = LinkExpirations.Contains(expiration) ? expiration : "never",
        ["password"] = "empty",
        ["passwordHashed"] = FilenCrypto.V2Hash("empty"),
        ["downloadBtn"] = downloadButton,
        ["type"] = "enable",
        ["salt"] = FilenCrypto.ToHex(RandomNumberGenerator.GetBytes(128))
    };

    /// <summary>
    /// The upload's file record. The name, size, and type are encrypted with the file key (as "003" metadata for version
    /// 3 keys, as Filen's current SDK writes them); the full metadata is encrypted with the account's keys.
    /// </summary>
    internal static JObject CreateUploadRequest(FilenKeys accountKeys, FilenUploadedFile file, string metadata)
    {
        string EncryptWithFileKey(string value) => file.Version >= 3
            ? FilenCrypto.EncryptMetadataV3(file.Key, value)
            : FilenCrypto.EncryptMetadataV2(file.Key, value);

        return new JObject
        {
            ["uuid"] = file.ID,
            ["name"] = EncryptWithFileKey(file.Name),
            ["nameHashed"] = accountKeys.HashName(file.Name),
            ["size"] = EncryptWithFileKey(file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ["parent"] = file.ParentID,
            ["mime"] = EncryptWithFileKey(file.MimeType),
            ["metadata"] = accountKeys.EncryptMetadata(metadata),
            ["version"] = file.Version
        };
    }

    /// <summary>Filen's response format: "status", "message", "code", and "data".</summary>
    internal static JToken ParseResponse(string responseText, string endpoint)
    {
        JObject response;
        try
        {
            response = JObject.Parse(responseText);
        }
        catch (JsonException e)
        {
            string responseKind = responseText.TrimStart().StartsWith("<", StringComparison.Ordinal) ? "HTML" : $"a {responseText.Length}-character response";
            throw new FilenRequestException($"Filen returned {responseKind} instead of JSON for {endpoint}.", e);
        }

        if (response.Value<bool?>("status") != true)
        {
            throw new FilenApiException(response.Value<string>("code") ?? string.Empty, response.Value<string>("message") ?? string.Empty, endpoint);
        }

        return response["data"] ?? JValue.CreateNull();
    }

    private void UploadChunk(FilenUploadedFile file, long index, string parentID, string uploadKey, byte[] encrypted)
    {
        string hash = FilenCrypto.ToHex(SHA512.HashData(encrypted));
        string url = $"{IngestURL}/v3/upload?uuid={file.ID}&index={index}&parent={Uri.EscapeDataString(parentID)}&uploadKey={uploadKey}&hash={hash}";
        using var data = new MemoryStream(encrypted, writable: false);
        Send(UploadMethod.POST, url, "/v3/upload", data, null, authorized: true);
    }

    /// <summary>
    /// When the folder is shared with other Filen users or is in a public folder link, Filen's apps add a new file to
    /// those shares and links, so that their recipients see it. As in Filen's current SDK, looking up the shares and links
    /// must succeed, while a share or link that cannot be updated is only logged.
    /// </summary>
    private void AddToConnectedParent(FilenKeys accountKeys, FilenUploadedFile file, string metadata)
    {
        JToken linked = Request(UploadMethod.POST, "/v3/item/linked", new JObject { ["uuid"] = file.ParentID });
        JToken shared = Request(UploadMethod.POST, "/v3/item/shared", new JObject { ["uuid"] = file.ParentID });

        foreach (JToken link in linked["links"]?.Children() ?? Enumerable.Empty<JToken>())
        {
            TryUpdateConnection("the public folder link", () =>
            {
                string encryptedLinkKey = link.Value<string>("linkKey") ?? string.Empty;
                Request(UploadMethod.POST, "/v3/dir/link/add", new JObject
                {
                    ["uuid"] = file.ID,
                    ["parent"] = file.ParentID,
                    ["linkUUID"] = link.Value<string>("linkUUID"),
                    ["type"] = "file",
                    ["metadata"] = accountKeys.EncryptMetadataWithLinkKey(encryptedLinkKey, metadata),
                    ["key"] = encryptedLinkKey,
                    ["expiration"] = "never"
                });
            });
        }

        foreach (JToken user in shared["users"]?.Children() ?? Enumerable.Empty<JToken>())
        {
            TryUpdateConnection("the share with " + user.Value<string>("email"), () =>
            {
                using RSA publicKey = RSA.Create();
                publicKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(user.Value<string>("publicKey") ?? string.Empty), out _);
                byte[] encrypted = publicKey.Encrypt(Encoding.UTF8.GetBytes(metadata), RSAEncryptionPadding.OaepSHA512);
                Request(UploadMethod.POST, "/v3/item/share", new JObject
                {
                    ["uuid"] = file.ID,
                    ["parent"] = file.ParentID,
                    ["email"] = user.Value<string>("email"),
                    ["type"] = "file",
                    ["metadata"] = Convert.ToBase64String(encrypted)
                });
            });
        }
    }

    private void TryUpdateConnection(string connection, Action update)
    {
        int errorCount = Errors.Count;
        try
        {
            update();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A failed request also adds its error to the list; the upload itself still succeeded.
            Errors.Errors.RemoveRange(errorCount, Errors.Count - errorCount);
            DebugHelper.WriteException(e, $"Filen: the uploaded file could not be added to {connection}");
        }
    }

    private static string? DecryptFolderName(FilenKeys accountKeys, string? encryptedMetadata)
    {
        if (string.IsNullOrEmpty(encryptedMetadata)) return null;
        try
        {
            return JObject.Parse(accountKeys.DecryptMetadata(encryptedMetadata)).Value<string>("name");
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private string GetBaseFolderID()
    {
        string? id = Request(UploadMethod.GET, "/v3/user/baseFolder", null).Value<string>("uuid");
        return string.IsNullOrWhiteSpace(id) ? throw new FilenRequestException("Filen did not return the cloud drive's root folder.") : id;
    }

    private FilenKeys RequireLogin() =>
        !string.IsNullOrWhiteSpace(apiKey) && keys != null && keys.IsValid() ? keys : throw new FilenRequestException(FilenProvider.LoginRequiredMessage);

    private void ThrowIfStopped()
    {
        if (stopRequested) throw new OperationCanceledException();
    }

    private JToken Request(UploadMethod method, string endpoint, JObject? body, bool authorized = true)
    {
        using MemoryStream? data = body == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)), writable: false);
        return Send(method, GatewayURL + endpoint, endpoint, data, body == null ? null : "application/json", authorized);
    }

    private JToken Send(UploadMethod method, string url, string endpoint, Stream? data, string? contentType, bool authorized)
    {
        var headers = new NameValueCollection { ["Accept"] = "application/json" };
        if (authorized) headers["Authorization"] = "Bearer " + apiKey;

        using HttpWebResponse? response = GetResponse(method, url, data, contentType, headers: headers, allowNon2xxResponses: true);
        ThrowIfStopped();
        if (response == null) throw new FilenRequestException($"The Filen request {endpoint} failed.");

        using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
        string text = reader.ReadToEnd();
        if ((int)response.StatusCode is < 200 or > 299 && string.IsNullOrWhiteSpace(text))
        {
            throw new FilenRequestException($"The Filen request {endpoint} failed with HTTP status {(int)response.StatusCode}.");
        }

        return ParseResponse(text, endpoint);
    }
}

/// <summary>A file being uploaded: its ID, key, and the metadata Filen stores for it.</summary>
internal sealed class FilenUploadedFile
{
    public FilenUploadedFile(int version, string name, string mimeType, string parentID)
    {
        Version = version;
        Name = name;
        MimeType = mimeType;
        ParentID = parentID;
        // Version 2 file keys are 32 letters and digits, used as the key bytes; version 3 keys are 32 random bytes, written as hex.
        if (version >= 3)
        {
            Key = RandomNumberGenerator.GetBytes(32);
            KeyString = FilenCrypto.ToHex(Key);
        }
        else
        {
            KeyString = FilenCrypto.RandomString(32);
            Key = Encoding.ASCII.GetBytes(KeyString);
        }
    }

    public string ID { get; } = Guid.NewGuid().ToString();
    public int Version { get; }
    public string Name { get; }
    public string MimeType { get; }
    public string ParentID { get; }
    public byte[] Key { get; }
    public string KeyString { get; }
    public long Size { get; set; }
    public string Hash { get; set; } = string.Empty;
    public long Timestamp { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public string CreateMetadata() => new JObject
    {
        ["name"] = Name,
        ["size"] = Size,
        ["mime"] = MimeType,
        ["key"] = KeyString,
        ["lastModified"] = Timestamp,
        ["creation"] = Timestamp,
        ["blake3"] = Hash
    }.ToString(Formatting.None);
}

public sealed class FilenFolderInfo
{
    public string ID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}

internal class FilenRequestException : Exception
{
    public FilenRequestException(string message) : base(message)
    {
    }

    public FilenRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

internal sealed class FilenApiException : FilenRequestException
{
    public FilenApiException(string code, string message, string endpoint)
        : base(string.IsNullOrWhiteSpace(message) ? $"Filen rejected {endpoint} ({code})." : $"Filen: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}
