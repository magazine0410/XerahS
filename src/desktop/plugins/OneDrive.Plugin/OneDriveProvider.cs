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

namespace ShareX.OneDrive.Plugin;

public sealed class OneDriveProvider : UploaderProviderBase, IInstanceSecretBackupProvider
{
    internal const string Id = "onedrive";
    internal const string ClientIdSecret = "clientId";
    internal const string ClientSecretSecret = "clientSecret";
    internal const string TokenSecret = "oauthToken";
    internal const string AppRegistrationUrl = "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade";
    internal const string MissingClientMessage = "Enter the application (client) ID and client secret of your Microsoft app registration.";

    /// <summary>ShareX's OneDrive authorization: Microsoft identity platform, offline_access and files.readwrite, with PKCE.</summary>
    internal static readonly OAuth2Client OAuth = new()
    {
        ServiceName = "OneDrive",
        AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        Scope = "offline_access files.readwrite",
        UsePkce = true
    };

    public override string ProviderId => Id;
    public override string Name => "OneDrive";
    public override string Description => "Upload files to OneDrive";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.Text, UploaderCategory.File];
    public override Type ConfigModelType => typeof(OneDriveConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        OneDriveConfigModel config = DeserializeConfig(settingsJson);
        return new OneDriveUploader(LoadAuthInfo(config.SecretKey), token => SaveToken(config.SecretKey, token), config);
    }

    // As in ShareX, an authorized account is required.
    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            OneDriveConfigModel config = DeserializeConfig(settingsJson);
            OAuth2Info authInfo = LoadAuthInfo(config.SecretKey);
            return !string.IsNullOrWhiteSpace(authInfo.Client_ID) && !string.IsNullOrEmpty(authInfo.Token?.access_token);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => FileTypes.All();

    public override object? CreateConfigView() => new Views.OneDriveConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.OneDriveConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson) =>
        InstanceSecretHelper.GetReferences(ProviderId, settingsJson, ClientIdSecret, ClientSecretSecret, TokenSecret);

    private OAuth2Info LoadAuthInfo(string secretKey)
    {
        var authInfo = new OAuth2Info(ResolveSecret(secretKey, ClientIdSecret), ResolveSecret(secretKey, ClientSecretSecret));
        string tokenJson = ResolveSecret(secretKey, TokenSecret);
        if (!string.IsNullOrEmpty(tokenJson) && JsonConvert.DeserializeObject<OAuth2Token>(tokenJson) is { } token) authInfo.Token = token;
        return authInfo;
    }

    private void SaveToken(string secretKey, OAuth2Token token)
    {
        if (Secrets != null && !string.IsNullOrWhiteSpace(secretKey))
        {
            Secrets.SetSecret(ProviderId, secretKey, TokenSecret, JsonConvert.SerializeObject(token, Formatting.None));
        }
    }

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static OneDriveConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new OneDriveConfigModel()
            : JsonConvert.DeserializeObject<OneDriveConfigModel>(settingsJson) ?? new OneDriveConfigModel();
}
