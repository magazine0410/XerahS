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

using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using XerahS.Common;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.YouTube.Plugin.ViewModels;

public partial class YouTubeConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _clientId = string.Empty;
    [ObservableProperty] private string _clientSecret = string.Empty;
    [ObservableProperty] private string _redirectUri = YouTubeConfigModel.DefaultRedirectUri;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private YouTubeVideoPrivacy _privacyType = YouTubeVideoPrivacy.Public;
    [ObservableProperty] private bool _useShortenedLink;

    public IReadOnlyList<YouTubeVideoPrivacy> PrivacyTypes { get; } = Enum.GetValues<YouTubeVideoPrivacy>();

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;
    private CancellationTokenSource? _loginCts;

    public string ConnectionStatus => IsConnected ? "Connected" : "Not connected";

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectionStatus));

    [RelayCommand]
    private static void OpenAppRegistration() => URLHelpers.OpenURL(YouTubeProvider.AppRegistrationUrl);

    /// <summary>Opens the authorization page and receives the code on the local redirect URI.</summary>
    [RelayCommand]
    private async Task AuthorizeAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            StatusMessage = YouTubeProvider.MissingClientMessage;
            return;
        }

        string? redirectError = OAuthLoopbackListener.ValidateRedirectUri(RedirectUri, out Uri? redirectUri);
        if (redirectError != null)
        {
            StatusMessage = redirectError;
            return;
        }

        PersistClient();
        IsBusy = true;
        StatusMessage = "Complete the login in your browser. Waiting for the authorization...";
        _loginCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var authInfo = new OAuth2Info(ClientId.Trim(), ClientSecret.Trim());
            var uploader = new YouTubeUploader(authInfo, null, new YouTubeConfigModel());
            OAuth2Token token = await YouTubeProvider.OAuth.LoginWithBrowserAsync(uploader, authInfo, redirectUri!, URLHelpers.OpenURL, _loginCts.Token);
            _secrets?.SetSecret(YouTubeProvider.Id, _secretKey, YouTubeProvider.TokenSecret, JsonConvert.SerializeObject(token, Formatting.None));
            IsConnected = true;
            StatusMessage = "Connected.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "The login was cancelled or took longer than 5 minutes.";
        }
        catch (HttpListenerException ex)
        {
            StatusMessage = "Unable to listen on the redirect URI. Use a free local port, registered in your app. " + ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _loginCts.Dispose();
            _loginCts = null;
        }
    }

    [RelayCommand]
    private void CancelLogin() => _loginCts?.Cancel();

    [RelayCommand]
    private void Disconnect()
    {
        _secrets?.DeleteSecret(YouTubeProvider.Id, _secretKey, YouTubeProvider.TokenSecret);
        IsConnected = false;
        StatusMessage = null;
    }

    public void LoadFromJson(string json)
    {
        try
        {
            YouTubeConfigModel config = YouTubeProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            RedirectUri = string.IsNullOrWhiteSpace(config.RedirectUri) ? YouTubeConfigModel.DefaultRedirectUri : config.RedirectUri;
            PrivacyType = config.PrivacyType;
            UseShortenedLink = config.UseShortenedLink;
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the YouTube settings.";
        }
    }

    public string ToJson()
    {
        PersistClient();
        return JsonConvert.SerializeObject(CreateModel(), Formatting.Indented);
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            StatusMessage = YouTubeProvider.MissingClientMessage;
            return false;
        }

        PersistClient();
        StatusMessage = IsConnected ? null : "Authorize your account.";
        return IsConnected;
    }

    public void SetContext(IProviderContext context)
    {
        _secrets = context.Secrets;
        LoadSecrets();
    }

    private YouTubeConfigModel CreateModel() => new()
    {
        SecretKey = _secretKey,
        RedirectUri = RedirectUri?.Trim() ?? string.Empty,
        PrivacyType = PrivacyType,
        UseShortenedLink = UseShortenedLink
    };

    /// <summary>The account's settings and token, for the settings page's own requests (loading folders and the like).</summary>
    private YouTubeUploader CreateUploader()
    {
        var authInfo = new OAuth2Info(ClientId.Trim(), ClientSecret.Trim());
        if (_secrets?.GetSecret(YouTubeProvider.Id, _secretKey, YouTubeProvider.TokenSecret) is { Length: > 0 } tokenJson &&
            JsonConvert.DeserializeObject<OAuth2Token>(tokenJson) is { } token)
        {
            authInfo.Token = token;
        }

        return new YouTubeUploader(authInfo, refreshed =>
            _secrets?.SetSecret(YouTubeProvider.Id, _secretKey, YouTubeProvider.TokenSecret, JsonConvert.SerializeObject(refreshed, Formatting.None)), CreateModel());
    }

    private void LoadSecrets()
    {
        if (_secrets == null) return;
        ClientId = _secrets.GetSecret(YouTubeProvider.Id, _secretKey, YouTubeProvider.ClientIdSecret) ?? string.Empty;
        ClientSecret = _secrets.GetSecret(YouTubeProvider.Id, _secretKey, YouTubeProvider.ClientSecretSecret) ?? string.Empty;
        IsConnected = !string.IsNullOrEmpty(_secrets.GetSecret(YouTubeProvider.Id, _secretKey, YouTubeProvider.TokenSecret));
    }

    private void PersistClient()
    {
        if (_secrets == null) return;
        Persist(YouTubeProvider.ClientIdSecret, ClientId);
        Persist(YouTubeProvider.ClientSecretSecret, ClientSecret);
    }

    private void Persist(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _secrets!.DeleteSecret(YouTubeProvider.Id, _secretKey, name);
        else _secrets!.SetSecret(YouTubeProvider.Id, _secretKey, name, value.Trim());
    }
}
