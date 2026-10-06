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

using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ShareX.Filen.Plugin;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Uploaders;

// The Filen plugin's cryptography, checked against values from Filen's own TypeScript SDK (@filen/sdk 0.4.2), and, for
// the hashing key, which Filen's Go and Rust SDKs derive from the private exponent, against Python's cryptography library.
[TestFixture]
public class FilenPluginTests
{
    private const string Password = "correct horse";
    private const string V2Salt = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";
    private const string V3Salt = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
    private const string MasterKey = "76ee9f9999fa2562a81a4d6a0636ed699396c09b304e468c0561a27ee05efc65";
    private const string DataKeyHex = "4f6e65206b6579206f6620363420686578206368617261637465727321212121";
    private const string FileKeyV2 = "abcdefghijklmnopqrstuvwxyz012345";
    private const string ChunkText = "Filen chunk data for the XerahS test";

    // A 2048-bit test key, generated for these tests only.
    private const string PrivateKey = "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDHuDEHmcUSe2B+qJNqqzm+kVb0Z5DpTVdN/OUHrQdpVQqF96oYbM+qdisQHWFerkdeZarUknSlDbktRqCiqfn8vrHV/Yw/LyhxHAf4VbkSeTTaMF4M+ef9rnPLOGWH8dlbwyQ9HyQO35RIEvHTxNH4rNPeIPaMWSRq4g51ICk63cQxsp3M7qM9ZUziK6qQYYxdB82Rd6WWGqrx+XDHlhGJYItr/bMtyixcgNBCwbIBfZHx5w2unGF0owOFUjO+QiQiwyrWnRpshMQ4zeS84+kessOgBKF3lB89Z7umsMlDYjbafBCDeCL/y+cSP2OHnM+m3y/pGBT1SC4lH6tjr7VzAgMBAAECggEAMjsWQcYf9bUIIPL/GK31+QMO7qt5m+Py2E+JvvN60LgtzGHs6vW7CTFtRUFOcwS8OHazj4FCSxz3fPatghvkHvhLO/noIyAWONSHAKN2x7mqXFIb5YGZIomU4ADQMG/YbibXX3QMmp0K61GwdsWi1buKEe0SdVwaN7OhVT4AgKhCI3MR3Vcxgr0gthCJ9tYoMUiu9wXrbdj2i4cwL9PT3Nxu5sixamKWcxMOFBcTFdHrYwl9939NUycziLAxEAD9GGqNDMj53MwvUbesJwK7dBY08EBeuQ5MzZ4yXUhAA17LxHu07g3K4tjTNGvFiILt7U9a1G71WfLjdTKed+RZKQKBgQDyVqjaV3XE1YJzGsCDtgm6aBoPg6ILv+AJ9sVLWdBZNGue5Xq2Uz9GDInqZoyxVGayLmEd/VGBwsVnl6cWAcEsRmyvgPiRqIpJyr7ZnVgRtvcAbupa4fZK588FzIUTyJ9dmwqEmvm+xu1QdVPdJeKVVOn1VNbydvchEpBODEFb9QKBgQDS+nhPM6puVYS1mpt3/1Iiol3zfFsZhaW1xRTu2iIKwrH9BCJJo3QaPP456ZupLN00JStfw/jnNG2KiQPpYLbARLZxCDwrnPqdv9CcOF3omduV3WKK5j0GpBtD+Gp3OJX6G39BDT6nDuygsFmQWS2aGNhyiokEKZsg25wPCoISxwKBgQDE89dRdIlbSqc1g0V5BiaL3/VmLHYzlGKx1mPsZHcnOKpKvnhn2+pttN0VqvinWlAIehcl924ZxsEG+0KpaQ2lPSsxy+C5CMAzTtqjI/SimQ/Pw1UmPcUchZ9EOD7joEadHdeDhCx7d1MB9AkPj63B7fwjDiuPbU5WC69KLz2jTQKBgFZp5rsyAz2b43ZqtAELX7312RtwtPOYLQ5tgaxvRgA4TKTAe7bDrztL/ikESRCa4qOIEeLwptM0QyW6BxMg3NmVi1Byugp3CqNwPGawI66wziLtAX643QgKUWyepCTe1XcSqiWf4iiU0VDjylt4W1hBku4fVg2RaQulR59Vg39jAoGAATYRAfmHbSM06cWKy7xkik1dsrrm7unoslkra/S8wS7g9oqJAmw7p3u31t8U8uw1tdnHp/QgcJmKrXb+o7NSBH7z4DYH3xAYyuAjBqyvrrMOhzRc6gIHKQnbo6ymAknwtYSCdnUphkaJQxQvCdN6L2c1O8YgTRHZOOiL4GpLiQI=";
    private const string HmacKey = "b2d467aa23c7fb42b6a9183b482ea28b3189e6b1a24579c6ab8ed29af6ae6363";

