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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;

namespace ShareX.Mega.Plugin;

/// <summary>
/// ShareX's MEGA uploader: log in with the email and password (and a two-factor code when MEGA asks for one), then
/// encrypt and upload the file in chunks, create its node in the chosen folder, and return its public link.
/// </summary>
public sealed class MegaUploader : FileUploader
{
    private const string ApiURL = "https://g.api.mega.co.nz/cs";
    private const string PublicFileURL = "https://mega.nz/file/";
    private const int InitialChunkSize = 128 * 1024;
    private const int MaximumChunkSize = 1024 * 1024;
    private const int UploadTokenLength = 36;

    private long sequenceNumber = RandomNumberGenerator.GetInt32(int.MaxValue);
    private string? sessionID;
    private byte[]? masterKey;

    public string Email { get; } = string.Empty;
    public string Password { get; } = string.Empty;
    public string? FolderID { get; set; }

    public MegaUploader(string email, string password)
    {
        Email = email?.Trim() ?? string.Empty;
        Password = password ?? string.Empty;
    }

    public MegaUploader(string? authenticatedSessionID, byte[]? accountMasterKey)
    {
        sessionID = authenticatedSessionID;
        masterKey = accountMasterKey;
    }

    public override UploadResult Upload(Stream stream, string fileName)
    {
        var result = new UploadResult();
        bool wasProgressReportingEnabled = AllowReportProgress;
        AllowReportProgress = false;

        try
        {
            if (!stream.CanSeek)
            {
                throw new NotSupportedException("MEGA uploads require a seekable stream.");
            }

            long fileSize = stream.Length - stream.Position;
            if (string.IsNullOrWhiteSpace(sessionID) || masterKey?.Length != 16)
            {
                throw new MegaRequestException(MegaProvider.LoginRequiredMessage);
            }

            string targetNodeID = string.IsNullOrWhiteSpace(FolderID) ? GetRootNodeID() : FolderID;
            string uploadURL = GetUploadURL(fileSize);

            string? completionHandle = null;
            long uploadPosition = 0;
            var progress = new ProgressManager(fileSize);

            using var cipher = new MegaFileCipher();
            foreach (int chunkSize in GetChunkSizes(fileSize))
            {
                byte[] chunk = new byte[chunkSize];
                if (chunkSize > 0)
                {
                    stream.ReadExactly(chunk);
                    cipher.EncryptChunk(chunk);
                }

                using var chunkStream = new MemoryStream(chunk, writable: false);
                RawResponse uploadResponse = Send($"{uploadURL}/{uploadPosition}", chunkStream, "application/octet-stream", null)
                    ?? throw new MegaRequestException("MEGA rejected the upload request.");

                string response = Encoding.UTF8.GetString(uploadResponse.Body).Trim();
                if (long.TryParse(response, out long uploadError) && uploadError < 0)
                {
                    throw new MegaApiException(uploadError);
                }

                if (uploadResponse.Body.Length == UploadTokenLength)
                {
                    completionHandle = ToBase64URL(uploadResponse.Body);
                }
                else if (uploadResponse.Body.Length > 0)
                {
                    throw new MegaRequestException($"MEGA returned an invalid upload token ({uploadResponse.Body.Length} bytes).");
                }

                uploadPosition += chunkSize;
                if (wasProgressReportingEnabled && fileSize > 0 && progress.UpdateProgress(chunkSize))
                {
                    OnProgressChanged(progress);
                }
            }

            if (string.IsNullOrWhiteSpace(completionHandle))
            {
                throw new MegaRequestException("MEGA did not return an upload completion handle.");
            }

            byte[] fullFileKey = cipher.CreateFullFileKey();
            byte[] encryptedAttributes = EncryptAttributes(fileName, cipher.FileKey);
            byte[] encryptedFileKey = TransformEcb(fullFileKey, masterKey, encrypt: true);

            JToken createNodeResponse = SendApiRequest(new JObject
            {
                ["a"] = "p",
                ["t"] = targetNodeID,
                ["n"] = new JArray
                {
                    new JObject
                    {
                        ["h"] = completionHandle,
                        ["t"] = 0,
                        ["a"] = ToBase64URL(encryptedAttributes),
                        ["k"] = ToBase64URL(encryptedFileKey)
                    }
                }
            });

            string? nodeID = createNodeResponse.SelectToken("f[0].h")?.Value<string>();
            if (string.IsNullOrWhiteSpace(nodeID))
            {
                throw new MegaRequestException("MEGA created the file but did not return its node handle.");
            }

            string? publicHandle = SendApiRequest(new JObject { ["a"] = "l", ["n"] = nodeID }).Value<string>();
            if (string.IsNullOrWhiteSpace(publicHandle))
            {
                throw new MegaRequestException("MEGA did not return a public file handle.");
            }

            result.Response = createNodeResponse.ToString(Formatting.None);
            result.URL = $"{PublicFileURL}{publicHandle}#{ToBase64URL(fullFileKey)}";
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

    /// <summary>ShareX's LoginAsync: returns the session ID and the Base64URL master key.</summary>
    public (string SessionID, string MasterKey) Login(string? twoFactorAuthenticationCode = null)
    {
        sessionID = null;
        masterKey = null;

        JToken preLoginResponse = SendApiRequest(new JObject { ["a"] = "us0", ["user"] = Email }, includeSession: false);
        int accountVersion = preLoginResponse.Value<int?>("v") ?? 0;
        string? salt = preLoginResponse.Value<string>("s");
        MegaAuthenticationInfo authentication = CreateAuthenticationInfo(Email, Password, accountVersion, salt);

        var request = new JObject { ["a"] = "us", ["user"] = Email, ["uh"] = authentication.UserHash };
        if (!string.IsNullOrWhiteSpace(twoFactorAuthenticationCode))
        {
            request["mfa"] = twoFactorAuthenticationCode.Trim();
        }

        JToken loginResponse = SendApiRequest(request, includeSession: false);
        masterKey = TransformEcb(FromBase64URL(loginResponse.Value<string>("k")), authentication.PasswordKey, encrypt: false);

        string? encryptedSessionID = loginResponse.Value<string>("csid");
        if (!string.IsNullOrWhiteSpace(encryptedSessionID))
        {
            byte[] encryptedPrivateKey = FromBase64URL(loginResponse.Value<string>("privk"));
            sessionID = DecryptSessionID(encryptedSessionID, encryptedPrivateKey, masterKey);
        }
        else
        {
            string? temporarySessionID = loginResponse.Value<string>("tsid");
            if (!ValidateTemporarySessionID(temporarySessionID, masterKey))
            {
                throw new MegaRequestException("MEGA returned an invalid login session.");
            }

            sessionID = temporarySessionID!;
        }

        return (sessionID, ToBase64URL(masterKey));
    }

    /// <summary>
    /// Every folder in the Cloud Drive, named by its path. ShareX shows the folders as a tree that loads each level
    /// when it is expanded; XerahS lists them all at once.
    /// </summary>
    public IReadOnlyList<MegaFolderInfo> GetAllFolders()
    {
        if (string.IsNullOrWhiteSpace(sessionID) || masterKey?.Length != 16)
        {
            throw new MegaRequestException(MegaProvider.LoginRequiredMessage);
        }

        JToken[] nodes = GetNodes()["f"]?.Children<JToken>().ToArray() ?? [];
        string? rootID = nodes.FirstOrDefault(node => node.Value<int?>("t") == 2)?.Value<string>("h");
        var folders = nodes.Where(node => node.Value<int?>("t") == 1)
            .ToDictionary(node => node.Value<string>("h") ?? string.Empty, node => (Parent: node.Value<string>("p"), Name: DecryptNodeName(node)));

        string? PathOf(string id, int depth)
        {
            if (depth > 64 || !folders.TryGetValue(id, out var folder) || folder.Name == null) return null;
            if (folder.Parent == rootID) return folder.Name;
            string? parentPath = folder.Parent == null ? null : PathOf(folder.Parent, depth + 1);
            return parentPath == null ? null : parentPath + "/" + folder.Name;
        }

        return folders.Keys
            .Select(id => new MegaFolderInfo { ID = id, Name = PathOf(id, 0) ?? string.Empty })
            .Where(folder => !string.IsNullOrEmpty(folder.ID) && !string.IsNullOrEmpty(folder.Name))
            .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private string GetRootNodeID()
    {
        string? rootNodeID = GetNodes()["f"]?.Children<JToken>().FirstOrDefault(node => node.Value<int?>("t") == 2)?.Value<string>("h");
        return string.IsNullOrWhiteSpace(rootNodeID) ? throw new MegaRequestException("MEGA did not return the Cloud Drive root folder.") : rootNodeID;
    }

    private JToken GetNodes() => SendApiRequest(new JObject { ["a"] = "f", ["c"] = 1 });

    private string? DecryptNodeName(JToken node)
    {
        string? encryptedAttributes = node.Value<string>("a");
        string? keyData = node.Value<string>("k");
        if (string.IsNullOrWhiteSpace(encryptedAttributes) || string.IsNullOrWhiteSpace(keyData))
        {
            return null;
        }

        foreach (string keyEntry in keyData.Split('/'))
        {
            int separatorIndex = keyEntry.IndexOf(':');
            string encodedKey = separatorIndex >= 0 ? keyEntry[(separatorIndex + 1)..] : keyEntry;

            try
            {
                byte[] encryptedKey = FromBase64URL(encodedKey);
                if (encryptedKey.Length != 16) continue;

                byte[] folderKey = TransformEcb(encryptedKey, masterKey!, encrypt: false);
                byte[] attributeData = FromBase64URL(encryptedAttributes);
                if (attributeData.Length == 0 || attributeData.Length % 16 != 0) continue;

                using Aes aes = CreateAes(folderKey, CipherMode.CBC);
                aes.IV = new byte[16];
                using ICryptoTransform decryptor = aes.CreateDecryptor();
                byte[] decryptedAttributes = decryptor.TransformFinalBlock(attributeData, 0, attributeData.Length);
                string json = Encoding.UTF8.GetString(decryptedAttributes).TrimEnd('\0');
                if (!json.StartsWith("MEGA", StringComparison.Ordinal)) continue;

                return JObject.Parse(json[4..]).Value<string>("n");
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
            {
            }
        }

        return null;
    }

    private string GetUploadURL(long fileSize)
    {
        string? uploadURL = SendApiRequest(new JObject { ["a"] = "u", ["ssl"] = 2, ["v"] = 3, ["s"] = fileSize }).Value<string>("p");
        return string.IsNullOrWhiteSpace(uploadURL) ? throw new MegaRequestException("MEGA did not return an upload URL.") : uploadURL.TrimEnd('/');
    }

    private JToken SendApiRequest(JObject request, bool includeSession = true)
    {
        long requestID = Interlocked.Increment(ref sequenceNumber);
        string url = $"{ApiURL}?id={requestID}";
        if (includeSession && !string.IsNullOrWhiteSpace(sessionID))
        {
            url += "&sid=" + Uri.EscapeDataString(sessionID);
        }

        byte[] requestBody = Encoding.UTF8.GetBytes(new JArray(request).ToString(Formatting.None));
        string? hashcash = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var headers = new NameValueCollection { ["Accept"] = "application/json" };
            if (!string.IsNullOrWhiteSpace(hashcash))
            {
                headers["X-Hashcash"] = hashcash;
            }

            using var requestStream = new MemoryStream(requestBody, writable: false);
            RawResponse responseInfo = Send(url, requestStream, "application/json", headers)
                ?? throw new MegaRequestException("MEGA API request failed.");

            if (responseInfo.Status == HttpStatusCode.PaymentRequired)
            {
                string? challenge = responseInfo.Headers["X-Hashcash"];
                if (attempt > 0 || string.IsNullOrWhiteSpace(challenge))
                {
                    throw new MegaRequestException("MEGA proof-of-work verification failed.");
                }

                hashcash = GenerateHashcashToken(challenge);
                continue;
            }

            if ((int)responseInfo.Status is < 200 or > 299)
            {
                throw new MegaRequestException($"MEGA API request failed with HTTP status {(int)responseInfo.Status}.");
            }

            return ParseApiResponse(Encoding.UTF8.GetString(responseInfo.Body), request.Value<string>("a") ?? "unknown");
        }

        throw new MegaRequestException("MEGA proof-of-work verification failed.");
    }

    /// <summary>ShareX's handling of an API response: the first element, its "result", and negative error codes.</summary>
    internal static JToken ParseApiResponse(string responseText, string command)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new MegaRequestException("MEGA returned an empty API response.");
        }

        JToken parsedResponse;
        try
        {
            parsedResponse = JToken.Parse(responseText);
        }
        catch (JsonException e)
        {
            string responseKind = responseText.TrimStart().StartsWith("<", StringComparison.Ordinal) ? "HTML" : $"a {responseText.Length}-character payload";
            throw new MegaRequestException($"MEGA returned {responseKind} instead of JSON for API command '{command}'.", e);
        }

        JToken responseToken = parsedResponse is JArray responseArray
            ? responseArray.Count == 0 ? throw new MegaRequestException("MEGA returned an empty API response.") : responseArray[0]
            : parsedResponse;

        if (responseToken is JObject responseObject && responseObject.TryGetValue("result", out JToken? resultToken))
        {
            responseToken = resultToken;
        }

        if (responseToken.Type == JTokenType.Integer && responseToken.Value<long>() < 0)
        {
            throw new MegaApiException(responseToken.Value<long>());
        }

        return responseToken;
    }

    private RawResponse? Send(string url, Stream data, string contentType, NameValueCollection? headers)
    {
        using HttpWebResponse? response = GetResponse(XerahS.Uploaders.HttpMethod.POST, url, data, contentType, headers: headers, allowNon2xxResponses: true);
        if (response == null) return null;

        using var body = new MemoryStream();
        response.GetResponseStream().CopyTo(body);
        return new RawResponse(response.StatusCode, response.Headers, body.ToArray());
    }

    private sealed record RawResponse(HttpStatusCode Status, WebHeaderCollection Headers, byte[] Body);

    internal static string GenerateHashcashToken(string challenge, CancellationToken cancellationToken = default)
    {
        string[]? parts = challenge?.Split(':');
        if (parts == null || parts.Length < 4 || parts[0] != "1" || !byte.TryParse(parts[1], out byte easiness))
        {
            throw new MegaRequestException("MEGA returned an invalid proof-of-work challenge.");
        }

        string tokenText = parts[3];
        byte[] token = FromBase64URL(tokenText);
        const int tokenLength = 48;
        const int prefixLength = 4;
        const int repeatCount = 262144;
        if (token.Length != tokenLength)
        {
            throw new MegaRequestException("MEGA returned an invalid proof-of-work token.");
        }

        byte[] buffer = new byte[prefixLength + repeatCount * tokenLength];
        Buffer.BlockCopy(token, 0, buffer, prefixLength, tokenLength);
        int filled = tokenLength;
        int tokenAreaLength = repeatCount * tokenLength;
        while (filled < tokenAreaLength)
        {
            int copyLength = Math.Min(filled, tokenAreaLength - filled);
            Buffer.BlockCopy(buffer, prefixLength, buffer, prefixLength + filled, copyLength);
            filled += copyLength;
        }

        uint threshold = (uint)(((easiness & 63) << 1) + 1) << ((easiness >> 6) * 7 + 3);
        Span<byte> hash = stackalloc byte[32];

        for (uint nonce = 0; ; nonce++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer[0] = (byte)(nonce >> 24);
            buffer[1] = (byte)(nonce >> 16);
            buffer[2] = (byte)(nonce >> 8);
            buffer[3] = (byte)nonce;
            SHA256.HashData(buffer, hash);

            uint firstWord = (uint)(hash[0] << 24 | hash[1] << 16 | hash[2] << 8 | hash[3]);
            if (firstWord <= threshold)
            {
                return $"1:{tokenText}:{ToBase64URL(buffer[..prefixLength])}";
            }
        }
    }

    internal static MegaAuthenticationInfo CreateAuthenticationInfo(string email, string password, int accountVersion, string? salt)
    {
        byte[] passwordBytes = PasswordToBytes(password);

        if (accountVersion == 2 && !string.IsNullOrWhiteSpace(salt))
        {
            byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, FromBase64URL(salt), 100000,
                HashAlgorithmName.SHA512, 32);
            return new MegaAuthenticationInfo
            {
                PasswordKey = derivedKey[..16],
                UserHash = ToBase64URL(derivedKey[16..])
            };
        }

        if (accountVersion == 1)
        {
            byte[] passwordKey = PrepareLegacyPasswordKey(passwordBytes);
            return new MegaAuthenticationInfo
            {
                PasswordKey = passwordKey,
                UserHash = CreateLegacyUserHash(email.ToLowerInvariant(), passwordKey)
            };
        }

        throw new NotSupportedException($"MEGA account version {accountVersion} is not supported.");
    }

