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

namespace ShareX.Pushbullet.Plugin;

public sealed class PushbulletProvider : UploaderProviderBase, IInstanceSecretBackupProvider, IInstanceSecretMigrator
{
    internal const string Id = "pushbullet";
    internal const string AccessTokenSecret = "accessToken";
    internal const string AccountSettingsUrl = "https://www.pushbullet.com/#settings/account";
    internal const string MissingAccessTokenMessage = "Enter your Pushbullet access token (created in your Pushbullet account settings).";
    internal const string NoDeviceMessage = "Load your Pushbullet devices and choose one.";

    public override string ProviderId => Id;
    public override string Name => "Pushbullet";
    public override string Description => "Push files to a Pushbullet device";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.Text, UploaderCategory.File];
    public override Type ConfigModelType => typeof(PushbulletConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        PushbulletConfigModel config = DeserializeConfig(settingsJson);
        return new PushbulletUploader(ResolveSecret(config.SecretKey, AccessTokenSecret), config.CurrentDevice?.Key);
    }

    // As in ShareX, the access token and a device from the device list are required.
    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            PushbulletConfigModel config = DeserializeConfig(settingsJson);
            return !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, AccessTokenSecret)) && config.CurrentDevice != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => FileTypes.All();

    public override object? CreateConfigView() => new Views.PushbulletConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.PushbulletConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson) =>
        InstanceSecretHelper.GetReferences(ProviderId, settingsJson, AccessTokenSecret);

    public bool TryMigrateSecrets(string settingsJson, ISecretStore secrets, out string updatedSettingsJson, out int migratedSecretCount) =>
        InstanceSecretHelper.MigratePlaintext(settingsJson, secrets, ProviderId,
            new Dictionary<string, string> { [nameof(PushbulletConfigModel.UserAPIKey)] = AccessTokenSecret }, out updatedSettingsJson, out migratedSecretCount);

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static PushbulletConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new PushbulletConfigModel()
            : JsonConvert.DeserializeObject<PushbulletConfigModel>(settingsJson) ?? new PushbulletConfigModel();
}