    // ── Login key derivation ────────────────────────────────────────────────

    [Test]
    public void V2Login_DerivesTheMasterKeyAndPassword_LikeFilensSdk()
    {
        (string masterKey, string loginPassword) = FilenCrypto.DeriveV2(Password, V2Salt);
        Assert.That(masterKey, Is.EqualTo(MasterKey));
        Assert.That(loginPassword, Is.EqualTo("4e9c51ef89b43a4102220ac8b11b3b26648ea99c69e7a7953a6205681dac54af1c44d5aeb0c63b645195a8dcbe59fc6dca8c2e6452892063d4d78b213ba51905"));
    }

    [Test]
    public void V3Login_DerivesTheKeyEncryptionKeyAndPassword_LikeFilensSdk()
    {
        (byte[] keyEncryptionKey, string loginPassword) = FilenCrypto.DeriveV3(Password, V3Salt);
        Assert.That(FilenCrypto.ToHex(keyEncryptionKey), Is.EqualTo("d57c7934a4a04aadb70f7935a9509e2d40fa2fba9988f88d718c79e98761744a"));
        Assert.That(loginPassword, Is.EqualTo("88ef216d3ef119d024d5c9d98f0537f12f91ea0be9baa29d7b22cd4ab1cc6d74"));
    }

    // ── Metadata ────────────────────────────────────────────────────────────