    internal static byte[] PasswordToBytes(string? password)
    {
        byte[] bytes = new byte[((password?.Length ?? 0) + 3) & ~3];
        for (int index = 0; index < (password?.Length ?? 0); index++)
        {
            bytes[index] = (byte)password![index];
        }
        return bytes;
    }

    private static byte[] PrepareLegacyPasswordKey(byte[] passwordBytes)
    {
        byte[] passwordKey =
        [
            0x93, 0xC4, 0x67, 0xE3, 0x7D, 0xB0, 0xC7, 0xA4,
            0xD1, 0xBE, 0x3F, 0x81, 0x01, 0x52, 0xCB, 0x56
        ];

        List<ICryptoTransform> encryptors = new List<ICryptoTransform>();
        try
        {
            for (int offset = 0; offset < passwordBytes.Length; offset += 16)
            {
                byte[] key = new byte[16];
                Buffer.BlockCopy(passwordBytes, offset, key, 0, Math.Min(16, passwordBytes.Length - offset));
                Aes aes = CreateAes(key, CipherMode.ECB);
                encryptors.Add(new OwnedCryptoTransform(aes, aes.CreateEncryptor()));
            }

            for (int iteration = 0; iteration < 65536; iteration++)
            {
                foreach (ICryptoTransform encryptor in encryptors)
                {
                    byte[] output = new byte[16];
                    encryptor.TransformBlock(passwordKey, 0, passwordKey.Length, output, 0);
                    passwordKey = output;
                }
            }
        }
        finally
        {
            foreach (ICryptoTransform encryptor in encryptors)
            {
                encryptor.Dispose();
            }
        }

        return passwordKey;
    }

