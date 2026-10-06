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

using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using ShareX.AmazonS3.Plugin;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Managers;
using XerahS.Core.Uploaders;
using XerahS.History;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Helpers;

[TestFixture]
[NonParallelizable]
public class PortableSettingsBackupServiceTests
{
    private string _testRoot = null!;
    private string _originalPersonalFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _originalPersonalFolder = SettingsManager.PersonalFolder;
        _testRoot = Path.Combine(Path.GetTempPath(), "XerahS.Tests", "PortableSettings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        ProviderCatalog.Clear();
        ProviderCatalog.RegisterProvider(new AmazonS3Provider());
    }

    [TearDown]
    public void TearDown()
    {
        ProviderContextManager.ResetProviderContext();
        ProviderCatalog.Clear();
        SettingsManager.PersonalFolder = _originalPersonalFolder;
        InstanceManager.Instance.ReloadConfiguration();

        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private const int Tolerance = 1; // 1px tolerance for rounding to nearest pixel
    private const int FileReadRetryCount = 5; // macOS EBUSY/EACCES resilience post-secret-write

    [Test]
    public void FileNaming_IncludesSanitizedComputerNameAndGuaranteesXsbakExtension()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                PortableSettingsBackupService.GetDefaultFileName("0.29.0", "SHAREX-NB1"),
                Is.EqualTo("xerahs-0.29.0-SHAREX-NB1-backup.xsbak"));
            Assert.That(
                PortableSettingsBackupService.GetDefaultFileName("0.29.0", "SHAREX/NB1:*"),
                Is.EqualTo("xerahs-0.29.0-SHAREX-NB1-backup.xsbak"));
            Assert.That(
                PortableSettingsBackupService.NormalizeBackupFilePath(Path.Combine(_testRoot, "portable.zip")),
                Is.EqualTo(Path.Combine(_testRoot, "portable.xsbak")));
            Assert.That(
                PortableSettingsBackupService.NormalizeBackupFilePath(Path.Combine(_testRoot, "portable.xsbak")),
                Is.EqualTo(Path.Combine(_testRoot, "portable.xsbak")));
        });
    }

    /// <summary>
    /// Resilient File.ReadAllText wrapper. On macOS the kernel can briefly
    /// delay handle-release on a just-closed writer; read failures (EBUSY,
    /// EACCES, ENOENT) inside the retry window are retried before the
    /// underlying IOException is propagated. Behaviour on Linux/Windows
    /// is unchanged because the read either succeeds on first try or
    /// throws an unrecoverable error after the retry budget.
    /// When the path does not exist (e.g., secrets backed by macOS Keychain
    /// rather than the AES file store), the empty string is returned, which
    /// satisfies the surrounding "Does.Not.Contain(plaintextSecret)" assertion
    /// because a missing file trivially contains nothing.
    /// </summary>
    private static string ReadAllTextWithRetry(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        Exception? lastError = null;
        for (int attempt = 0; attempt < FileReadRetryCount; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex) when (attempt < FileReadRetryCount - 1 && (ex is IOException || ex is UnauthorizedAccessException))
            {
                lastError = ex;
                Thread.Sleep(50);
            }
        }

        throw lastError ?? new IOException($"ReadAllText failed after {FileReadRetryCount} attempts: {path}");
    }

    [Test]
    public void CreateAndRestore_RoundTripsSettingsDestinationAndPlaintextS3SecretsAcrossRoots()
    {
        const string accessKey = "AKIA_PORTABLE_TEST_123";
        const string secretAccessKey = "portable-secret-access-key-value";
        const string secretKey = "s3-portable-reference";
        string sourceRoot = Path.Combine(_testRoot, "source");
        string targetRoot = Path.Combine(_testRoot, "target");
        string archivePath = Path.Combine(_testRoot, "settings.xsbak");

        InitializeRoot(sourceRoot);
        SettingsManager.Settings.ShowTray = false;
        SettingsManager.Settings.CustomUploadersConfigPath = Path.Combine(sourceRoot, "external-uploaders");
        SettingsManager.Settings.CustomWorkflowsConfigPath = Path.Combine(sourceRoot, "external-workflows");
        Directory.CreateDirectory(SettingsManager.Settings.CustomUploadersConfigPath);
        Directory.CreateDirectory(SettingsManager.Settings.CustomWorkflowsConfigPath);

        var instance = new UploaderInstance
        {
            InstanceId = "portable-s3",
            ProviderId = "amazons3",
            Category = UploaderCategory.File,
            DisplayName = "Portable S3",
            IsAvailable = true,
            SettingsJson = $$"""
            {
              "AuthMode": 0,
              "SecretKey": "{{secretKey}}",
              "BucketName": "portable-bucket",
              "Region": "ap-southeast-2"
            }
            """
        };
        InstanceManager.Instance.AddInstance(instance);
        InstanceManager.Instance.SetDefaultInstance(UploaderCategory.File, instance.InstanceId);

        ISecretStore sourceSecrets = ProviderContextManager.EnsureProviderContext().Secrets;
        sourceSecrets.SetSecret("amazons3", secretKey, "accessKeyId", accessKey);
        sourceSecrets.SetSecret("amazons3", secretKey, "secretAccessKey", secretAccessKey);
        File.WriteAllText(Path.Combine(SettingsManager.SettingsFolder, "ReClipConfig.json"), "{\"enabled\":true}");

        PortableSettingsBackupResult created = PortableSettingsBackupService.Create(archivePath);

        Assert.Multiple(() =>
        {
            Assert.That(created.SecretCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(File.Exists(archivePath), Is.True);
            Assert.That(ReadArchiveEntry(archivePath, "settings/secrets.json"), Does.Contain(accessKey));
            Assert.That(ReadArchiveEntry(archivePath, "settings/secrets.json"), Does.Contain(secretAccessKey));
            Assert.That(ReadAllTextWithRetry(SettingsManager.SecretsStoreFilePath), Does.Not.Contain(secretAccessKey));
        });

        InitializeRoot(targetRoot);
        SettingsManager.Settings.ShowTray = true;
        SettingsManager.SaveApplicationConfig();

        PortableSettingsRestoreResult restored = PortableSettingsBackupService.Restore(archivePath);
        ISecretStore targetSecrets = ProviderContextManager.EnsureProviderContext().Secrets;
        UploaderInstance? restoredInstance = InstanceManager.Instance.GetInstance(instance.InstanceId);

        Assert.Multiple(() =>
        {
            Assert.That(restored.SecretCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(SettingsManager.Settings.ShowTray, Is.False);
            Assert.That(SettingsManager.Settings.CustomUploadersConfigPath, Is.Empty);
            Assert.That(SettingsManager.Settings.CustomWorkflowsConfigPath, Is.Empty);
            Assert.That(restoredInstance, Is.Not.Null);
            Assert.That(restoredInstance!.DisplayName, Is.EqualTo("Portable S3"));
            Assert.That(InstanceManager.Instance.GetDefaultInstance(UploaderCategory.File)?.InstanceId, Is.EqualTo(instance.InstanceId));
            Assert.That(targetSecrets.GetSecret("amazons3", secretKey, "accessKeyId"), Is.EqualTo(accessKey));
            Assert.That(targetSecrets.GetSecret("amazons3", secretKey, "secretAccessKey"), Is.EqualTo(secretAccessKey));
            Assert.That(ReadAllTextWithRetry(SettingsManager.SecretsStoreFilePath), Does.Not.Contain(secretAccessKey));
            Assert.That(File.Exists(Path.Combine(SettingsManager.SettingsFolder, "ReClipConfig.json")), Is.True);
        });
    }

    [Test]
    public void Restore_WhenPayloadHashDoesNotMatch_DoesNotModifyCurrentSettings()
    {
        string sourceRoot = Path.Combine(_testRoot, "source-corrupt");
        string targetRoot = Path.Combine(_testRoot, "target-corrupt");
        string archivePath = Path.Combine(_testRoot, "corrupt.xsbak");

        InitializeRoot(sourceRoot);
        SettingsManager.Settings.ShowTray = false;
        PortableSettingsBackupService.Create(archivePath);

        using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = zip.GetEntry("settings/application.json")!;
            entry.Delete();
            ZipArchiveEntry replacement = zip.CreateEntry("settings/application.json");
            using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
            writer.Write("{\"ShowTray\":false,\"tampered\":true}");
        }

        InitializeRoot(targetRoot);
        SettingsManager.Settings.ShowTray = true;
        SettingsManager.SaveApplicationConfig();

        Assert.Throws<InvalidDataException>(() => PortableSettingsBackupService.Restore(archivePath));
        SettingsManager.LoadApplicationConfig(fallbackSupport: false);
        Assert.That(SettingsManager.Settings.ShowTray, Is.True);
    }

    [Test]
    public void HistoryOnlyBackup_ReplacesTheOpenHistoryAndLeavesTheSettings()
    {
        string sourceRoot = Path.Combine(_testRoot, "source");
        string targetRoot = Path.Combine(_testRoot, "target");
        string archivePath = Path.Combine(_testRoot, "history.xsbak");

        InitializeRoot(sourceRoot);
        using (var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath()))
        {
            history.AppendHistoryItem(new HistoryItem { FileName = "source.png", FilePath = "/captures/source.png", DateTime = DateTime.Now, Type = "Image" });
        }

        PortableSettingsBackupService.Create(archivePath, includeSettings: false, includeHistory: true);
        using (ZipArchive zip = ZipFile.OpenRead(archivePath))
        {
            Assert.That(zip.GetEntry("history/History.db"), Is.Not.Null);
            Assert.That(zip.GetEntry("settings/application.json"), Is.Null, "As in ShareX, Settings off leaves the settings out.");
        }

        InitializeRoot(targetRoot);
        SettingsManager.Settings.ShowTray = false;
        SettingsManager.SaveApplicationConfig();
        // XerahS keeps the history open while it runs.
        using var openHistory = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        openHistory.AppendHistoryItem(new HistoryItem { FileName = "target.png", FilePath = "/captures/target.png", DateTime = DateTime.Now, Type = "Image" });

        PortableSettingsRestoreResult restored = PortableSettingsBackupService.Restore(archivePath);

        Assert.Multiple(() =>
        {
            Assert.That(restored.RestoredHistory, Is.True);
            Assert.That(restored.RestoredSettings, Is.False);
            Assert.That(openHistory.GetHistoryItems().Select(item => item.FileName), Is.EqualTo(new[] { "source.png" }));
            Assert.That(SettingsManager.Settings.ShowTray, Is.False, "A history backup does not replace the settings.");
        });
    }

    [Test]
    public void SettingsAndHistoryBackup_ContainsBoth_AndNeitherIsRefused()
    {
        string archivePath = Path.Combine(_testRoot, "both.xsbak");
        InitializeRoot(Path.Combine(_testRoot, "source"));
        using (var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath()))
        {
            history.AppendHistoryItem(new HistoryItem { FileName = "both.png", FilePath = "/captures/both.png", DateTime = DateTime.Now, Type = "Image" });
        }

        PortableSettingsBackupService.Create(archivePath, includeSettings: true, includeHistory: true);
        using (ZipArchive zip = ZipFile.OpenRead(archivePath))
        {
            Assert.That(zip.GetEntry("history/History.db"), Is.Not.Null);
            Assert.That(zip.GetEntry("settings/application.json"), Is.Not.Null);
        }

        PortableSettingsRestoreResult restored = PortableSettingsBackupService.Restore(archivePath);
        Assert.That(restored.RestoredSettings && restored.RestoredHistory, Is.True);
        Assert.Throws<ArgumentException>(() => PortableSettingsBackupService.Create(
            Path.Combine(_testRoot, "none.xsbak"), includeSettings: false, includeHistory: false));
    }

    [Test]
    public void Restore_RejectsADamagedHistory_WithoutChangingTheCurrentOne()
    {
        string archivePath = Path.Combine(_testRoot, "damaged.xsbak");
        InitializeRoot(Path.Combine(_testRoot, "source"));
        using (var history = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath()))
        {
            history.AppendHistoryItem(new HistoryItem { FileName = "kept.png", FilePath = "/captures/kept.png", DateTime = DateTime.Now, Type = "Image" });
        }
        PortableSettingsBackupService.Create(archivePath, includeSettings: false, includeHistory: true);

        // Replace the database with other bytes and update the manifest so only the database check can catch it.
        byte[] damaged = Encoding.UTF8.GetBytes("not a database");
        using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            zip.GetEntry("history/History.db")!.Delete();
            using (var stream = zip.CreateEntry("history/History.db").Open()) stream.Write(damaged);
            var manifestEntry = zip.GetEntry("manifest.json")!;
            string manifest;
            using (var reader = new StreamReader(manifestEntry.Open())) manifest = reader.ReadToEnd();
            var json = Newtonsoft.Json.Linq.JObject.Parse(manifest);
            var file = json["Files"]!.First(f => (string?)f["Path"] == "history/History.db");
            file["Length"] = damaged.Length;
            file["Sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(damaged));
            manifestEntry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write(json.ToString());
        }

        Assert.Throws<InvalidDataException>(() => PortableSettingsBackupService.Restore(archivePath));
        using var current = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.That(current.GetHistoryItems().Select(item => item.FileName), Is.EqualTo(new[] { "kept.png" }));
    }

    private static void InitializeRoot(string personalFolder)
    {
        SettingsManager.PersonalFolder = personalFolder;
        InstanceManager.Instance.ReloadConfiguration();
        ProviderContextManager.ResetProviderContext();
        SettingsManager.LoadInitialSettings();
        InstanceManager.Instance.ImportConfigurationJson(JsonConvert.SerializeObject(new InstanceConfiguration()));
    }

    private static string ReadArchiveEntry(string archivePath, string entryName)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry entry = archive.GetEntry(entryName)!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
