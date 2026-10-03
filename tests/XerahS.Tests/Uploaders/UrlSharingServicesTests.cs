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

using System.Net.Mail;
using Newtonsoft.Json;
using NUnit.Framework;
using ShareX.Email.Plugin;
using ShareX.Email.Plugin.ViewModels;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;
using XerahS.Uploaders.SharingServices;

namespace XerahS.Tests.Uploaders;

[TestFixture, NonParallelizable]
public sealed class UrlSharingServicesTests
{
    private string _directory = null!;
    private string _originalFolder = null!;
    private Func<EmailMessageDraft, CancellationToken, Task<EmailMessageDraft?>>? _previousCompose;
    private Func<string, bool> _previousOpenUrl = null!;

    [SetUp]
    public void SetUp()
    {
        _originalFolder = SettingsManager.PersonalFolder;
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-url-sharing-" + Guid.NewGuid().ToString("N"));
        SettingsManager.PersonalFolder = _directory;
        Directory.CreateDirectory(PathsManager.SettingsFolder);
        InstanceManager.Instance.ReloadConfiguration();
        _previousCompose = UrlSharingHost.ComposeEmailAsync;
        _previousOpenUrl = UrlSharingHost.OpenUrl;
    }

    [TearDown]
    public void TearDown()
    {
        UrlSharingHost.ComposeEmailAsync = _previousCompose;
        UrlSharingHost.OpenUrl = _previousOpenUrl;
        SettingsManager.PersonalFolder = _originalFolder;
        InstanceManager.Instance.ReloadConfiguration();
        Directory.Delete(_directory, recursive: true);
    }