    private static string CreateLegacyUserHash(string email, byte[] passwordKey)
    {
        byte[] hash = new byte[16];
        byte[] emailBytes = Encoding.UTF8.GetBytes(email);
        for (int index = 0; index < emailBytes.Length; index++)
        {
            hash[index % hash.Length] ^= emailBytes[index];
        }

        using Aes aes = CreateAes(passwordKey, CipherMode.ECB);
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        for (int iteration = 0; iteration < 16384; iteration++)
        {
            byte[] output = new byte[16];
            encryptor.TransformBlock(hash, 0, hash.Length, output, 0);
            hash = output;
        }

        byte[] result = new byte[8];
        Buffer.BlockCopy(hash, 0, result, 0, 4);
        Buffer.BlockCopy(hash, 8, result, 4, 4);
        return ToBase64URL(result);
    }

    private static string DecryptSessionID(string encryptedSessionID, byte[] encryptedPrivateKey, byte[] accountMasterKey)
    {
        int paddedLength = (encryptedPrivateKey.Length + 15) / 16 * 16;
        Array.Resize(ref encryptedPrivateKey, paddedLength);
        byte[] privateKey = TransformEcb(encryptedPrivateKey, accountMasterKey, encrypt: false);

        int offset = 0;
        System.Numerics.BigInteger p = ReadMpi(privateKey, ref offset);
        System.Numerics.BigInteger q = ReadMpi(privateKey, ref offset);
        System.Numerics.BigInteger d = ReadMpi(privateKey, ref offset);
        _ = ReadMpi(privateKey, ref offset);

        byte[] encryptedSessionBytes = FromBase64URL(encryptedSessionID);
        int sessionOffset = 0;
        System.Numerics.BigInteger encryptedSession = ReadMpi(encryptedSessionBytes, ref sessionOffset);
        System.Numerics.BigInteger decryptedSession = System.Numerics.BigInteger.ModPow(encryptedSession, d, p * q);
        byte[] sessionBytes = decryptedSession.ToByteArray(isUnsigned: true, isBigEndian: true);

        if (sessionBytes.Length < 43)
        {
            throw new CryptographicException("MEGA returned an invalid encrypted session.");
        }

        return ToBase64URL(sessionBytes[..43]);
    }

