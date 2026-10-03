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

namespace ShareX.Email.Plugin;

/// <summary>ShareX's Email URL sharing service.</summary>
public sealed class EmailProvider : UploaderProviderBase, IInstanceSecretBackupProvider
{
    internal const string Id = "email";

    public override string ProviderId => Id;
    public override string Name => "Email";
    public override string Description => "Share the URL by email over SMTP";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.UrlSharing];
    public override Type ConfigModelType => typeof(EmailConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation;

    public override Uploader CreateInstance(string settingsJson)
    {
        EmailConfigModel config = DeserializeConfig(settingsJson);
        return new EmailSharer(config, ResolveSecret(config.SecretKey, EmailSharer.PasswordSecret), Secrets);
    }

    public override bool ValidateSettings(string settingsJson)
    {
        EmailConfigModel config;
        try
        {
            config = DeserializeConfig(settingsJson);
        }
        catch (JsonException)
        {
            return false;
        }

        return EmailSharer.GetConfigError(config, ResolveSecret(config.SecretKey, EmailSharer.PasswordSecret)) == null;
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() =>
        new() { [UploaderCategory.UrlSharing] = [] };

    public override object? CreateConfigView() => new Views.EmailConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.EmailConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson)
    {
        string? secretKey;
        try
        {
            secretKey = string.IsNullOrWhiteSpace(settingsJson) ? null : JObject.Parse(settingsJson).Value<string>(nameof(EmailConfigModel.SecretKey));
        }
        catch (JsonException)
        {
            return [];
        }

        return string.IsNullOrWhiteSpace(secretKey)
            ? []
            : [new(ProviderId, secretKey, EmailSharer.PasswordSecret), new(ProviderId, secretKey, EmailSharer.LastToSecret)];
    }

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static EmailConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new EmailConfigModel()
            : JsonConvert.DeserializeObject<EmailConfigModel>(settingsJson) ?? new EmailConfigModel();
}
