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

namespace ShareX.Upaste.Plugin;

public sealed class UpasteProvider : UploaderProviderBase, IInstanceSecretBackupProvider, IInstanceSecretMigrator
{
    internal const string Id = "upaste";
    internal const string UserKeySecret = "userKey";
    internal const string AccountSettingsUrl = "https://upaste.me/account/settings";
    internal const string MissingUserKeyMessage = "Enter your uPaste user key (created in your account settings: " + AccountSettingsUrl + ").";

    public override string ProviderId => Id;
    public override string Name => "uPaste";
    public override string Description => "Upload text to uPaste";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Text];
    public override Type ConfigModelType => typeof(UpasteConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        UpasteConfigModel config = DeserializeConfig(settingsJson);
        return new UpasteUploader(ResolveSecret(config.SecretKey, UserKeySecret), config.IsPublic);
    }

    // ShareX treats the user key as optional, but uPaste's API now answers 401 to every request without a key.
    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            UpasteConfigModel config = DeserializeConfig(settingsJson);
            return !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, UserKeySecret));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new()
    {
        [UploaderCategory.Text] = ["txt", "log", "json", "xml", "md", "html", "css", "js", "cs", "py", "sql"]
    };

    public override object? CreateConfigView() => new Views.UpasteConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.UpasteConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson)
    {
        string? secretKey = ReadSecretKey(settingsJson);
        return string.IsNullOrWhiteSpace(secretKey) ? [] : [new(ProviderId, secretKey, UserKeySecret)];
    }

    public bool TryMigrateSecrets(string settingsJson, ISecretStore secrets, out string updatedSettingsJson, out int migratedSecretCount)
    {
        updatedSettingsJson = settingsJson;
        migratedSecretCount = 0;
        JObject json;
        try { json = JObject.Parse(settingsJson); }
        catch (JsonException) { return false; }

        string? userKey = json.Value<string>(nameof(UpasteConfigModel.UserKey));
        if (string.IsNullOrEmpty(userKey)) return false;

        string secretKey = json.Value<string>(nameof(UpasteConfigModel.SecretKey)) is { Length: > 0 } key ? key : Guid.NewGuid().ToString("N");
        secrets.SetSecret(ProviderId, secretKey, UserKeySecret, userKey);
        if (!secrets.HasSecret(ProviderId, secretKey, UserKeySecret)) return false;

        json[nameof(UpasteConfigModel.SecretKey)] = secretKey;
        json.Remove(nameof(UpasteConfigModel.UserKey));
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
            return string.IsNullOrWhiteSpace(settingsJson) ? null : JObject.Parse(settingsJson).Value<string>(nameof(UpasteConfigModel.SecretKey));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static UpasteConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new UpasteConfigModel()
            : JsonConvert.DeserializeObject<UpasteConfigModel>(settingsJson) ?? new UpasteConfigModel();
}