    private static bool ValidateTemporarySessionID(string? temporarySessionID, byte[] accountMasterKey)
    {
        if (string.IsNullOrWhiteSpace(temporarySessionID))
        {
            return false;
        }

        byte[] sessionBytes = FromBase64URL(temporarySessionID);
        if (sessionBytes.Length != 32)
        {
            return false;
        }

        byte[] encryptedChallenge = sessionBytes[16..];
        byte[] challenge = TransformEcb(encryptedChallenge, accountMasterKey, encrypt: false);
        return CryptographicOperations.FixedTimeEquals(sessionBytes.AsSpan(0, 16), challenge);
    }

    private static System.Numerics.BigInteger ReadMpi(byte[] data, ref int offset)
    {
        if (offset + 2 > data.Length)
        {
            throw new CryptographicException("Invalid MEGA MPI value.");
        }

        int bitLength = data[offset] * 256 + data[offset + 1];
        int byteLength = (bitLength + 7) / 8;
        offset += 2;

        if (byteLength == 0 || offset + byteLength > data.Length)
        {
            throw new CryptographicException("Invalid MEGA MPI value.");
        }

        System.Numerics.BigInteger value = new System.Numerics.BigInteger(data.AsSpan(offset, byteLength), isUnsigned: true, isBigEndian: true);
        offset += byteLength;
        return value;
    }

