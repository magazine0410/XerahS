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

using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace ShareX.Filen.Plugin;

/// <summary>
/// The keys of a logged-in Filen account. Version 2 accounts encrypt metadata with their master keys (the one derived
/// from the password first); version 3 accounts use their data encryption key. Both hash names with the key derived
/// from the account's private key. Kept in the secret store so that the password does not have to be stored.
/// </summary>
internal sealed class FilenKeys
{
    [JsonProperty("authVersion")]
    public int AuthVersion { get; set; }

    [JsonProperty("masterKeys")]
    public List<string> MasterKeys { get; set; } = [];

    [JsonProperty("dek")]
    public string DataEncryptionKey { get; set; } = string.Empty;

    [JsonProperty("hmacKey")]
    public string HmacKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int FileEncryptionVersion => AuthVersion >= 3 ? 3 : 2;

    public bool IsValid() =>
        HmacKey.Length == 64 &&
        (AuthVersion >= 3 ? DataEncryptionKey.Length == 64 : AuthVersion == 2 && MasterKeys.Count > 0 && MasterKeys.All(key => key.Length > 0));

    public string Serialize() => JsonConvert.SerializeObject(this);

    public static FilenKeys? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            FilenKeys? keys = JsonConvert.DeserializeObject<FilenKeys>(json);
            return keys != null && keys.IsValid() ? keys : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string EncryptMetadata(string plaintext) => AuthVersion >= 3
        ? FilenCrypto.EncryptMetadataV3(Convert.FromHexString(DataEncryptionKey), plaintext)
        : FilenCrypto.EncryptMetadataV2(Encoding.UTF8.GetBytes(MasterKeys[0]), plaintext);

    /// <summary>Metadata in any of Filen's formats; version 2 metadata is tried with every master key.</summary>
    public string DecryptMetadata(string encrypted)
    {
        if (encrypted.StartsWith("003", StringComparison.Ordinal))
        {
            if (DataEncryptionKey.Length != 64) throw new CryptographicException("The metadata needs a data encryption key.");
            return FilenCrypto.DecryptMetadataV3(Convert.FromHexString(DataEncryptionKey), encrypted);
        }

        bool firstFormat = encrypted.StartsWith("U2FsdGVk", StringComparison.Ordinal);
        if (!firstFormat && !encrypted.StartsWith("002", StringComparison.Ordinal))
        {
            throw new CryptographicException("The metadata is in an unknown format.");
        }

        foreach (string masterKey in MasterKeys)
        {
            try
            {
                byte[] key = Encoding.UTF8.GetBytes(masterKey);
                return firstFormat ? FilenCrypto.DecryptMetadataV1(key, encrypted) : FilenCrypto.DecryptMetadataV2(key, encrypted);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
            }
        }

        throw new CryptographicException("None of the account's keys could decrypt the metadata.");
    }

    /// <summary>The name hash Filen uses to find a name in a folder: of the lowercase name.</summary>
    public string HashName(string name)
    {
        string lowercase = name.ToLowerInvariant();
        return AuthVersion >= 3 ? FilenCrypto.HmacHash(Convert.FromHexString(HmacKey), lowercase) : FilenCrypto.V2Hash(lowercase);
    }

    /// <summary>
    /// Encrypts metadata with a public folder link's key, as Filen's current SDK does: on a version 3 account, a key of 64
    /// hex characters is a data key ("003"); any other key is used as a master key ("002").
    /// </summary>
    public string EncryptMetadataWithLinkKey(string encryptedLinkKey, string plaintext)
    {
        string linkKey = DecryptMetadata(encryptedLinkKey);
        bool hexKey = linkKey.Length == 64 && linkKey.All(Uri.IsHexDigit);
        return AuthVersion >= 3 && hexKey
            ? FilenCrypto.EncryptMetadataV3(Convert.FromHexString(linkKey), plaintext)
            : FilenCrypto.EncryptMetadataV2(Encoding.UTF8.GetBytes(linkKey), plaintext);
    }
}
