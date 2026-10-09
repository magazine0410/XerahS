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

using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ShareX.Vgyme.Plugin;
using XerahS.Common;
using XerahS.Uploaders;
using XerahS.Uploaders.LegacySupport;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Uploaders;

[TestFixture]
[NonParallelizable]
public class ShareXImportSecretsTests
{
    private string _folder = null!;
    private string _previousFolder = null!;
    private IProviderContext? _previousContext;
    private InMemorySecretStore _secrets = null!;

    [SetUp]
    public void SetUp()
    {
        _previousFolder = PathsManager.PersonalFolder;
        _previousContext = ProviderCatalog.GetProviderContext();
        _folder = Path.Combine(Path.GetTempPath(), "xerahs-import-secrets-" + Guid.NewGuid().ToString("N"));
        PathsManager.PersonalFolder = _folder;
        InstanceManager.Instance.ReloadConfiguration();
        _secrets = new InMemorySecretStore();
        ProviderCatalog.Clear();
        ProviderCatalog.RegisterProvider(new VgymeProvider());
        ProviderCatalog.SetProviderContext(new TestProviderContext(_secrets));
    }

    [TearDown]
    public void TearDown()
    {
        ProviderCatalog.Clear();
        if (_previousContext != null) ProviderCatalog.SetProviderContext(_previousContext);
        PathsManager.PersonalFolder = _previousFolder;
        InstanceManager.Instance.ReloadConfiguration();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public void ImportedKey_IsInTheSecretStore_WhereTheUploadReadsIt()
    {
        BuiltinInstanceMigrator.Migrate(new UploadersConfig { VgymeUserKey = "imported-user-key" });

        UploaderInstance instance = InstanceManager.Instance.GetInstancesByCategory(UploaderCategory.Image).Single(i => i.ProviderId == "vgyme");
        string secretKey = JObject.Parse(instance.SettingsJson)["SecretKey"]!.ToString();
        var provider = (VgymeProvider)ProviderCatalog.GetProvider("vgyme")!;
        Assert.Multiple(() =>
        {
            Assert.That(instance.SettingsJson, Does.Not.Contain("imported-user-key"), "the key is not kept as plain text");
            Assert.That(_secrets.GetSecret("vgyme", secretKey, "userKey"), Is.EqualTo("imported-user-key"));
            Assert.That(provider.ValidateSettings(instance.SettingsJson), Is.True, "the destination is set up");
        });
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
