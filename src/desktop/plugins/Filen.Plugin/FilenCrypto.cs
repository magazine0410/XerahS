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
using Konscious.Security.Cryptography;

namespace ShareX.Filen.Plugin;

/// <summary>
/// Filen's client-side cryptography, as described in Filen's API guides and implemented by its MIT-licensed Go SDK
/// (filen-sdk-go): the login key derivation, metadata and file data encryption, and the name hashes.
/// </summary>
internal static class FilenCrypto
{
    private const string AlphanumericCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>
    /// Login version 2: PBKDF2-SHA512 (200,000 iterations, 64 bytes) of the password with the account's salt. The first
    /// half of the hex string is the master key; the SHA-512 of the second half is the password sent to Filen.
    /// </summary>
    public static (string MasterKey, string LoginPassword) DeriveV2(string password, string salt)
    {
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), 200000, HashAlgorithmName.SHA512, 64);
        string hex = ToHex(derived);
        return (hex[..64], ToHex(SHA512.HashData(Encoding.ASCII.GetBytes(hex[64..]))));
    }

    /// <summary>
    /// Login version 3: Argon2id (3 passes, 64 MiB, 4 lanes, 64 bytes) of the password with the account's hex salt. The
    /// first half is the key that decrypts the data encryption key; the second half, as hex, is the password sent to Filen.
    /// </summary>
    public static (byte[] KeyEncryptionKey, string LoginPassword) DeriveV3(string password, string salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = Convert.FromHexString(salt),
            Iterations = 3,
            MemorySize = 65536,
            DegreeOfParallelism = 4
        };
        byte[] derived = argon2.GetBytes(64);
        return (derived[..32], ToHex(derived[32..]));
    }

    /// <summary>The "002" metadata format: AES-256-GCM with a key derived from the master key or file key.</summary>
    public static string EncryptMetadataV2(byte[] key, string plaintext)
    {
        byte[] nonce = Encoding.ASCII.GetBytes(RandomString(NonceSize));
        return "002" + Encoding.ASCII.GetString(nonce) + Convert.ToBase64String(Seal(DeriveMetadataKey(key), nonce, Encoding.UTF8.GetBytes(plaintext)));
    }

    public static string DecryptMetadataV2(byte[] key, string encrypted)
    {
        if (encrypted.Length < 15 || !encrypted.StartsWith("002", StringComparison.Ordinal))
        {
            throw new CryptographicException("The metadata is not in Filen's 002 format.");
        }

        byte[] nonce = Encoding.ASCII.GetBytes(encrypted[3..15]);
        return Encoding.UTF8.GetString(Open(DeriveMetadataKey(key), nonce, Convert.FromBase64String(encrypted[15..])));
    }

    /// <summary>The "003" metadata format of version 3 accounts: AES-256-GCM with the data encryption key.</summary>
    public static string EncryptMetadataV3(byte[] key, string plaintext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        return "003" + ToHex(nonce) + Convert.ToBase64String(Seal(key, nonce, Encoding.UTF8.GetBytes(plaintext)));
    }

    public static string DecryptMetadataV3(byte[] key, string encrypted)
    {
        if (encrypted.Length < 27 || !encrypted.StartsWith("003", StringComparison.Ordinal))
        {
            throw new CryptographicException("The metadata is not in Filen's 003 format.");
        }

        byte[] nonce = Convert.FromHexString(encrypted[3..27]);
        return Encoding.UTF8.GetString(Open(key, nonce, Convert.FromBase64String(encrypted[27..])));
    }

    /// <summary>
    /// The first metadata format ("U2FsdGVk", base64 of "Salted__"): AES-256-CBC with OpenSSL's MD5 key derivation. Very
    /// old accounts can still have folder names and keys in this format.
    /// </summary>
    public static string DecryptMetadataV1(byte[] key, string encrypted)
    {
        byte[] data = Convert.FromBase64String(encrypted);
        if (data.Length < 32 || Encoding.ASCII.GetString(data, 0, 8) != "Salted__")
        {
            throw new CryptographicException("The metadata is not in Filen's first format.");
        }

        byte[] salt = data[8..16];
        byte[] keyAndIV = new byte[48];
        byte[] previous = [];
        for (int offset = 0; offset < keyAndIV.Length;)
        {
            previous = MD5.HashData([.. previous, .. key, .. salt]);
            int count = Math.Min(previous.Length, keyAndIV.Length - offset);
            Array.Copy(previous, 0, keyAndIV, offset, count);
            offset += count;
        }

        using Aes aes = Aes.Create();
        aes.Key = keyAndIV[..32];
        return Encoding.UTF8.GetString(aes.DecryptCbc(data[16..], keyAndIV[32..], PaddingMode.PKCS7));
    }

    /// <summary>A chunk of file data: the nonce, then the AES-256-GCM ciphertext and its tag.</summary>
    public static byte[] EncryptData(byte[] key, ReadOnlySpan<byte> data)
    {
        byte[] result = new byte[NonceSize + data.Length + TagSize];
        Span<byte> nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, result.AsSpan(NonceSize, data.Length), result.AsSpan(NonceSize + data.Length));
        return result;
    }

    public static byte[] DecryptData(byte[] key, ReadOnlySpan<byte> data)
    {
        byte[] plaintext = new byte[data.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(data[..NonceSize], data[NonceSize..^TagSize], data[^TagSize..], plaintext);
        return plaintext;
    }

    /// <summary>Filen's older name hash: the SHA-1 of the hex SHA-512.</summary>
    public static string V2Hash(string value) =>
        ToHex(SHA1.HashData(Encoding.ASCII.GetBytes(ToHex(SHA512.HashData(Encoding.UTF8.GetBytes(value))))));

    /// <summary>The account's hashing key: HKDF-SHA256 of the RSA private exponent, with the info "hmac-sha256-key".</summary>
    public static byte[] CreateHmacKey(string privateKeyBase64)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
        byte[] exponent = rsa.ExportParameters(true).D ?? throw new CryptographicException("The private key has no exponent.");
        int start = 0;
        while (start < exponent.Length - 1 && exponent[start] == 0) start++;
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, exponent[start..], 32, [], Encoding.ASCII.GetBytes("hmac-sha256-key"));
    }

    public static string HmacHash(byte[] hmacKey, string value) => ToHex(HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(value)));

    /// <summary>Filen's random strings (file keys, upload keys, nonces): letters and digits.</summary>
    public static string RandomString(int length) => RandomNumberGenerator.GetString(AlphanumericCharacters, length);

    public static string ToHex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(data);

    private static byte[] DeriveMetadataKey(byte[] key) => Rfc2898DeriveBytes.Pbkdf2(key, key, 1, HashAlgorithmName.SHA512, 32);

    private static byte[] Seal(byte[] key, byte[] nonce, byte[] plaintext)
    {
        byte[] result = new byte[plaintext.Length + TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, result.AsSpan(0, plaintext.Length), result.AsSpan(plaintext.Length));
        return result;
    }

    private static byte[] Open(byte[] key, byte[] nonce, byte[] data)
    {
        if (data.Length < TagSize) throw new CryptographicException("The encrypted metadata is too short.");
        byte[] plaintext = new byte[data.Length - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, data.AsSpan(0, plaintext.Length), data.AsSpan(plaintext.Length), plaintext);
        return plaintext;
    }
}
