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

namespace ShareX.Sul.Plugin;

public sealed class SulProvider : UploaderProviderBase, IInstanceSecretBackupProvider, IInstanceSecretMigrator
{
    internal const string Id = "sul";
    internal const string ApiKeySecret = "apiKey";
    internal const string MissingApiKeyMessage = "Enter your s-ul API key (shown in your s-ul account).";

    public override string ProviderId => Id;
    public override string Name => "s-ul";
    public override string Description => "Upload files to s-ul";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.Text, UploaderCategory.File];
    public override Type ConfigModelType => typeof(SulConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        SulConfigModel config = DeserializeConfig(settingsJson);
        return new SulUploader(ResolveSecret(config.SecretKey, ApiKeySecret));
    }

    // As in ShareX, the API key is required.
    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(ResolveSecret(DeserializeConfig(settingsJson).SecretKey, ApiKeySecret));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => FileTypes.All();

    public override object? CreateConfigView() => new Views.SulConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.SulConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson) =>
        InstanceSecretHelper.GetReferences(ProviderId, settingsJson, ApiKeySecret);

    public bool TryMigrateSecrets(string settingsJson, ISecretStore secrets, out string updatedSettingsJson, out int migratedSecretCount) =>
        InstanceSecretHelper.MigratePlaintext(settingsJson, secrets, ProviderId,
            new Dictionary<string, string> { [nameof(SulConfigModel.APIKey)] = ApiKeySecret }, out updatedSettingsJson, out migratedSecretCount);

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static SulConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new SulConfigModel()
            : JsonConvert.DeserializeObject<SulConfigModel>(settingsJson) ?? new SulConfigModel();
}
