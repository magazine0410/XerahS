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
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.BackblazeB2.Plugin;

public sealed class BackblazeB2Provider : UploaderProviderBase, IInstanceSecretBackupProvider, IInstanceSecretMigrator
{
    internal const string Id = "backblazeb2";
    internal const string KeyIdSecret = "applicationKeyId";
    internal const string KeySecret = "applicationKey";
    internal const string MissingKeyMessage = "Enter your B2 application key ID and application key.";

    public override string ProviderId => Id;
    public override string Name => "Backblaze B2";
    public override string Description => "Upload files to a Backblaze B2 bucket";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.Text, UploaderCategory.File];
    public override Type ConfigModelType => typeof(BackblazeB2ConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        BackblazeB2ConfigModel config = DeserializeConfig(settingsJson);
        return new BackblazeB2Uploader(ResolveSecret(config.SecretKey, KeyIdSecret), ResolveSecret(config.SecretKey, KeySecret), config);
    }

    // As in ShareX, the application key ID and key are required.
    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            BackblazeB2ConfigModel config = DeserializeConfig(settingsJson);
            return !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, KeyIdSecret)) &&
                !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, KeySecret));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => FileTypes.All();

    public override object? CreateConfigView() => new Views.BackblazeB2ConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.BackblazeB2ConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson) =>
        InstanceSecretHelper.GetReferences(ProviderId, settingsJson, KeyIdSecret, KeySecret);

    public bool TryMigrateSecrets(string settingsJson, ISecretStore secrets, out string updatedSettingsJson, out int migratedSecretCount) =>
        InstanceSecretHelper.MigratePlaintext(settingsJson, secrets, ProviderId, new Dictionary<string, string>
        {
            [nameof(BackblazeB2ConfigModel.ApplicationKeyId)] = KeyIdSecret,
            [nameof(BackblazeB2ConfigModel.ApplicationKey)] = KeySecret
        }, out updatedSettingsJson, out migratedSecretCount);

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static BackblazeB2ConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new BackblazeB2ConfigModel()
            : JsonConvert.DeserializeObject<BackblazeB2ConfigModel>(settingsJson) ?? new BackblazeB2ConfigModel();
}
