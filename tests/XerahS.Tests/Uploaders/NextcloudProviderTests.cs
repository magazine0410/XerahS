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

using Newtonsoft.Json;
using NUnit.Framework;
using ShareX.Nextcloud.Plugin;
using System.Reflection;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Uploaders;

[TestFixture]
public class NextcloudProviderTests
{
    [Test]
    public void ValidateSettings_AcceptsLegacyConfigWhenUserIdIsPresent()
    {
        const string secretKey = "nextcloud-secret";
        NextcloudProvider provider = CreateProvider(secretKey, "app-password");
        string settingsJson = JsonConvert.SerializeObject(new NextcloudConfigModel
        {
            ServerUrl = "https://cloud.example.com",
            LoginName = string.Empty,
            UserId = "alice",
            SecretKey = secretKey
        });

        bool isValid = provider.ValidateSettings(settingsJson);

        Assert.That(isValid, Is.True);
    }

    [Test]
    public void ValidateSettings_RejectsConfigWithoutLoginIdentity()
    {
        const string secretKey = "nextcloud-secret";
        NextcloudProvider provider = CreateProvider(secretKey, "app-password");
        string settingsJson = JsonConvert.SerializeObject(new NextcloudConfigModel
        {
            ServerUrl = "https://cloud.example.com",
            LoginName = string.Empty,
            UserId = string.Empty,
            SecretKey = secretKey
        });

        bool isValid = provider.ValidateSettings(settingsJson);

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void CombineRelativePath_NormalizesBackslashesInNameSegment()
    {
        string relativePath = NextcloudClient.CombineRelativePath("ShareX/2026", @"April\cat.png");

        Assert.That(relativePath, Is.EqualTo("ShareX/2026/April/cat.png"));
    }

    [Test]
    public void CombineRelativePath_StripsDotSegmentsBeforeBuildingWebDavPath()
    {
        string relativePath = NextcloudClient.CombineRelativePath("ShareX/../2026", @"./April/../cat.png");

        Assert.That(relativePath, Is.EqualTo("ShareX/2026/April/cat.png"));
    }

    [Test]
    public void ExtractRelativePath_StripsServerBasePathFromAbsoluteHref()
    {
        string relativePath = InvokeExtractRelativePath(
            "https://cloud.example.com/nextcloud/remote.php/dav/files/alice/ShareX/2026/cat.png",
            "/remote.php/dav/files/alice/",
            "alice");

        Assert.That(relativePath, Is.EqualTo("ShareX/2026/cat.png"));
    }

    [Test]
    public void ExtractRelativePath_HandlesEncodedUserIdWithoutLosingRootFolder()
    {
        string relativePath = InvokeExtractRelativePath(
            "/nextcloud/remote.php/dav/files/john%2Fdoe/Screenshots",
            "/remote.php/dav/files/john/doe/",
            "john/doe");

        Assert.That(relativePath, Is.EqualTo("Screenshots"));
    }

    [Test]
    public void ParsePropFindResponse_UsesSuccessfulPropstatWhenEarlierPropertiesFailed()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response>
                <d:href>/remote.php/dav/files/alice/Photos/</d:href>
                <d:propstat>
                  <d:prop>
                    <d:getcontentlength />
                  </d:prop>
                  <d:status>HTTP/1.1 404 Not Found</d:status>
                </d:propstat>
                <d:propstat>
                  <d:prop>
                    <d:displayname>Photos</d:displayname>
                    <d:resourcetype><d:collection /></d:resourcetype>
                    <d:getlastmodified>Mon, 27 Apr 2026 12:00:00 GMT</d:getlastmodified>
                  </d:prop>
                  <d:status>HTTP/1.1 200 OK</d:status>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;

        IReadOnlyList<NextcloudFileEntry> entries = InvokeParsePropFindResponse(xml, "alice", string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Name, Is.EqualTo("Photos"));
            Assert.That(entries[0].RelativePath, Is.EqualTo("Photos"));
            Assert.That(entries[0].IsFolder, Is.True);
            Assert.That(entries[0].SizeBytes, Is.Zero);
            Assert.That(entries[0].ModifiedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task CreateFolderAsync_RejectsBlankFolderNameWithoutUsingCachedSettings()
    {
        const string secretKey = "nextcloud-secret";
        NextcloudProvider provider = CreateProvider(secretKey, "app-password");
        SetLatestSettings(provider, JsonConvert.SerializeObject(new NextcloudConfigModel
        {
            ServerUrl = "https://cloud.example.com",
            LoginName = "alice",
            SecretKey = secretKey
        }));

        bool created = await provider.CreateFolderAsync(string.Empty, "   ");

        Assert.That(created, Is.False);
    }

    [Test]
    public void ParseShareResponse_AcceptsTheOcsV2SuccessCode()
    {
        // ownCloud 10.15.3's reply to the share request (OCS v2 reports success as 200, as Nextcloud does).
        const string v2 = """{"ocs":{"meta":{"status":"ok","statuscode":200,"message":null,"totalitems":"","itemsperpage":""},"data":{"id":"1","share_type":3,"token":"U8uu7QhQ7rWnJ0X","path":"\/ShareX\/2026\/10\/oc-small.txt","url":"http:\/\/127.0.0.1:8090\/s\/U8uu7QhQ7rWnJ0X"}}}""";
        const string v1 = """{"ocs":{"meta":{"status":"ok","statuscode":100,"message":"OK"},"data":{"token":"abc","url":"https://cloud.example.com/s/abc"}}}""";
        const string failure = """{"ocs":{"meta":{"status":"failure","statuscode":403,"message":"You can't share your root folder","totalitems":"","itemsperpage":""},"data":[]}}""";

        Assert.Multiple(() =>
        {
            Assert.That(NextcloudClient.ParseShareResponse(v2)?.Url, Is.EqualTo("http://127.0.0.1:8090/s/U8uu7QhQ7rWnJ0X"));
            Assert.That(NextcloudClient.ParseShareResponse(v1)?.Url, Is.EqualTo("https://cloud.example.com/s/abc"));
            Assert.That(() => NextcloudClient.ParseShareResponse(failure),
                Throws.InvalidOperationException.With.Message.EqualTo("You can't share your root folder"));
        });
    }

    [Test]
    public void ResolveProductName_UsesTheThemingNameThenStatusPhp()
    {
        // ownCloud 10.15.3's status.php; ownCloud has no theming capability.
        const string ownCloudStatus = """{"installed":true,"maintenance":false,"needsDbUpgrade":false,"version":"10.15.3.0","versionstring":"10.15.3","edition":"Community","productname":"ownCloud","product":"ownCloud"}""";

        Assert.Multiple(() =>
        {
            Assert.That(NextcloudClient.ResolveProductName(null, ownCloudStatus), Is.EqualTo("ownCloud"));
            Assert.That(NextcloudClient.ResolveProductName("Company Cloud", ownCloudStatus), Is.EqualTo("Company Cloud"));
            Assert.That(NextcloudClient.ResolveProductName(null, null), Is.EqualTo("Nextcloud"));
            Assert.That(NextcloudClient.ResolveProductName(null, "<html>"), Is.EqualTo("Nextcloud"));
        });
    }

    [Test]
    public void Upload_FailsBeforeNetworkCall_WhenPublicSharesAreUnsupported()
    {
        NextcloudUploader uploader = new(new NextcloudConfigModel
        {
            ServerUrl = "https://127.0.0.1:1",
            LoginName = "alice",
            CreatePublicShare = true,
            SupportsPublicShares = false
        }, "app-password", string.Empty);

        using MemoryStream stream = new("hello nextcloud"u8.ToArray());

        UploadResult result = uploader.Upload(stream, "note.txt");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(uploader.Errors.Errors.Select(error => error.Text),
                Has.Some.EqualTo("This Nextcloud server does not support public shares. Disable public share creation or refresh the server profile."));
            Assert.That(uploader.Errors.Errors.Select(error => error.Text),
                Has.None.Contains("Connection refused").IgnoreCase
                    .And.None.Contains("actively refused").IgnoreCase);
        });
    }