    [Test]
    public void Metadata_DecryptsEachOfFilensFormats()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FilenCrypto.DecryptMetadataV2(Encoding.UTF8.GetBytes(MasterKey), "002sU7C6Pfl10O9R8V5YZpdSgf0wVWGZIWOYpsA3+soLmmW/+R2EtMpGq8riPqoEGI="),
                Is.EqualTo("""{"name":"Screenshots"}"""));
            Assert.That(FilenCrypto.DecryptMetadataV3(Convert.FromHexString(DataKeyHex), "0037bd658f9dcbf3a0153b3ec20k+84KGMnBoW3mt5XAU2zb1A5oUb64MDuFz8hxsFStTdRZw=="),
                Is.EqualTo("ünïcode name.png"));
            Assert.That(FilenCrypto.DecryptMetadataV1(Encoding.UTF8.GetBytes(MasterKey), "U2FsdGVkX18UAXRzyN2Yl1OCmUMzDH37gxRfC8NcLiV5nitETOx5tL4cgrUlDV4L"),
                Is.EqualTo("""{"name":"Old folder"}"""));
        });
    }

    [Test]
    public void Metadata_RoundTripsInTheFormatsXerahSWrites()
    {
        byte[] masterKey = Encoding.UTF8.GetBytes(MasterKey);
        byte[] dataKey = Convert.FromHexString(DataKeyHex);
        string v2 = FilenCrypto.EncryptMetadataV2(masterKey, "metadata");
        string v3 = FilenCrypto.EncryptMetadataV3(dataKey, "metadata");
        Assert.That(v2, Does.StartWith("002"));
        Assert.That(v3, Does.StartWith("003"));
        Assert.That(FilenCrypto.DecryptMetadataV2(masterKey, v2), Is.EqualTo("metadata"));
        Assert.That(FilenCrypto.DecryptMetadataV3(dataKey, v3), Is.EqualTo("metadata"));
    }

    [Test]
    public void FileKey_DecryptsTheNameFilensSdkEncryptedWithIt()
    {
        // Version 2 file keys are used as text; version 3 file keys are 32 bytes written as hex.
        Assert.That(FilenCrypto.DecryptMetadataV2(Encoding.ASCII.GetBytes(FileKeyV2), "002ZfV1wVuc-V_SiLJeoPngNzO/HRpxFFTYY1GYlqgYaxbWrGDt"), Is.EqualTo("capture.png"));
        Assert.That(FilenCrypto.DecryptMetadataV3(Convert.FromHexString(DataKeyHex), "003599bf35f65fe09c81bddbcd1RF+ydb3IlSQR5G1jhA/cNzyFRWCXDQaPa3oT"), Is.EqualTo("capture.png"));
    }

    // ── File data ───────────────────────────────────────────────────────────

    [Test]
    public void Data_DecryptsChunksFilensSdkEncrypted()
    {
        Assert.That(Encoding.UTF8.GetString(FilenCrypto.DecryptData(Encoding.ASCII.GetBytes(FileKeyV2),
            Convert.FromBase64String("V1N5WmFvQm90OFp20OafiJu1BWpc6n2klLru9CydBzby9c8hvnHMQt1HG2DAJ3pGgQiTGfsiEiJg/gO+PdoWHQ=="))), Is.EqualTo(ChunkText));
        Assert.That(Encoding.UTF8.GetString(FilenCrypto.DecryptData(Convert.FromHexString(DataKeyHex),
            Convert.FromBase64String("t2PWgng4kM1a990s7H71tMWLoEGhocKJ0poYp5k92lT8k6Oevk4OC2fEopj+eQaAs+D0XEUWALogImqZNbiwMQ=="))), Is.EqualTo(ChunkText));
    }

    [Test]
    public void Data_IsTheNonceTheCiphertextAndTheTag()
    {
        byte[] key = Convert.FromHexString(DataKeyHex);
        byte[] plaintext = Encoding.UTF8.GetBytes(ChunkText);
        byte[] encrypted = FilenCrypto.EncryptData(key, plaintext);
        Assert.That(encrypted.Length, Is.EqualTo(12 + plaintext.Length + 16));
        Assert.That(FilenCrypto.DecryptData(key, encrypted), Is.EqualTo(plaintext));
    }

    [Test]
    public void FileHash_IsTheLowercaseBlake3OfTheContent()
    {
        using var hasher = Blake3.Hasher.New();
        Assert.That(hasher.Finalize().ToString(), Is.EqualTo("af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262"));
        using var content = Blake3.Hasher.New();
        content.Update(Encoding.UTF8.GetBytes(ChunkText));
        Assert.That(content.Finalize().ToString(), Is.EqualTo("8210022b6e40af29c14abb4384b9eb12165c4e6f1503aba946b412342568ef0b"));
    }

    // ── Hashes and keys ─────────────────────────────────────────────────────

    [Test]
    public void V2Hash_MatchesFilensSdk()
    {
        Assert.That(FilenCrypto.V2Hash("empty"), Is.EqualTo("8f83dfba6522ce8c34c5afefa64878e3a4ac554d"));
        Assert.That(FilenCrypto.V2Hash("capture 2026-10-06.png"), Is.EqualTo("28e0239f2eae3a68f8c44eccecb87ed9fd71cc5a"));
    }

    [Test]
    public void HmacKey_IsDerivedFromThePrivateExponent_AndHashesV3Names()
    {
        Assert.That(FilenCrypto.ToHex(FilenCrypto.CreateHmacKey(PrivateKey)), Is.EqualTo(HmacKey));

        var v3 = new FilenKeys { AuthVersion = 3, DataEncryptionKey = DataKeyHex, HmacKey = HmacKey };
        var v2 = new FilenKeys { AuthVersion = 2, MasterKeys = [MasterKey], HmacKey = HmacKey };
        // Names are hashed in lowercase.
        Assert.That(v3.HashName("Capture 2026-10-06.PNG"), Is.EqualTo("355f9f7f6bf4651e2fe3afe5bc6eb43eb0d2654c51487e60621434cd08126650"));
        Assert.That(v2.HashName("Capture 2026-10-06.PNG"), Is.EqualTo("28e0239f2eae3a68f8c44eccecb87ed9fd71cc5a"));
    }

    [Test]
    public void AccountKeys_DecryptWithAnyMasterKey_AndSurviveTheSecretStore()
    {
        string encrypted = FilenCrypto.EncryptMetadataV2(Encoding.UTF8.GetBytes(MasterKey), "folder");
        var keys = new FilenKeys { AuthVersion = 2, MasterKeys = ["0000000000000000000000000000000000000000000000000000000000000000", MasterKey], HmacKey = HmacKey };
        Assert.That(keys.DecryptMetadata(encrypted), Is.EqualTo("folder"));
        Assert.That(keys.EncryptMetadata("x"), Does.StartWith("002"));

        FilenKeys? restored = FilenKeys.Deserialize(keys.Serialize());
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.MasterKeys, Is.EqualTo(keys.MasterKeys));
        Assert.That(restored.FileEncryptionVersion, Is.EqualTo(2));

        var v3 = new FilenKeys { AuthVersion = 3, DataEncryptionKey = DataKeyHex, HmacKey = HmacKey };
        Assert.That(v3.EncryptMetadata("x"), Does.StartWith("003"));
        Assert.That(v3.FileEncryptionVersion, Is.EqualTo(3));
        Assert.That(FilenKeys.Deserialize("""{"authVersion":3,"dek":"","hmacKey":""}"""), Is.Null);
        Assert.That(FilenKeys.Deserialize("not json"), Is.Null);
    }

    [Test]
    public void FolderLinkKey_UsesTheDataKeyFormatOnlyForHexKeysOfV3Accounts()
    {
        var v3 = new FilenKeys { AuthVersion = 3, DataEncryptionKey = DataKeyHex, HmacKey = HmacKey };
        string hexLinkKey = new string('a', 64);
        string encryptedHexKey = v3.EncryptMetadata(hexLinkKey);
        string encrypted = v3.EncryptMetadataWithLinkKey(encryptedHexKey, "metadata");
        Assert.That(FilenCrypto.DecryptMetadataV3(Convert.FromHexString(hexLinkKey), encrypted), Is.EqualTo("metadata"));

        string textLinkKey = "abcdefghijklmnopqrstuvwxyz012345";
        string legacy = v3.EncryptMetadataWithLinkKey(v3.EncryptMetadata(textLinkKey), "metadata");
        Assert.That(FilenCrypto.DecryptMetadataV2(Encoding.UTF8.GetBytes(textLinkKey), legacy), Is.EqualTo("metadata"));
    }

    // ── Requests ────────────────────────────────────────────────────────────

    [Test]
    public void UploadRequest_EncryptsTheNameWithTheFileKey_AndTheMetadataWithTheAccountKeys()
    {
        foreach (FilenKeys keys in new[]
        {
            new FilenKeys { AuthVersion = 2, MasterKeys = [MasterKey], HmacKey = HmacKey },
            new FilenKeys { AuthVersion = 3, DataEncryptionKey = DataKeyHex, HmacKey = HmacKey }
        })
        {
            var file = new FilenUploadedFile(keys.FileEncryptionVersion, "Capture.png", "image/png", "parent-id") { Size = 5, Hash = "hash" };
            JObject request = FilenUploader.CreateUploadRequest(keys, file, file.CreateMetadata());
            string name = request.Value<string>("name")!;
            string decryptedName = file.Version >= 3 ? FilenCrypto.DecryptMetadataV3(file.Key, name) : FilenCrypto.DecryptMetadataV2(file.Key, name);
            JObject metadata = JObject.Parse(keys.DecryptMetadata(request.Value<string>("metadata")!));

            Assert.Multiple(() =>
            {
                Assert.That(decryptedName, Is.EqualTo("Capture.png"));
                Assert.That(name, Does.StartWith(file.Version >= 3 ? "003" : "002"));
                Assert.That(request.Value<string>("nameHashed"), Is.EqualTo(keys.HashName("Capture.png")));
                Assert.That(request.Value<int>("version"), Is.EqualTo(keys.FileEncryptionVersion));
                Assert.That(request.Value<string>("parent"), Is.EqualTo("parent-id"));
                Assert.That(metadata.Value<string>("name"), Is.EqualTo("Capture.png"));
                Assert.That(metadata.Value<long>("size"), Is.EqualTo(5));
                Assert.That(metadata.Value<string>("mime"), Is.EqualTo("image/png"));
                Assert.That(metadata.Value<string>("key"), Is.EqualTo(file.KeyString));
                Assert.That(metadata.Value<string>("blake3"), Is.EqualTo("hash"));
                Assert.That(file.KeyString.Length, Is.EqualTo(file.Version >= 3 ? 64 : 32));
            });
        }
    }

    [Test]
    public void PublicLink_UsesTheFormatOfFilensApps()
    {
        const string linkID = "11111111-2222-4333-8444-555555555555";
        Assert.That(FilenUploader.CreatePublicLinkURL(linkID, "0123456789abcdef0123456789abcdef"),
            Is.EqualTo("https://app.filen.io/#/d/" + linkID + "%23" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"))));

        JObject request = FilenUploader.CreateLinkRequest(linkID, "file-id", "7d", downloadButton: true);
        Assert.Multiple(() =>
        {
            Assert.That(request.Value<string>("uuid"), Is.EqualTo(linkID));
            Assert.That(request.Value<string>("fileUUID"), Is.EqualTo("file-id"));
            Assert.That(request.Value<string>("expiration"), Is.EqualTo("7d"));
            Assert.That(request.Value<string>("password"), Is.EqualTo("empty"));
            Assert.That(request.Value<string>("passwordHashed"), Is.EqualTo("8f83dfba6522ce8c34c5afefa64878e3a4ac554d"));
            Assert.That(request.Value<bool>("downloadBtn"), Is.True);
            Assert.That(request.Value<string>("type"), Is.EqualTo("enable"));
            Assert.That(request.Value<string>("salt")!.Length, Is.EqualTo(256));
        });
        Assert.That(FilenUploader.CreateLinkRequest(linkID, "file-id", "forever", false).Value<string>("expiration"), Is.EqualTo("never"));
    }

    [Test]
    public void ApiResponse_ReturnsTheDataAndReportsErrorCodes()
    {
        Assert.That(FilenUploader.ParseResponse("""{"status":true,"message":"","code":"ok","data":{"uuid":"root"}}""", "/v3/user/baseFolder").Value<string>("uuid"), Is.EqualTo("root"));
        var error = Assert.Throws<FilenApiException>(() => FilenUploader.ParseResponse("""{"status":false,"message":"Please enter your 2FA code.","code":"enter_2fa"}""", "/v3/login"));
        Assert.That(error!.Code, Is.EqualTo("enter_2fa"));
        Assert.That(error.Message, Does.Contain("2FA"));
        Assert.Throws<FilenRequestException>(() => FilenUploader.ParseResponse("<html></html>", "/v3/login"));
    }

    // ── Provider ────────────────────────────────────────────────────────────

    [Test]
    public void Provider_RequiresALogin()
    {
        var provider = new FilenProvider();
        Assert.That(provider.ValidateSettings("""{"SecretKey":"k","Email":"user@example.com"}"""), Is.False);
        FilenConfigModel config = FilenProvider.DeserializeConfig(null);
        Assert.That(config.LinkExpiration, Is.EqualTo("never"));
        Assert.That(config.ShowDownloadButton, Is.True);
    }

    [Test]
    public async Task Upload_WithoutALogin_ReportsThatTheLoginIsRequired()
    {
        using var content = new MemoryStream([1, 2, 3]);
        var outcome = await UploaderUploadAdapter.UploadAsync(new FilenProvider().CreateInstance("{}"),
            new UploadRequest { Content = content, FileName = "capture.png", Category = UploaderCategory.Image }, CancellationToken.None);
        Assert.That(outcome.Succeeded, Is.False);
        Assert.That(outcome.Error, Is.EqualTo(FilenProvider.LoginRequiredMessage));
    }
}
