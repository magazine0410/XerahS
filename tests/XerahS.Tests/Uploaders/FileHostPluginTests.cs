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

using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ShareX.BackblazeB2.Plugin;
using ShareX.Box.Plugin;
using ShareX.GoogleCloudStorage.Plugin;
using ShareX.GoogleDrive.Plugin;
using ShareX.ImgFish.Plugin;
using ShareX.Mega.Plugin;
using ShareX.OneDrive.Plugin;
using ShareX.Pushbullet.Plugin;
using ShareX.Sul.Plugin;
using ShareX.YouTube.Plugin;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Uploaders;

// ShareX's OneDrive, Google Drive, Box, Google Cloud Storage, YouTube, MEGA, Backblaze B2, Pushbullet, img.fish, and
// s-ul uploaders, and the shared OAuth 2.0 client.
[TestFixture]
public class FileHostPluginTests
{
    // ── OAuth 2.0 client ────────────────────────────────────────────────────

    private static readonly OAuth2Client TestOAuth = new()
    {
        ServiceName = "Test",
        AuthorizationEndpoint = "https://auth.test/authorize",
        TokenEndpoint = "https://auth.test/token",
        Scope = "files",
        UsePkce = true,
        AuthorizationArguments = new() { ["access_type"] = "offline" }
    };