    [Test]
    public void Upload_FailsBeforeNetworkCall_WhenSharePasswordsAreUnsupported()
    {
        NextcloudUploader uploader = new(new NextcloudConfigModel
        {
            ServerUrl = "https://127.0.0.1:1",
            LoginName = "alice",
            CreatePublicShare = true,
            SupportsPublicShares = true,
            SupportsSharePasswords = false
        }, "app-password", "secret-share-password");

        using MemoryStream stream = new("hello nextcloud"u8.ToArray());

        UploadResult result = uploader.Upload(stream, "note.txt");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(uploader.Errors.Errors.Select(error => error.Text),
                Has.Some.EqualTo("This Nextcloud server does not support share passwords. Clear the share password or refresh the server profile."));
            Assert.That(uploader.Errors.Errors.Select(error => error.Text),
                Has.None.Contains("Connection refused").IgnoreCase
                    .And.None.Contains("actively refused").IgnoreCase);
        });
    }

    [Test]
    public void Upload_AllowsNonSeekableStreamsWithoutThrowingNotSupported()
    {
        NextcloudUploader uploader = new(new NextcloudConfigModel
        {
            ServerUrl = "https://127.0.0.1:1",
            LoginName = "alice"
        }, "app-password", string.Empty);

        using NonSeekableReadStream stream = new("hello nextcloud"u8.ToArray());

        UploadResult result = uploader.Upload(stream, "note.txt");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(uploader.Errors.Errors.Select(error => error.Text),
            Has.None.Contains("NotSupportedException").IgnoreCase
                .And.None.Contains("specified method is not supported").IgnoreCase);
    }

    private static NextcloudProvider CreateProvider(string secretKey, string appPassword)
    {
        NextcloudProvider provider = new();
        InMemorySecretStore secrets = new();
        secrets.SetSecret("nextcloud", secretKey, "appPassword", appPassword);
        provider.SetContext(new TestProviderContext(secrets));
        return provider;
    }

    private static void SetLatestSettings(NextcloudProvider provider, string settingsJson)
    {
        FieldInfo? field = typeof(NextcloudProvider).GetField("_latestSettingsJson", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(provider, settingsJson);
    }

    private static string InvokeExtractRelativePath(string href, string hrefPrefix, string userId)
    {
        MethodInfo? method = typeof(NextcloudClient).GetMethod("ExtractRelativePath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        object? result = method!.Invoke(null, new object[] { href, hrefPrefix, userId });
        Assert.That(result, Is.TypeOf<string>());
        return (string)result!;
    }

    private static IReadOnlyList<NextcloudFileEntry> InvokeParsePropFindResponse(string xml, string userId, string requestedPath)
    {
        MethodInfo? method = typeof(NextcloudClient).GetMethod("ParsePropFindResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        object? result = method!.Invoke(null, new object[] { xml, userId, requestedPath });
        Assert.That(result, Is.InstanceOf<IReadOnlyList<NextcloudFileEntry>>());
        return (IReadOnlyList<NextcloudFileEntry>)result!;
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableReadStream(byte[] data)
        {
            _inner = new MemoryStream(data, writable: false);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class TestProviderContext : IProviderContext
    {
        public TestProviderContext(ISecretStore secrets)
        {
            Secrets = secrets;
        }

        public ISecretStore Secrets { get; }
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
}