    private static byte[] EncryptAttributes(string fileName, byte[] fileKey)
    {
        string json = JsonConvert.SerializeObject(new { n = fileName }, Formatting.None);
        byte[] data = Encoding.UTF8.GetBytes("MEGA" + json);
        Array.Resize(ref data, (data.Length + 15) / 16 * 16);

        using Aes aes = CreateAes(fileKey, CipherMode.CBC);
        aes.IV = new byte[16];
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] TransformEcb(byte[] data, byte[] key, bool encrypt)
    {
        if (data == null || data.Length == 0 || data.Length % 16 != 0)
        {
            throw new CryptographicException("Invalid MEGA AES block data.");
        }

        using Aes aes = CreateAes(key, CipherMode.ECB);
        using ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        return transform.TransformFinalBlock(data, 0, data.Length);
    }

    private static Aes CreateAes(byte[] key, CipherMode mode)
    {
        Aes aes = Aes.Create();
        aes.Key = key;
        aes.Mode = mode;
        aes.Padding = PaddingMode.None;
        return aes;
    }

    private static IEnumerable<int> GetChunkSizes(long fileSize)
    {
        if (fileSize == 0)
        {
            yield return 0;
            yield break;
        }

        long remaining = fileSize;
        int chunkSize = InitialChunkSize;
        while (remaining > 0)
        {
            int currentSize = (int)Math.Min(chunkSize, remaining);
            yield return currentSize;
            remaining -= currentSize;
            chunkSize = Math.Min(chunkSize + InitialChunkSize, MaximumChunkSize);
        }
    }