    [Test]
    public void OAuth_AuthorizationUrl_HasTheRedirectStateScopeAndPkce()
    {
        var proof = new OAuth2ProofKey(OAuth2ChallengeMethod.SHA256);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(TestOAuth.GetAuthorizationURL("client", "http://127.0.0.1:52476/cb", "state1", proof)).Query);
        Assert.Multiple(() =>
        {
            Assert.That(query["response_type"], Is.EqualTo("code"));
            Assert.That(query["client_id"], Is.EqualTo("client"));
            Assert.That(query["redirect_uri"], Is.EqualTo("http://127.0.0.1:52476/cb"));
            Assert.That(query["state"], Is.EqualTo("state1"));
            Assert.That(query["scope"], Is.EqualTo("files"));
            Assert.That(query["code_challenge"], Is.EqualTo(proof.CodeChallenge));
            Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
            Assert.That(query["access_type"], Is.EqualTo("offline"));
        });
    }

    [Test]
    public void OAuth_Token_KeepsTheRefreshTokenWhenTheServiceSendsNone_LikeShareX()
    {
        OAuth2Token? token = OAuth2Client.ParseToken("""{"access_token":"new","expires_in":3600}""", "old-refresh");
        Assert.That(token!.access_token, Is.EqualTo("new"));
        Assert.That(token.refresh_token, Is.EqualTo("old-refresh"));
        Assert.That(token.IsExpired, Is.False);
        Assert.That(OAuth2Client.ParseToken("""{"error":"invalid_grant"}""", null), Is.Null);
    }

    [Test]
    public void OAuth_CheckAuthorization_RequiresALogin()
    {
        var uploader = new BoxUploader(new OAuth2Info("id", "secret"), null, new BoxConfigModel());
        Assert.That(BoxProvider.OAuth.CheckAuthorization(uploader, new OAuth2Info("id", "secret"), null), Is.False);
        Assert.That(uploader.Errors.ToString(), Does.Contain("Box login is required"));
    }

    [Test]
    public void OAuth_Callback_ChecksTheStateAndReportsErrors()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OAuthLoopbackListener.ReadCallback("code", "s", null, null, "s").Code, Is.EqualTo("code"));
            Assert.That(OAuthLoopbackListener.ReadCallback("code", "other", null, null, "s").Error, Is.EqualTo("state_mismatch"));
            Assert.That(OAuthLoopbackListener.ReadCallback(null, "s", "access_denied", "The user denied access.", "s").ErrorDescription, Is.EqualTo("The user denied access."));
            Assert.That(OAuthLoopbackListener.ReadCallback(null, "s", null, null, "s").Error, Is.EqualTo("missing_code"));
        });
    }

    [TestCase("http://127.0.0.1:52476/oauth2/callback", true)]
    [TestCase("http://localhost:52476/oauth2/callback", true)]
    [TestCase("https://127.0.0.1:52476/oauth2/callback", false)]
    [TestCase("http://example.com:52476/callback", false)]
    [TestCase("http://127.0.0.1/callback", false)]
    public void OAuth_RedirectUri_MustBeALocalHttpAddressWithAPort(string value, bool valid)
    {
        Assert.That(OAuthLoopbackListener.ValidateRedirectUri(value, out _) == null, Is.EqualTo(valid));
    }

    // ── Google Drive ────────────────────────────────────────────────────────

    [Test]
    public void GoogleDrive_Metadata_UsesTheFolderOrSharedDrive_LikeShareX()
    {
        Assert.That(JObject.Parse(GoogleDriveUploader.CreateMetadata("a.png", null, null)).ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("""{"name":"a.png"}"""));
        JObject inDrive = JObject.Parse(GoogleDriveUploader.CreateMetadata("a.png", null, "drive1"));
        Assert.That(inDrive["parents"]![0]!.Value<string>(), Is.EqualTo("drive1"));
        JObject inFolder = JObject.Parse(GoogleDriveUploader.CreateMetadata("a.png", "folder1", "drive1"));
        Assert.That(inFolder["parents"]![0]!.Value<string>(), Is.EqualTo("folder1"));
        Assert.That(inFolder.Value<string>("driveId"), Is.EqualTo("drive1"));
    }

    [Test]
    public void GoogleDrive_DirectLink_DropsTheExportArgument_LikeShareX()
    {
        Assert.That(GoogleDriveUploader.GetDirectLink("https://drive.google.com/uc?id=abc&export=download"), Is.EqualTo("https://drive.google.com/uc?id=abc"));
    }

    // ── Google Cloud Storage ────────────────────────────────────────────────

    [Test]
    public void GoogleCloudStorage_BuildsShareXsUrlAndPath()
    {
        var config = new GoogleCloudStorageConfigModel { Bucket = "bucket", ObjectPrefix = "/shots/", RemoveExtensionImage = true };
        Assert.Multiple(() =>
        {
            Assert.That(GoogleCloudStorageUploader.GetUploadPath(config, "a.png"), Is.EqualTo("shots/a"));
            Assert.That(GoogleCloudStorageUploader.GetUploadPath(config, "a.txt"), Is.EqualTo("shots/a.txt"));
            Assert.That(GoogleCloudStorageUploader.GenerateURL(config, "shots/a b.png"), Is.EqualTo("https://storage.googleapis.com/bucket/shots/a%20b.png"));
            Assert.That(GoogleCloudStorageUploader.GenerateURL(new GoogleCloudStorageConfigModel { Bucket = "bucket", Domain = "cdn.test" }, "a.png"), Is.EqualTo("https://cdn.test/a.png"));
            Assert.That(JObject.Parse(GoogleCloudStorageUploader.CreateMetadata("a.png", true))["acl"]![0]!.Value<string>("entity"), Is.EqualTo("allUsers"));
            Assert.That(JObject.Parse(GoogleCloudStorageUploader.CreateMetadata("a.png", false))["acl"], Is.Null);
        });
    }

    [Test]
    public void GoogleCloudStorage_NeedsABucket_LikeShareX()
    {
        var secrets = new InMemorySecretStore();
        secrets.SetSecret("googlecloudstorage", "k", "clientId", "id");
        secrets.SetSecret("googlecloudstorage", "k", "oauthToken", """{"access_token":"token"}""");
        var provider = new GoogleCloudStorageProvider();
        provider.SetContext(new TestProviderContext(secrets));
        Assert.That(provider.ValidateSettings("""{"SecretKey":"k"}"""), Is.False);
        Assert.That(provider.ValidateSettings("""{"SecretKey":"k","Bucket":"b"}"""), Is.True);
    }

    // ── YouTube ─────────────────────────────────────────────────────────────

    [TestCase(YouTubeVideoPrivacy.Public, "public")]
    [TestCase(YouTubeVideoPrivacy.Unlisted, "unlisted")]
    [TestCase(YouTubeVideoPrivacy.Private, "private")]
    public void YouTube_SendsTheDocumentedPrivacyStrings(YouTubeVideoPrivacy privacy, string expected)
    {
        JObject metadata = JObject.Parse(YouTubeUploader.CreateMetadata("video", privacy));
        Assert.That(metadata["status"]!.Value<string>("privacyStatus"), Is.EqualTo(expected));
        Assert.That(metadata["snippet"]!.Value<string>("title"), Is.EqualTo("video"));
    }

    [Test]
    public void YouTube_UsesTheWatchOrShortLink_AndReportsRejections()
    {
        var result = new UploadResult { Response = """{"id":"abc","status":{"uploadStatus":"rejected","rejectionReason":"duplicate"}}""" };
        var errors = new UploaderErrorManager();
        YouTubeUploader.ApplyResponse(result, false, errors);
        Assert.That(result.URL, Is.EqualTo("https://www.youtube.com/watch?v=abc"));
        Assert.That(errors.ToString(), Is.EqualTo("YouTube upload rejected: duplicate"));

        var shortResult = new UploadResult { Response = """{"id":"abc","status":{"uploadStatus":"uploaded"}}""" };
        YouTubeUploader.ApplyResponse(shortResult, true, new UploaderErrorManager());
        Assert.That(shortResult.URL, Is.EqualTo("https://youtu.be/abc"));
    }

    // ── OneDrive and Box ────────────────────────────────────────────────────

    [Test]
    public void OneDrive_UsesTheRootOrTheFolderItem_LikeShareX()
    {
        Assert.That(OneDriveUploader.GetFolderPath(""), Is.EqualTo("me/drive/root"));
        Assert.That(OneDriveUploader.GetFolderPath("ID1"), Is.EqualTo("me/drive/items/ID1"));
    }

    [Test]
    public void Box_FilePage_LikeShareX()
    {
        Assert.That(BoxUploader.GetFilePageUrl("0", "42"), Is.EqualTo("https://app.box.com/files/0/f/0/1/f_42"));
    }

    // ── MEGA ────────────────────────────────────────────────────────────────

    [Test]
    public void Mega_Base64URL_RoundTrips()
    {
        byte[] data = [0xfb, 0xff, 0x00, 0x10, 0x3e];
        string encoded = MegaUploader.ToBase64URL(data);
        Assert.That(encoded, Does.Not.Contain("+").And.Not.Contain("/").And.Not.Contain("="));
        Assert.That(MegaUploader.FromBase64URL(encoded), Is.EqualTo(data));
    }

    [Test]
    public void Mega_ApiResponse_UnwrapsTheResultAndReportsErrorCodes()
    {
        Assert.That(MegaUploader.ParseApiResponse("""[{"p":"https://upload.test"}]""", "u").Value<string>("p"), Is.EqualTo("https://upload.test"));
        Assert.That(MegaUploader.ParseApiResponse("""{"result":"handle"}""", "l").Value<string>(), Is.EqualTo("handle"));
        var error = Assert.Throws<MegaApiException>(() => MegaUploader.ParseApiResponse("[-26]", "us"));
        Assert.That(error!.ErrorCode, Is.EqualTo(-26));
        Assert.That(error.Message, Does.Contain("two-factor"));
        Assert.Throws<MegaRequestException>(() => MegaUploader.ParseApiResponse("<html></html>", "us"));
    }

    [Test]
    public void Mega_PasswordBytes_ArePaddedToFourBytes_LikeShareX()
    {
        Assert.That(MegaUploader.PasswordToBytes("abcde"), Is.EqualTo(new byte[] { 97, 98, 99, 100, 101, 0, 0, 0 }));
    }

    // ── Backblaze B2 ────────────────────────────────────────────────────────

    [TestCase(0, null, "NewUrl")]
    [TestCase(401, "expired_auth_token", "NewUrl")]
    [TestCase(401, "unauthorized", "Fail")]
    [TestCase(408, null, "SameUrl")]
    [TestCase(429, null, "SameUrl")]
    [TestCase(503, null, "NewUrl")]
    [TestCase(400, "bad_request", "Fail")]
    [TestCase(200, null, "None")]
    public void BackblazeB2_RetriesLikeShareX(int status, string? code, string expected)
    {
        string text = code == null ? "{}" : $$"""{"status":{{status}},"code":"{{code}}","message":""}""";
        Assert.That(BackblazeB2Uploader.GetRetry(new BackblazeB2Uploader.B2Response((HttpStatusCode)status, text)).ToString(), Is.EqualTo(expected));
    }

    [Test]
    public void BackblazeB2_BuildsTheDownloadOrCustomUrl_LikeShareX()
    {
        var config = new BackblazeB2ConfigModel { BucketName = "my bucket" };
        Assert.That(BackblazeB2Uploader.BuildUrl("https://f001.backblazeb2.com", config, "shots/a b.png"),
            Is.EqualTo("https://f001.backblazeb2.com/file/my%20bucket/shots/a%20b.png"));
        config.UseCustomUrl = true;
        config.CustomUrl = "cdn.test";
        Assert.That(BackblazeB2Uploader.BuildUrl("https://f001.backblazeb2.com", config, "a.png"), Is.EqualTo("https://cdn.test/a.png"));
    }

    [Test]
    public void BackblazeB2_ChecksTheKeysPermissions_LikeShareX()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BackblazeB2Uploader.CheckUploadPermission(null, "b1", "x/a.png"), Is.Null);
            Assert.That(BackblazeB2Uploader.CheckUploadPermission(JObject.Parse("""{"bucketId":"b2"}"""), "b1", "a.png"), Does.Contain("this bucket"));
            Assert.That(BackblazeB2Uploader.CheckUploadPermission(JObject.Parse("""{"namePrefix":"shots/"}"""), "b1", "other/a.png"), Does.Contain("name prefix"));
            Assert.That(BackblazeB2Uploader.CheckUploadPermission(JObject.Parse("""{"capabilities":["readFiles"]}"""), "b1", "a.png"), Does.Contain("does not allow"));
            Assert.That(BackblazeB2Uploader.FindBucketId("""{"buckets":[{"bucketName":"a","bucketId":"1"},{"bucketName":"b","bucketId":"2"}]}""", "b"), Is.EqualTo("2"));
            Assert.That(BackblazeB2Uploader.DescribeError(new BackblazeB2Uploader.B2Response(HttpStatusCode.Unauthorized, """{"status":401,"code":"bad_auth_token","message":""}""")),
                Is.EqualTo("Got status 401 (bad_auth_token)"));
        });
    }

    // ── Pushbullet ──────────────────────────────────────────────────────────

    [Test]
    public void Pushbullet_SendsTheDeprecatedUploadFieldsOnlyWhenPresent()
    {
        Assert.That(PushbulletUploader.CreateUploadArguments(JObject.Parse("""{"upload_url":"https://upload.test"}""")), Is.Empty);
        Assert.That(PushbulletUploader.CreateUploadArguments(JObject.Parse("""{"data":{"acl":"public-read","key":"k"}}""")),
            Is.EqualTo(new Dictionary<string, string> { ["acl"] = "public-read", ["key"] = "k" }));
    }

    [Test]
    public void Pushbullet_PushesTheFileToTheDevice_AndListsNamedDevices()
    {
        Dictionary<string, string> push = PushbulletUploader.CreatePushArguments(
            JObject.Parse("""{"file_name":"a.png","file_type":"image/png","file_url":"https://dl.test/a.png"}"""), "a.png", "dev1");
        Assert.That(push["type"], Is.EqualTo("file"));
        Assert.That(push["device_iden"], Is.EqualTo("dev1"));
        Assert.That(push["file_url"], Is.EqualTo("https://dl.test/a.png"));

        List<PushbulletDevice> devices = PushbulletUploader.ParseDevices(JObject.Parse("""{"devices":[{"iden":"1","nickname":"Phone"},{"iden":"2"}]}"""));
        Assert.That(devices.Select(device => device.Name), Is.EqualTo(new[] { "Phone" }));
    }

    [Test]
    public void Pushbullet_NeedsATokenAndADevice_LikeShareX()
    {
        var secrets = new InMemorySecretStore();
        secrets.SetSecret("pushbullet", "k", "accessToken", "token");
        var provider = new PushbulletProvider();
        provider.SetContext(new TestProviderContext(secrets));
        Assert.That(provider.ValidateSettings("""{"SecretKey":"k"}"""), Is.False);
        Assert.That(provider.ValidateSettings("""{"SecretKey":"k","DeviceList":[{"Key":"1","Name":"Phone"}],"SelectedDeviceKey":"1"}"""), Is.True);
    }

    // ── img.fish and s-ul ───────────────────────────────────────────────────

    [Test]
    public void ImgFish_UsesTheLinkAndDeletionUrl_AndReportsErrors()
    {
        var result = new UploadResult { IsSuccess = true, Response = """{"link":"https://img.fish/a","destroy":"https://img.fish/d/a"}""" };
        ImgFishUploader.ApplyResponse(result, new UploaderErrorManager());
        Assert.That(result.URL, Is.EqualTo("https://img.fish/a"));
        Assert.That(result.DeletionURL, Is.EqualTo("https://img.fish/d/a"));

        var failed = new UploadResult { IsSuccess = true, Response = """{"error":"No file uploaded"}""" };
        var errors = new UploaderErrorManager();
        ImgFishUploader.ApplyResponse(failed, errors);
        Assert.That(failed.IsSuccess, Is.False);
        Assert.That(errors.ToString(), Is.EqualTo("img.fish: No file uploaded"));

        Assert.That(ImgFishUploader.CreateHeaders("")["x-api-key"], Is.Null);
        Assert.That(ImgFishUploader.CreateHeaders("key")["x-api-key"], Is.EqualTo("key"));
        Assert.That(new ImgFishProvider().ValidateSettings("""{"FileIDLength":7}"""), Is.False);
        Assert.That(new ImgFishProvider().ValidateSettings("{}"), Is.True);
    }

    [Test]
    public void Sul_BuildsTheUrlAndDeletionUrl_AndReadsItsReason()
    {
        var result = new UploadResult { IsSuccess = true, Response = """{"protocol":"https://","domain":"me.s-ul.eu","filename":"abc","extension":".png"}""" };
        SulUploader.ApplyResponse(result, "key1", new UploaderErrorManager());
        Assert.That(result.URL, Is.EqualTo("https://me.s-ul.eu/abc.png"));
        Assert.That(result.DeletionURL, Is.EqualTo("https://s-ul.eu/delete.php?key=key1&file=abc"));
        Assert.That(SulUploader.CreateArguments("key1")["wizard"], Is.EqualTo("true"));

        var failed = new UploadResult { IsSuccess = true, Response = """{"success":false,"reason":"Wizards only beyond this point. Error code: 103"}""" };
        var errors = new UploaderErrorManager();
        SulUploader.ApplyResponse(failed, "bad", errors);
        Assert.That(errors.ToString(), Does.Contain("Error code: 103"));
    }

    // ── Settings and imported secrets ───────────────────────────────────────

    [AvaloniaTest]
    public void SettingsViews_LoadWithTheirViewModels()
    {
        var secrets = new InMemorySecretStore();
        foreach (UploaderProviderBase provider in new UploaderProviderBase[]
        {
            new ImgFishProvider(), new SulProvider(), new PushbulletProvider(), new BackblazeB2Provider(), new MegaProvider(),
            new GoogleDriveProvider(), new GoogleCloudStorageProvider(), new YouTubeProvider(), new OneDriveProvider(), new BoxProvider()
        })
        {
            var viewModel = provider.CreateConfigViewModel()!;
            ((IProviderContextAware)viewModel).SetContext(new TestProviderContext(secrets));
            viewModel.LoadFromJson("{}");
            var view = (Control)provider.CreateConfigView()!;
            view.DataContext = viewModel;
            var window = new Window { Content = view, Width = 800, Height = 600 };
            window.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.That(view.IsLoaded, Is.True, provider.ProviderId);
            Assert.That(viewModel.ToJson(), Does.Contain("SecretKey"), provider.ProviderId);
            window.Close();
        }
    }

    [Test]
    public void ImportedPlaintextSecrets_MoveIntoTheSecretStore()
    {
        var secrets = new InMemorySecretStore();

        Assert.That(new BackblazeB2Provider().TryMigrateSecrets("""{"SecretKey":"k1","ApplicationKeyId":"id1","ApplicationKey":"key1","BucketName":"b"}""",
            secrets, out string b2, out int b2Count), Is.True);
        Assert.That(b2Count, Is.EqualTo(2));
        Assert.That(secrets.GetSecret("backblazeb2", "k1", "applicationKey"), Is.EqualTo("key1"));
        Assert.That(b2, Does.Not.Contain("key1").And.Not.Contain("id1").And.Contain("\"BucketName\": \"b\""));

        Assert.That(new SulProvider().TryMigrateSecrets("""{"SecretKey":"k2","APIKey":"sul-key"}""", secrets, out _, out _), Is.True);
        Assert.That(secrets.GetSecret("sul", "k2", "apiKey"), Is.EqualTo("sul-key"));

        Assert.That(new PushbulletProvider().TryMigrateSecrets("""{"SecretKey":"k3","UserAPIKey":"pb","SelectedDeviceKey":"1"}""", secrets, out string pushbullet, out _), Is.True);
        Assert.That(secrets.GetSecret("pushbullet", "k3", "accessToken"), Is.EqualTo("pb"));
        Assert.That(PushbulletProvider.DeserializeConfig(pushbullet).SelectedDeviceKey, Is.EqualTo("1"));

        Assert.That(new SulProvider().TryMigrateSecrets("""{"SecretKey":"k2"}""", secrets, out _, out _), Is.False);
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<(string ProviderId, string SecretKey, string Name), string> _values = new();

        public string? GetSecret(string providerId, string secretKey, string name)
            => _values.TryGetValue((providerId, secretKey, name), out string? value) ? value : null;

        public void SetSecret(string providerId, string secretKey, string name, string value)
            => _values[(providerId, secretKey, name)] = value;

        public void DeleteSecret(string providerId, string secretKey, string name)
            => _values.Remove((providerId, secretKey, name));

        public bool HasSecret(string providerId, string secretKey, string name)
            => _values.ContainsKey((providerId, secretKey, name));
    }

    private sealed class TestProviderContext(ISecretStore secrets) : IProviderContext
    {
        public ISecretStore Secrets { get; } = secrets;
    }
}
