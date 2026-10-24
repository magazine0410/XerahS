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

using NUnit.Framework;
using XerahS.Core.Security;

namespace XerahS.Tests.Core;

[TestFixture]
public class SecretStoreKeyringTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("xerahs-secret-keyring-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void KeyringReads_KeepLeadingAndTrailingSpaces()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SecretStore.LinuxLibsecretSecretStore.ReadLookupOutput(" token with spaces \n"), Is.EqualTo(" token with spaces \n"));
            Assert.That(SecretStore.LinuxLibsecretSecretStore.ReadLookupOutput("   "), Is.EqualTo("   "));
            Assert.That(SecretStore.LinuxLibsecretSecretStore.ReadLookupOutput(""), Is.Null);
        });
    }

    [Test]
    public void MovingTheFileStore_KeepsASecretWithSurroundingSpaces()
    {
        string filePath = Path.Combine(_directory, "SecretsStore.json");
        new SecretStore.AesFileSecretStore(filePath, Path.Combine(_directory, "SecretsStore.key")).SetSecret("p", "k", "n", " padded ");
        var keyring = new FakeKeyring();

        Assert.That(SecretStore.MoveFileSecretsToKeyring(filePath, keyring), Is.True);
        Assert.That(keyring.Values["p:k:n"], Is.EqualTo(" padded "));
    }

    internal sealed class FakeKeyring : SecretStore.IKeyringAccounts
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public bool RefuseWrites { get; init; }

        public string? GetSecret(string account) => Values.TryGetValue(account, out string? value) ? value : null;

        public void SetSecret(string account, string value)
        {
            if (!RefuseWrites) Values[account] = value;
        }
    }
}
