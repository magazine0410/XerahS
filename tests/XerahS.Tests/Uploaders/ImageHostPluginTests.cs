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

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using ShareX.Chevereto.Plugin;
using ShareX.Flickr.Plugin;
using ShareX.ImageChest.Plugin;
using ShareX.ImgBB.Plugin;
using ShareX.Upaste.Plugin;
using ShareX.Vgyme.Plugin;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Uploaders;

// ShareX's Flickr, vgy.me, Chevereto, and uPaste uploaders, and the ImgBB and Image Chest destinations.
[TestFixture]
public class ImageHostPluginTests
{
    // ── Flickr ──────────────────────────────────────────────────────────────

    [Test]
    public void FlickrOAuth_SignsWithHmacSha1_LikeThePublishedExample()
    {
        // The worked example from Twitter's "Creating a signature" guide for OAuth 1.0a.
        var args = new Dictionary<string, string>
        {
            ["status"] = "Hello Ladies + Gentlemen, a signed OAuth request!",
            ["include_entities"] = "true"
        };

        Dictionary<string, string> parameters = FlickrOAuth.Sign("POST", "https://api.twitter.com/1.1/statuses/update.json", args,
            "xvz1evFS4wEEPTGEFPHBog", "kAcSOqF21Fu85e7zjz7ZN2U4ZRhfV3WpwPAoE3Z7kBw",
            "370773112-GmHxMAgYyLbNEtIKZeRNFsMKPR9EyMZeS9weJAEb", "LswwdoUaIvS8ltyTt5jkRh4J50vUPVVHtR2YPi5kE",
            nonce: "kYjzVBB8Y0ZFabxSWbWovY3uYSQ2pTgmZeNu2VS4cg", timestamp: "1318622958");

        Assert.That(parameters["oauth_signature"], Is.EqualTo("hCtSmYh+iHYCEqBWrE7C7hYmtUk="));
    }

    [Test]
    public void FlickrOAuth_ParsesTokenResponses()
    {
        var values = FlickrOAuth.ParseResponse("fullname=Jamal%20Fanaian&oauth_token=72157626318069415-087bfc7b5816092c&oauth_token_secret=a202d1f853ec69de&user_nsid=21207597%40N07&username=jamalfanaian");

        Assert.That(values["oauth_token"], Is.EqualTo("72157626318069415-087bfc7b5816092c"));
        Assert.That(values["oauth_token_secret"], Is.EqualTo("a202d1f853ec69de"));
        Assert.That(values["fullname"], Is.EqualTo("Jamal Fanaian"));
        Assert.That(values["user_nsid"], Is.EqualTo("21207597@N07"));
    }

    [Test]
    public void Flickr_SendsOnlyTheSettingsThatAreSet_LikeShareX()
    {
        var args = FlickrUploader.CreateUploadArguments(new FlickrConfigModel { Title = "Capture", IsPublic = "0", SafetyLevel = "1" });

        Assert.That(args, Is.EquivalentTo(new Dictionary<string, string> { ["title"] = "Capture", ["is_public"] = "0", ["safety_level"] = "1" }));
    }

    [Test]
    public void Flickr_ReadsThePhotoIdOrTheError()
    {
        var errors = new UploaderErrorManager();
        Assert.That(FlickrUploader.ParseUploadResponse("<?xml version=\"1.0\"?><rsp stat=\"ok\"><photoid>1234</photoid></rsp>", errors), Is.EqualTo("1234"));
        Assert.That(FlickrUploader.ParseUploadResponse("<rsp stat=\"fail\"><err code=\"98\" msg=\"Invalid auth token\"/></rsp>", errors), Is.Null);
        Assert.That(errors.ToString(), Does.Contain("Invalid auth token"));
    }

    [TestCase(true, "https://live.staticflickr.com/1/1234_o.png")]
    [TestCase(false, "https://www.flickr.com/photos/user/1234/sizes/o/")]
    public void Flickr_UsesTheLargestSize_LikeShareX(bool directLink, string expected)
    {
        const string sizes = """
            {"sizes":{"size":[
              {"label":"Square","source":"https://live.staticflickr.com/1/1234_s.png","url":"https://www.flickr.com/photos/user/1234/sizes/sq/"},
              {"label":"Original","source":"https://live.staticflickr.com/1/1234_o.png","url":"https://www.flickr.com/photos/user/1234/sizes/o/"}]},"stat":"ok"}
            """;

        Assert.That(FlickrUploader.ParseSizesResponse(sizes, directLink), Is.EqualTo(expected));
    }