    internal static string ToBase64URL(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static byte[] FromBase64URL(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Invalid MEGA Base64 value.");
        }

        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    private sealed class MegaFileCipher : IDisposable
    {
        private readonly Aes aes;
        private readonly byte[] fileMac = new byte[16];
        private ulong counter;

        public byte[] FileKey { get; } = RandomNumberGenerator.GetBytes(16);
        public byte[] IV { get; } = RandomNumberGenerator.GetBytes(8);
        public byte[] MetaMac { get; private set; } = new byte[8];

        public MegaFileCipher()
        {
            aes = CreateAes(FileKey, CipherMode.ECB);
        }

        public void EncryptChunk(byte[] data)
        {
            byte[] chunkMac = new byte[16];
            Buffer.BlockCopy(IV, 0, chunkMac, 0, IV.Length);
            Buffer.BlockCopy(IV, 0, chunkMac, IV.Length, IV.Length);

            byte[] counterBlock = new byte[16];
            byte[] keyStream = new byte[16];
            byte[] plainBlock = new byte[16];

            for (int offset = 0; offset < data.Length; offset += 16)
            {
                int blockLength = Math.Min(16, data.Length - offset);
                Array.Clear(plainBlock);
                Buffer.BlockCopy(data, offset, plainBlock, 0, blockLength);

                Buffer.BlockCopy(IV, 0, counterBlock, 0, IV.Length);
                WriteUInt64BigEndian(counterBlock, 8, counter++);
                aes.EncryptEcb(counterBlock, keyStream, PaddingMode.None);

                for (int index = 0; index < 16; index++)
                {
                    chunkMac[index] ^= plainBlock[index];
                }
                byte[] encryptedChunkMac = new byte[16];
                aes.EncryptEcb(chunkMac, encryptedChunkMac, PaddingMode.None);
                chunkMac = encryptedChunkMac;

                for (int index = 0; index < blockLength; index++)
                {
                    data[offset + index] = (byte)(plainBlock[index] ^ keyStream[index]);
                }
            }

            for (int index = 0; index < fileMac.Length; index++)
            {
                fileMac[index] ^= chunkMac[index];
            }
            byte[] encryptedFileMac = new byte[16];
            aes.EncryptEcb(fileMac, encryptedFileMac, PaddingMode.None);
            Buffer.BlockCopy(encryptedFileMac, 0, fileMac, 0, fileMac.Length);

            for (int index = 0; index < 4; index++)
            {
                MetaMac[index] = (byte)(fileMac[index] ^ fileMac[index + 4]);
                MetaMac[index + 4] = (byte)(fileMac[index + 8] ^ fileMac[index + 12]);
            }
        }

        public byte[] CreateFullFileKey()
        {
            byte[] fullFileKey = new byte[32];
            for (int index = 0; index < 8; index++)
            {
                fullFileKey[index] = (byte)(FileKey[index] ^ IV[index]);
                fullFileKey[index + 16] = IV[index];
            }
            for (int index = 8; index < 16; index++)
            {
                fullFileKey[index] = (byte)(FileKey[index] ^ MetaMac[index - 8]);
                fullFileKey[index + 16] = MetaMac[index - 8];
            }
            return fullFileKey;
        }

        public void Dispose()
        {
            aes.Dispose();
        }

        private static void WriteUInt64BigEndian(byte[] buffer, int offset, ulong value)
        {
            for (int index = 7; index >= 0; index--)
            {
                buffer[offset + index] = (byte)value;
                value >>= 8;
            }
        }
    }

    private sealed class OwnedCryptoTransform : ICryptoTransform
    {
        private readonly Aes owner;
        private readonly ICryptoTransform transform;

        public int InputBlockSize => transform.InputBlockSize;
        public int OutputBlockSize => transform.OutputBlockSize;
        public bool CanTransformMultipleBlocks => transform.CanTransformMultipleBlocks;
        public bool CanReuseTransform => transform.CanReuseTransform;

        public OwnedCryptoTransform(Aes owner, ICryptoTransform transform)
        {
            this.owner = owner;
            this.transform = transform;
        }

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            return transform.TransformBlock(inputBuffer, inputOffset, inputCount, outputBuffer, outputOffset);
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            return transform.TransformFinalBlock(inputBuffer, inputOffset, inputCount);
        }

        public void Dispose()
        {
            transform.Dispose();
            owner.Dispose();
        }
    }

}