    // ShareX's URL formats; Reddit, Pinterest and VK use https.
    [TestCase("share-facebook", "https://www.facebook.com/sharer/sharer.php?u={0}")]
    [TestCase("share-reddit", "https://www.reddit.com/submit?url={0}")]
    [TestCase("share-pinterest", "https://pinterest.com/pin/create/button/?url={0}&media={0}")]
    [TestCase("share-tumblr", "https://www.tumblr.com/share?v=3&u={0}")]
    [TestCase("share-linkedin", "https://www.linkedin.com/shareArticle?url={0}")]
    [TestCase("share-vk", "https://vk.com/share.php?url={0}")]
    [TestCase("share-google-lens", "https://lens.google.com/uploadbyurl?url={0}")]
    [TestCase("share-bing-visual-search", "https://www.bing.com/images/search?view=detailv2&iss=sbi&q=imgurl:{0}")]
    public async Task LinkServices_OpenTheirSharePage_WithTheUrlEncoded(string providerId, string format)
    {
        ProviderCatalog.InitializeBuiltInProviders();
        const string url = "https://files.test/a b.png?x=1&y=2";
        string? opened = null;
        UrlSharingHost.OpenUrl = page => { opened = page; return true; };
        var sharer = (UrlSharer)ProviderCatalog.GetProvider(providerId)!.CreateInstance("{}");
        UploadResult result = await sharer.ShareURLAsync(url);
        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.EqualTo(string.Format(format, "https%3A%2F%2Ffiles.test%2Fa%20b.png%3Fx%3D1%26y%3D2")));
            Assert.That(result.Errors.Count, Is.Zero);
            Assert.That(result.IsURLExpected, Is.False);
        });
    }

    [Test]
    public async Task LinkService_ReportsWhenTheBrowserCannotOpen()
    {
        UrlSharingHost.OpenUrl = _ => false;
        UploadResult result = await new LinkSharer("https://share.test/?u={0}").ShareURLAsync("https://a.test");
        Assert.That(result.Errors.ToString(), Does.Contain("could not be opened"));
    }

    [Test]
    public void LinkServices_AreAddedOnce_AndARemovedOneStaysRemoved()
    {
        ProviderCatalog.InitializeBuiltInProviders();
        var manager = InstanceManager.Instance;
        Assert.That(LinkSharingProvider.EnsureInstances(), Is.EqualTo(8));
        var added = manager.GetInstancesByCategory(UploaderCategory.UrlSharing);
        Assert.That(added.Select(instance => instance.DisplayName).First(), Is.EqualTo("Facebook (UrlSharing)"));
        Assert.That(LinkSharingProvider.EnsureInstances(), Is.Zero);

        manager.RemoveInstance(added.Single(instance => instance.ProviderId == "share-vk").InstanceId);
        manager.ReloadConfiguration();
        Assert.Multiple(() =>
        {
            Assert.That(LinkSharingProvider.EnsureInstances(), Is.Zero, "A removed link service is not added back.");
            Assert.That(manager.GetInstancesByCategory(UploaderCategory.UrlSharing), Has.Count.EqualTo(7));
        });
    }

    [Test]
    public async Task Email_AutomaticSend_SendsTheUrlWithoutTheWindow()
    {
        var (sharer, secrets) = CreateEmailSharer(new EmailConfigModel
        {
            FromEmail = "me@example.test", AutomaticSend = true, AutomaticSendTo = " friend@example.test ", DefaultSubject = "Look"
        });
        UrlSharingHost.ComposeEmailAsync = (_, _) => throw new AssertionException("The window must not open.");
        MailMessage? sent = null;
        sharer.Send = (message, config, password, _) =>
        {
            sent = message;
            Assert.That(password, Is.EqualTo("secret"));
            return Task.CompletedTask;
        };
        UploadResult result = await sharer.ShareURLAsync("https://files.test/a.png");
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Count, Is.Zero);
            Assert.That(sent!.To.Single().Address, Is.EqualTo("friend@example.test"));
            Assert.That(sent.From!.Address, Is.EqualTo("me@example.test"));
            Assert.That(sent.Subject, Is.EqualTo("Look"));
            Assert.That(sent.Body, Is.EqualTo("https://files.test/a.png"));
        });
    }

    [Test]
    public async Task Email_Window_IsPrefilledWithTheLastAddress_AndCancellingSendsNothing()
    {
        var config = new EmailConfigModel { FromEmail = "me@example.test", DefaultSubject = "Subject" };
        var (sharer, secrets) = CreateEmailSharer(config);
        secrets.SetSecret(EmailProvider.Id, config.SecretKey, EmailSharer.LastToSecret, "last@example.test");
        EmailMessageDraft? shown = null;
        int sends = 0;
        sharer.Send = (_, _, _, _) => { sends++; return Task.CompletedTask; };

        UrlSharingHost.ComposeEmailAsync = (draft, _) => { shown = draft; return Task.FromResult<EmailMessageDraft?>(null); };
        UploadResult cancelled = await sharer.ShareURLAsync("https://files.test/a.png");
        Assert.Multiple(() =>
        {
            Assert.That(shown, Is.EqualTo(new EmailMessageDraft("last@example.test", "Subject", "https://files.test/a.png")));
            Assert.That(sends, Is.Zero);
            Assert.That(cancelled.Errors.Count, Is.Zero, "As in ShareX, cancelling the window is not an error.");
        });

        UrlSharingHost.ComposeEmailAsync = (draft, _) => Task.FromResult<EmailMessageDraft?>(draft with { ToEmail = "new@example.test" });
        await sharer.ShareURLAsync("https://files.test/a.png");
        Assert.Multiple(() =>
        {
            Assert.That(sends, Is.EqualTo(1));
            Assert.That(secrets.GetSecret(EmailProvider.Id, config.SecretKey, EmailSharer.LastToSecret), Is.EqualTo("new@example.test"));
        });
    }

    [Test]
    public async Task Email_ReportsMissingSettingsAndSendFailures()
    {
        var (unconfigured, _) = CreateEmailSharer(new EmailConfigModel { FromEmail = string.Empty });
        Assert.That((await unconfigured.ShareURLAsync("https://a.test")).Errors.ToString(), Does.Contain("Email is not set up"));

        var (sharer, _) = CreateEmailSharer(new EmailConfigModel { FromEmail = "me@example.test", AutomaticSend = true, AutomaticSendTo = "x@example.test" });
        sharer.Send = (_, _, _, _) => throw new SmtpException("Authentication failed");
        Assert.That((await sharer.ShareURLAsync("https://a.test")).Errors.ToString(), Does.Contain("Authentication failed"));
    }

    [Test]
    public void EmailSettings_KeepThePasswordInTheSecretStore()
    {
        var secrets = new MemorySecretStore();
        var viewModel = new EmailConfigViewModel();
        viewModel.SetContext(new TestContext(secrets));
        viewModel.LoadFromJson(JsonConvert.SerializeObject(new EmailConfigModel()));
        viewModel.FromEmail = "me@example.test";
        viewModel.Password = "hunter2";
        Assert.That(viewModel.Validate(), Is.True);
        string json = viewModel.ToJson();
        var saved = JsonConvert.DeserializeObject<EmailConfigModel>(json)!;
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("hunter2"));
            Assert.That(secrets.GetSecret(EmailProvider.Id, saved.SecretKey, EmailSharer.PasswordSecret), Is.EqualTo("hunter2"));
        });

        viewModel.AutomaticSend = true;
        Assert.That(viewModel.Validate(), Is.False, "Automatic sending needs an address.");
    }

    // ShareX's ProcessTextUpload: the first clipboard URL option that is on wins.
    [TestCase(false, false, false, " https://example.test/page ", TaskJob.TextUpload, EDataType.Text)]
    [TestCase(true, true, true, " https://example.test/page ", TaskJob.DownloadUpload, EDataType.File)]
    [TestCase(false, true, true, "https://example.test/page", TaskJob.ShortenURL, EDataType.URL)]
    [TestCase(false, false, true, " https://example.test/page ", TaskJob.ShareURL, EDataType.URL)]
    [TestCase(false, false, true, "not a url", TaskJob.TextUpload, EDataType.Text)]
    public void ClipboardURL_UsesShareXsOrderOfOptions(bool contents, bool shorten, bool share, string text, TaskJob job, EDataType dataType)
    {
        var settings = new TaskSettings();
        settings.UploadSettings.ClipboardUploadURLContents = contents;
        settings.UploadSettings.ClipboardUploadShortenURL = shorten;
        settings.UploadSettings.ClipboardUploadShareURL = share;
        var info = new TaskInfo(settings) { TextContent = text, DataType = EDataType.Text, Job = TaskJob.TextUpload };
        WorkerTask.ApplyClipboardURLJob(info, settings);
        Assert.Multiple(() =>
        {
            Assert.That(info.Job, Is.EqualTo(job));
            Assert.That(info.DataType, Is.EqualTo(dataType));
            Assert.That(info.TextContent, Is.EqualTo(job == TaskJob.TextUpload ? text : "https://example.test/page"));
        });
    }

    [Test]
    public void Workflows_WithoutDestinationOverride_UseTheDefaultSharingService()
    {
        var defaults = new TaskSettings { UrlSharingDestinationInstanceId = "default-sharing" };
        var workflow = new TaskSettings { UrlSharingDestinationInstanceId = "own-sharing" };
        workflow.SetDefaultSettings(defaults);
        Assert.That(workflow.UrlSharingDestinationInstanceId, Is.EqualTo("default-sharing"));
        workflow = new TaskSettings { UseDefaultDestinations = false, UrlSharingDestinationInstanceId = "own-sharing" };
        workflow.SetDefaultSettings(defaults);
        Assert.Multiple(() =>
        {
            Assert.That(workflow.UrlSharingDestinationInstanceId, Is.EqualTo("own-sharing"));
            Assert.That(workflow.GetDestinationInstanceIdByCategory(UploaderCategory.UrlSharing), Is.EqualTo("own-sharing"));
        });
    }

    private static (EmailSharer Sharer, MemorySecretStore Secrets) CreateEmailSharer(EmailConfigModel config)
    {
        var secrets = new MemorySecretStore();
        secrets.SetSecret(EmailProvider.Id, config.SecretKey, EmailSharer.PasswordSecret, "secret");
        var provider = new EmailProvider();
        provider.SetContext(new TestContext(secrets));
        return ((EmailSharer)provider.CreateInstance(JsonConvert.SerializeObject(config)), secrets);
    }

    private sealed class TestContext(ISecretStore secrets) : IProviderContext
    {
        public ISecretStore Secrets { get; } = secrets;
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new();
        private static string Key(string provider, string key, string name) => $"{provider}|{key}|{name}";
        public string? GetSecret(string providerId, string secretKey, string name) => _values.GetValueOrDefault(Key(providerId, secretKey, name));
        public void SetSecret(string providerId, string secretKey, string name, string value) => _values[Key(providerId, secretKey, name)] = value;
        public void DeleteSecret(string providerId, string secretKey, string name) => _values.Remove(Key(providerId, secretKey, name));
        public bool HasSecret(string providerId, string secretKey, string name) => _values.ContainsKey(Key(providerId, secretKey, name));
    }
}