    [Test]
    public void Flickr_NeedsTheAppKeyAndAnAuthorizedAccount()
    {
        Assert.That(FlickrProvider.GetConfigError(new FlickrConfigModel(), "secret", "token", "token secret"), Does.Contain("app"));
        Assert.That(FlickrProvider.GetConfigError(new FlickrConfigModel { ConsumerKey = "key" }, "secret", null, null), Does.Contain("Authorize"));
        Assert.That(FlickrProvider.GetConfigError(new FlickrConfigModel { ConsumerKey = "key" }, "secret", "token", "token secret"), Is.Null);
    }

    // ── vgy.me ──────────────────────────────────────────────────────────────

    [Test]
    public void Vgyme_UsesTheImageAndDeletionUrls_LikeShareX()
    {
        var result = new UploadResult { IsSuccess = true, Response = """
            {"error":false,"url":"https://vgy.me/u/abc","image":"https://i.vgy.me/abc.png","delete":"https://vgy.me/delete/xyz"}
            """ };

        VgymeUploader.ApplyResponse(result, new UploaderErrorManager());

        Assert.That(result.URL, Is.EqualTo("https://i.vgy.me/abc.png"));
        Assert.That(result.DeletionURL, Is.EqualTo("https://vgy.me/delete/xyz"));
    }

    [Test]
    public void Vgyme_ReportsItsMessages()
    {
        var result = new UploadResult { IsSuccess = true, Response = """{"error":true,"messages":{"Unauthorized":"Invalid user key."}}""" };
        var errors = new UploaderErrorManager();

        VgymeUploader.ApplyResponse(result, errors);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(errors.ToString(), Does.Contain("Invalid user key."));
    }

    [Test]
    public void Vgyme_RequiresAUserKey_BecauseVgymeRejectsAnonymousUploads()
    {
        var secrets = new InMemorySecretStore();
        secrets.SetSecret("vgyme", "k", "userKey", "key");
        var provider = new VgymeProvider();
        provider.SetContext(new TestProviderContext(secrets));
        Assert.That(provider.ValidateSettings("{}"), Is.False);
        Assert.That(provider.ValidateSettings("""{"SecretKey":"other"}"""), Is.False);
        Assert.That(provider.ValidateSettings("""{"SecretKey":"k"}"""), Is.True);

        var uploader = new VgymeUploader(null);
        using var stream = new MemoryStream([1]);
        Assert.That(uploader.Upload(stream, "a.png").URL, Is.Null.Or.Empty);
        Assert.That(uploader.Errors.ToString(), Is.EqualTo(VgymeProvider.MissingUserKeyMessage));
    }

    // ── uPaste ──────────────────────────────────────────────────────────────

    [Test]
    public void Upaste_UsesThePasteLink_LikeShareX()
    {
        // The success response from uPaste's API v2 documentation.
        var result = new UploadResult { IsSuccess = true, Response = """
            {"paste":{"key":"562eb3","link":"https://upaste.me/562eb3","raw":"https://upaste.me/r/562eb3","download":"https://upaste.me/d/562eb3","expires":1756785600,"privacy":"unlisted","anonymous":0,"burn":0,"protected":0},"status":"success"}
            """ };

        UpasteUploader.ApplyResponse(result, new UploaderErrorManager());

        Assert.That(result.URL, Is.EqualTo("https://upaste.me/562eb3"));
    }

    [Test]
    public void Upaste_ReportsItsError()
    {
        var result = new UploadResult { IsSuccess = true, Response = """{"errorcode":0,"error":"invalid_key","status":"error"}""" };
        var errors = new UploaderErrorManager();

        UpasteUploader.ApplyResponse(result, errors);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(errors.ToString(), Is.EqualTo("uPaste: invalid_key"));
    }

    [TestCase(false, "1")]
    [TestCase(true, "0")]
    public void Upaste_SendsShareXsArguments(bool isPublic, string privacy)
    {
        Assert.That(UpasteUploader.CreateArguments("text", isPublic), Is.EqualTo(new Dictionary<string, string>
        {
            ["paste"] = "text", ["privacy"] = privacy, ["expire"] = "0"
        }));
    }