internal sealed class MegaAuthenticationInfo
{
    public byte[] PasswordKey { get; set; } = [];
    public string UserHash { get; set; } = string.Empty;
}

internal class MegaRequestException : Exception
{
    public MegaRequestException(string message) : base(message)
    {
    }

    public MegaRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

internal sealed class MegaApiException : MegaRequestException
{
    public long ErrorCode { get; }

    public MegaApiException(long errorCode) : base(GetErrorMessage(errorCode))
    {
        ErrorCode = errorCode;
    }

    private static string GetErrorMessage(long errorCode) => errorCode switch
    {
        -3 => "MEGA reported a temporary error. Please try again.",
        -4 => "MEGA is rate limiting requests. Please try again later.",
        -9 => "The requested MEGA item was not found.",
        -11 => "MEGA denied access to the requested item.",
        -14 => "MEGA rejected an encryption key.",
        -15 => "The MEGA session is invalid or expired.",
        -16 => "The MEGA account is blocked.",
        -17 => "The MEGA storage quota has been exceeded.",
        -18 => "MEGA is temporarily unavailable.",
        -26 => "MEGA requires a two-factor authentication code.",
        -27 => "The MEGA two-factor authentication code is invalid.",
        -29 => "MEGA requires additional proof-of-work verification for this request.",
        _ => $"MEGA API error: {errorCode}."
    };
}
