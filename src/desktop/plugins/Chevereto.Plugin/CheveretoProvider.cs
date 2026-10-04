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
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Chevereto.Plugin;

public sealed class CheveretoProvider : UploaderProviderBase, IInstanceSecretBackupProvider, IInstanceSecretMigrator
{
    internal const string Id = "chevereto";
    internal const string ApiKeySecret = "apiKey";

    public override string ProviderId => Id;
    public override string Name => "Chevereto";
    public override string Description => "Upload images to a Chevereto server";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image];
    public override Type ConfigModelType => typeof(CheveretoConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        CheveretoConfigModel config = DeserializeConfig(settingsJson);
        return new CheveretoUploader(config, ResolveSecret(config.SecretKey, ApiKeySecret));
    }

    public override bool ValidateSettings(string settingsJson)
    {
        CheveretoConfigModel config;
        try { config = DeserializeConfig(settingsJson); }
        catch (JsonException) { return false; }
        return GetConfigError(config, ResolveSecret(config.SecretKey, ApiKeySecret)) == null;
    }

    /// <summary>As in ShareX, Chevereto needs the upload URL and the API key.</summary>
    internal static string? GetConfigError(CheveretoConfigModel config, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(config.UploadURL)) return "Enter the Chevereto upload URL, for example https://example.com/api/1/upload.";
        if (string.IsNullOrWhiteSpace(apiKey)) return "Enter the Chevereto API key.";
        return null;
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new()
    {
        [UploaderCategory.Image] = ["png", "jpg", "jpeg", "gif", "bmp", "webp", "tiff", "tif", "avif", "heic"]
    };

    public override object? CreateConfigView() => new Views.CheveretoConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.CheveretoConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson)
    {
        string? secretKey = ReadSecretKey(settingsJson);
        return string.IsNullOrWhiteSpace(secretKey) ? [] : [new(ProviderId, secretKey, ApiKeySecret)];
    }

    public bool TryMigrateSecrets(string settingsJson, ISecretStore secrets, out string updatedSettingsJson, out int migratedSecretCount)
    {
        updatedSettingsJson = settingsJson;
        migratedSecretCount = 0;
        JObject json;
        try { json = JObject.Parse(settingsJson); }
        catch (JsonException) { return false; }

        string? apiKey = json.Value<string>(nameof(CheveretoConfigModel.APIKey));
        if (string.IsNullOrEmpty(apiKey)) return false;

        string secretKey = json.Value<string>(nameof(CheveretoConfigModel.SecretKey)) is { Length: > 0 } key ? key : Guid.NewGuid().ToString("N");
        secrets.SetSecret(ProviderId, secretKey, ApiKeySecret, apiKey);
        if (!secrets.HasSecret(ProviderId, secretKey, ApiKeySecret)) return false;

        json[nameof(CheveretoConfigModel.SecretKey)] = secretKey;
        json.Remove(nameof(CheveretoConfigModel.APIKey));
        updatedSettingsJson = json.ToString(Formatting.Indented);
        migratedSecretCount = 1;
        return true;
    }

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    private static string? ReadSecretKey(string settingsJson)
    {
        try
        {
            return string.IsNullOrWhiteSpace(settingsJson) ? null : JObject.Parse(settingsJson).Value<string>(nameof(CheveretoConfigModel.SecretKey));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static CheveretoConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new CheveretoConfigModel()
            : JsonConvert.DeserializeObject<CheveretoConfigModel>(settingsJson) ?? new CheveretoConfigModel();
}