    [Test]
    public void Upaste_RequiresAUserKey_BecauseUpasteRejectsRequestsWithoutOne()
    {
        var secrets = new InMemorySecretStore();
        secrets.SetSecret("upaste", "k", "userKey", "key");
        var provider = new UpasteProvider();
        provider.SetContext(new TestProviderContext(secrets));
        Assert.That(provider.ValidateSettings("{}"), Is.False);
        Assert.That(provider.ValidateSettings("""{"SecretKey":"other"}"""), Is.False);
        Assert.That(provider.ValidateSettings("""{"SecretKey":"k"}"""), Is.True);
        Assert.That(provider.SupportedCategories, Is.EqualTo(new[] { UploaderCategory.Text }));

        var uploader = new UpasteUploader(null, false);
        Assert.That(uploader.UploadText("text", "a.txt").URL, Is.Null.Or.Empty);
        Assert.That(uploader.Errors.ToString(), Is.EqualTo(UpasteProvider.MissingUserKeyMessage));
    }

    // ── Chevereto ───────────────────────────────────────────────────────────

    [TestCase(true, "https://img.example.com/images/a.png")]
    [TestCase(false, "https://img.example.com/image/a")]
    public void Chevereto_UsesTheDirectOrViewerUrl_LikeShareX(bool directURL, string expected)
    {
        var result = new UploadResult { IsSuccess = true, Response = """
            {"status_code":200,"image":{"url":"https://img.example.com/images/a.png","url_viewer":"https://img.example.com/image/a","thumb":{"url":"https://img.example.com/images/a.th.png"}}}
            """ };

        CheveretoUploader.ApplyResponse(result, directURL, new UploaderErrorManager());

        Assert.That(result.URL, Is.EqualTo(expected));
        Assert.That(result.ThumbnailURL, Is.EqualTo("https://img.example.com/images/a.th.png"));
    }

    [Test]
    public void Chevereto_ReportsItsErrorMessage()
    {
        var result = new UploadResult { Response = """{"status_code":400,"error":{"message":"Invalid API v1 key.","code":100}}""" };
        var errors = new UploaderErrorManager();

        CheveretoUploader.ApplyResponse(result, true, errors);

        Assert.That(errors.ToString(), Does.Contain("Invalid API v1 key."));
    }

    [Test]
    public void Chevereto_NeedsTheUploadUrlAndApiKey_LikeShareX()
    {
        Assert.That(CheveretoProvider.GetConfigError(new CheveretoConfigModel(), "key"), Does.Contain("upload URL"));
        Assert.That(CheveretoProvider.GetConfigError(new CheveretoConfigModel { UploadURL = "https://example.com/api/1/upload" }, ""), Does.Contain("API key"));
    }

    // ── ImgBB ───────────────────────────────────────────────────────────────

    [TestCase(true, "https://i.ibb.co/abc/a.png")]
    [TestCase(false, "https://ibb.co/abc")]
    public void ImgBB_UsesTheImageOrViewerUrl_WithThumbnailAndDeletionUrls(bool directLink, string expected)
    {
        var result = new UploadResult { IsSuccess = true, Response = """
            {"data":{"id":"abc","url_viewer":"https://ibb.co/abc","url":"https://i.ibb.co/abc/a.png","thumb":{"url":"https://i.ibb.co/abc/a-th.png"},"delete_url":"https://ibb.co/abc/del"},"success":true,"status":200}
            """ };

        ImgBBUploader.ApplyResponse(result, directLink, new UploaderErrorManager());

        Assert.That(result.URL, Is.EqualTo(expected));
        Assert.That(result.ThumbnailURL, Is.EqualTo("https://i.ibb.co/abc/a-th.png"));
        Assert.That(result.DeletionURL, Is.EqualTo("https://ibb.co/abc/del"));
    }

    [Test]
    public void ImgBB_ReportsItsErrorMessage()
    {
        // The response the API gave for an invalid key.
        var result = new UploadResult { Response = """{"status_code":400,"error":{"message":"Invalid API v1 key.","code":100},"status_txt":"Bad Request"}""" };
        var errors = new UploaderErrorManager();

        ImgBBUploader.ApplyResponse(result, true, errors);

        Assert.That(errors.ToString(), Does.Contain("ImgBB: Invalid API v1 key."));
    }

    [TestCase(0, null)]
    [TestCase(300, "300")]
    [TestCase(10, "60")]
    [TestCase(99999999, "15552000")]
    public void ImgBB_SendsTheExpirationWithinItsLimits(int expiration, string? expected)
    {
        var args = ImgBBUploader.CreateArguments(new ImgBBConfigModel { Expiration = expiration }, "key");

        Assert.That(args["key"], Is.EqualTo("key"));
        Assert.That(args.GetValueOrDefault("expiration"), Is.EqualTo(expected));
    }

    // ── Image Chest ─────────────────────────────────────────────────────────

