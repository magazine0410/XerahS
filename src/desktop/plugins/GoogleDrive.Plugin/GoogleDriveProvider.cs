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

namespace ShareX.GoogleDrive.Plugin;

public sealed class GoogleDriveProvider : UploaderProviderBase, IInstanceSecretBackupProvider
{
    internal const string Id = "googledrive";
    internal const string ClientIdSecret = "clientId";
    internal const string ClientSecretSecret = "clientSecret";
    internal const string TokenSecret = "oauthToken";
    internal const string AppRegistrationUrl = "https://console.cloud.google.com/apis/credentials";
    internal const string MissingClientMessage = "Enter the client ID and client secret of your Google OAuth client.";

    /// <summary>ShareX's GoogleOAuth2 with the drive.file scope. access_type=offline and prompt=consent make Google return a refresh token.</summary>
    internal static readonly OAuth2Client OAuth = new()
    {
        ServiceName = "Google Drive",
        AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint = "https://oauth2.googleapis.com/token",
        Scope = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/userinfo.profile",
        UsePkce = true,
        AuthorizationArguments = new() { ["access_type"] = "offline", ["prompt"] = "consent" }
    };

    public override string ProviderId => Id;
    public override string Name => "Google Drive";
    public override string Description => "Upload files to Google Drive";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.Text, UploaderCategory.File];
    public override Type ConfigModelType => typeof(GoogleDriveConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        GoogleDriveConfigModel config = DeserializeConfig(settingsJson);
        return new GoogleDriveUploader(LoadAuthInfo(config.SecretKey), token => SaveToken(config.SecretKey, token), config);
    }

    // As in ShareX, an authorized account is required.
    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            GoogleDriveConfigModel config = DeserializeConfig(settingsJson);
            OAuth2Info authInfo = LoadAuthInfo(config.SecretKey);
            return !string.IsNullOrWhiteSpace(authInfo.Client_ID) && !string.IsNullOrEmpty(authInfo.Token?.access_token);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => FileTypes.All();

    public override object? CreateConfigView() => new Views.GoogleDriveConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.GoogleDriveConfigViewModel();

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

    internal static GoogleDriveConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new GoogleDriveConfigModel()
            : JsonConvert.DeserializeObject<GoogleDriveConfigModel>(settingsJson) ?? new GoogleDriveConfigModel();
}
