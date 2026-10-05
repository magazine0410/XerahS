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
using XerahS.Uploaders;

namespace XerahS.Tests.Uploaders;

// Importing ShareX's UploadersConfig.json, whose [JsonEncrypt] values are "$DPAPIEncrypted$" and Windows DPAPI data.
[TestFixture]
public class UploadersConfigImporterTests
{
    private string _path = null!;

    [SetUp]
    public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "xerahs-import-" + Guid.NewGuid().ToString("N") + ".json");

    [TearDown]
    public void TearDown() => File.Delete(_path);

    [Test]
    public void PlainValues_AreImported_AsShareXReadsThem()
    {
        File.WriteAllText(_path, """{"SulAPIKey":"plain-key"}""");
        var target = new UploadersConfig();

        ImportResult result = UploadersConfigImporter.ImportFromFile(_path, target);

        Assert.That(target.SulAPIKey, Is.EqualTo("plain-key"));
        Assert.That(result.ImportedUploaders, Does.Contain("s-ul"));
        Assert.That(result.UndecryptableValues, Is.Empty);
    }

    [Test]
    public void EncryptedValuesThatCannotBeDecrypted_AreLeftEmpty_AndListed()
    {
        // Not valid DPAPI data on any system, as on Linux for every encrypted value.
        File.WriteAllText(_path, """
            {"SulAPIKey":"$DPAPIEncrypted$AQAAAA==","PushbulletSettings":{"UserAPIKey":"$DPAPIEncrypted$AQAAAA==","DeviceList":[]},"UpasteUserKey":"paste"}
            """);
        var target = new UploadersConfig();

        ImportResult result = UploadersConfigImporter.ImportFromFile(_path, target);

        Assert.Multiple(() =>
        {
            Assert.That(target.SulAPIKey, Is.Empty);
            Assert.That(result.ImportedUploaders, Does.Not.Contain("s-ul").And.Not.Contain("Pushbullet").And.Contain("uPaste"));
            Assert.That(result.UndecryptableValues, Is.EqualTo(new[] { "SulAPIKey", "PushbulletSettings.UserAPIKey" }));
            Assert.That(result.GetSummary(), Does.Contain("- SulAPIKey").And.Contain("Enter them again"));
        });
    }
}
