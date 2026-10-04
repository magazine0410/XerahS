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

namespace ShareX.ImageShack.Plugin;

public sealed class ImageShackProvider : UploaderProviderBase, IInstanceSecretBackupProvider, IInstanceSecretMigrator
{
    internal const string Id = "imageshack";
    internal const string ApiKeySecret = "apiKey";
    internal const string PasswordSecret = "password";
    internal const string AuthTokenSecret = "authToken";

    public override string ProviderId => Id;
    public override string Name => "ImageShack";
    public override string Description => "Upload images to ImageShack";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image];
    public override Type ConfigModelType => typeof(ImageShackConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        ImageShackConfigModel config = DeserializeConfig(settingsJson);
        return new ImageShackUploader(config, ResolveSecret(config.SecretKey, ApiKeySecret), ResolveSecret(config.SecretKey, AuthTokenSecret));
    }

    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            ImageShackConfigModel config = DeserializeConfig(settingsJson);
            return GetConfigError(ResolveSecret(config.SecretKey, ApiKeySecret), ResolveSecret(config.SecretKey, AuthTokenSecret)) == null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// As in ShareX, uploads need an auth token from logging in. ShareX's release builds include its own API key;
    /// XerahS has none, so the user enters one.
    /// </summary>
    internal static string? GetConfigError(string? apiKey, string? authToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "Enter your ImageShack API key (request one at https://imageshack.com/contact/api).";
        if (string.IsNullOrWhiteSpace(authToken)) return "Log in to ImageShack in the destination settings.";
        return null;
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new()
    {
        [UploaderCategory.Image] = ["png", "jpg", "jpeg", "gif", "bmp", "tiff", "tif"]
    };

    public override object? CreateConfigView() => new Views.ImageShackConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.ImageShackConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson)
    {
        string? secretKey;
        try
        {
            secretKey = string.IsNullOrWhiteSpace(settingsJson) ? null : JObject.Parse(settingsJson).Value<string>(nameof(ImageShackConfigModel.SecretKey));
        }
        catch (JsonException)
        {
            return [];
        }

        return string.IsNullOrWhiteSpace(secretKey)
            ? []
            : [new(ProviderId, secretKey, ApiKeySecret), new(ProviderId, secretKey, PasswordSecret), new(ProviderId, secretKey, AuthTokenSecret)];
    }

    public bool TryMigrateSecrets(string settingsJson, ISecretStore secrets, out string updatedSettingsJson, out int migratedSecretCount)
    {
        updatedSettingsJson = settingsJson;
        migratedSecretCount = 0;
        JObject json;
        try { json = JObject.Parse(settingsJson); }
        catch (JsonException) { return false; }

        string secretKey = json.Value<string>(nameof(ImageShackConfigModel.SecretKey)) is { Length: > 0 } key ? key : Guid.NewGuid().ToString("N");
        bool changed = false;
        foreach ((string field, string secret) in new[] { (nameof(ImageShackConfigModel.Password), PasswordSecret), (nameof(ImageShackConfigModel.AuthToken), AuthTokenSecret) })
        {
            string? value = json.Value<string>(field);
            if (string.IsNullOrEmpty(value)) continue;
            secrets.SetSecret(ProviderId, secretKey, secret, value);
            if (!secrets.HasSecret(ProviderId, secretKey, secret)) continue;
            json.Remove(field);
            migratedSecretCount++;
            changed = true;
        }

        if (!changed) return false;
        json[nameof(ImageShackConfigModel.SecretKey)] = secretKey;
        updatedSettingsJson = json.ToString(Formatting.Indented);
        return true;
    }

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static ImageShackConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new ImageShackConfigModel()
            : JsonConvert.DeserializeObject<ImageShackConfigModel>(settingsJson) ?? new ImageShackConfigModel();
}