    [TestCase(true, "https://cdn.imgchest.com/files/nw7w6cmlvye.png")]
    [TestCase(false, "https://imgchest.com/p/3qe4gdvj4j2")]
    public void ImageChest_UsesTheImageLinkOrPostPage_WithTheDeletionUrl(bool directLink, string expected)
    {
        var result = new UploadResult { IsSuccess = true, Response = """
            {"data":{"id":"3qe4gdvj4j2","privacy":"hidden","image_count":1,"images":[{"id":"nw7w6cmlvye","link":"https://cdn.imgchest.com/files/nw7w6cmlvye.png","position":1}],"delete_url":"https://imgchest.com/p/3qe4gdvj4j2/delete/abc"}}
            """ };

        ImageChestUploader.ApplyResponse(result, directLink, new UploaderErrorManager());

        Assert.That(result.URL, Is.EqualTo(expected));
        Assert.That(result.DeletionURL, Is.EqualTo("https://imgchest.com/p/3qe4gdvj4j2/delete/abc"));
    }

    [Test]
    public void ImageChest_ReportsItsMessageAndFieldErrors()
    {
        var result = new UploadResult { Response = """{"message":"The given data was invalid.","errors":{"images":["The images field is required."]}}""" };
        var errors = new UploaderErrorManager();

        ImageChestUploader.ApplyResponse(result, true, errors);

        Assert.That(errors.ToString(), Does.Contain("The given data was invalid.").And.Contain("The images field is required."));
    }

    [Test]
    public void ImageChest_SendsThePostOptions()
    {
        var args = ImageChestUploader.CreateArguments(new ImageChestConfigModel { Title = " Capture ", Privacy = "unknown", Nsfw = true });

        Assert.That(args["title"], Is.EqualTo("Capture"));
        Assert.That(args["privacy"], Is.EqualTo("hidden"));
        Assert.That(args["nsfw"], Is.EqualTo("true"));
        Assert.That(args["anonymous"], Is.EqualTo("false"));
    }

    // ── Settings views ──────────────────────────────────────────────────────

    [AvaloniaTest]
    public void SettingsViews_LoadWithTheirViewModels()
    {
        var secrets = new InMemorySecretStore();
        foreach (UploaderProviderBase provider in new UploaderProviderBase[]
        {
            new VgymeProvider(), new CheveretoProvider(), new FlickrProvider(), new ImgBBProvider(), new ImageChestProvider(), new UpasteProvider()
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

    // ── Settings from a ShareX configuration import ─────────────────────────

    [Test]
    public void ImportedPlaintextSecrets_MoveIntoTheSecretStore()
    {
        var secrets = new InMemorySecretStore();

        Assert.That(new VgymeProvider().TryMigrateSecrets("""{"SecretKey":"k1","UserKey":"user-key"}""", secrets, out string vgyme, out _), Is.True);
        Assert.That(secrets.GetSecret("vgyme", "k1", "userKey"), Is.EqualTo("user-key"));
        Assert.That(vgyme, Does.Not.Contain("user-key"));

        Assert.That(new CheveretoProvider().TryMigrateSecrets("""{"SecretKey":"k2","UploadURL":"https://example.com/api/1/upload","APIKey":"api-key"}""", secrets, out string chevereto, out _), Is.True);
        Assert.That(secrets.GetSecret("chevereto", "k2", "apiKey"), Is.EqualTo("api-key"));
        Assert.That(chevereto, Does.Not.Contain("api-key").And.Contain("example.com"));

        Assert.That(new UpasteProvider().TryMigrateSecrets("""{"SecretKey":"k3","UserKey":"paste-key","IsPublic":true}""", secrets, out string upaste, out _), Is.True);
        Assert.That(secrets.GetSecret("upaste", "k3", "userKey"), Is.EqualTo("paste-key"));
        Assert.That(upaste, Does.Not.Contain("paste-key"));
        Assert.That(UpasteProvider.DeserializeConfig(upaste).IsPublic, Is.True);

        Assert.That(new VgymeProvider().TryMigrateSecrets("""{"SecretKey":"k1"}""", secrets, out _, out _), Is.False);
    }

    [Test]
    public void Providers_ReadTheirSecretsFromTheSecretStore()
    {
        var secrets = new InMemorySecretStore();
        secrets.SetSecret("imgbb", "k", "apiKey", "key");
        var provider = new ImgBBProvider();
        provider.SetContext(new TestProviderContext(secrets));

        Assert.That(provider.ValidateSettings("""{"SecretKey":"k"}"""), Is.True);
        Assert.That(provider.ValidateSettings("""{"SecretKey":"other"}"""), Is.False);
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
