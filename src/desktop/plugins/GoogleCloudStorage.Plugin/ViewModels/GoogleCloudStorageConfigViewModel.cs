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

namespace ShareX.GoogleCloudStorage.Plugin.ViewModels;

public partial class GoogleCloudStorageConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _clientId = string.Empty;
    [ObservableProperty] private string _clientSecret = string.Empty;
    [ObservableProperty] private string _redirectUri = GoogleCloudStorageConfigModel.DefaultRedirectUri;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _bucket = string.Empty;
    [ObservableProperty] private string _domain = string.Empty;
    [ObservableProperty] private string _objectPrefix = "ShareX/%y/%mo";
    [ObservableProperty] private bool _removeExtensionImage;
    [ObservableProperty] private bool _removeExtensionVideo;
    [ObservableProperty] private bool _removeExtensionText;
    [ObservableProperty] private bool _setPublicACL = true;

    /// <summary>ShareX's preview of the URL an upload gets.</summary>
    public string PreviewUrl => GoogleCloudStorageUploader.GetPreviewURL(CreateModel());

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;
    private CancellationTokenSource? _loginCts;

    public string ConnectionStatus => IsConnected ? "Connected" : "Not connected";

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectionStatus));

    [RelayCommand]
    private static void OpenAppRegistration() => URLHelpers.OpenURL(GoogleCloudStorageProvider.AppRegistrationUrl);

    /// <summary>Opens the authorization page and receives the code on the local redirect URI.</summary>
    [RelayCommand]
    private async Task AuthorizeAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            StatusMessage = GoogleCloudStorageProvider.MissingClientMessage;
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
            var uploader = new GoogleCloudStorageUploader(authInfo, null, new GoogleCloudStorageConfigModel());
            OAuth2Token token = await GoogleCloudStorageProvider.OAuth.LoginWithBrowserAsync(uploader, authInfo, redirectUri!, URLHelpers.OpenURL, _loginCts.Token);
            _secrets?.SetSecret(GoogleCloudStorageProvider.Id, _secretKey, GoogleCloudStorageProvider.TokenSecret, JsonConvert.SerializeObject(token, Formatting.None));
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
        _secrets?.DeleteSecret(GoogleCloudStorageProvider.Id, _secretKey, GoogleCloudStorageProvider.TokenSecret);
        IsConnected = false;
        StatusMessage = null;
    }

    partial void OnBucketChanged(string value) => OnPropertyChanged(nameof(PreviewUrl));
    partial void OnDomainChanged(string value) => OnPropertyChanged(nameof(PreviewUrl));
    partial void OnObjectPrefixChanged(string value) => OnPropertyChanged(nameof(PreviewUrl));
    partial void OnRemoveExtensionImageChanged(bool value) => OnPropertyChanged(nameof(PreviewUrl));

    public void LoadFromJson(string json)
    {
        try
        {
            GoogleCloudStorageConfigModel config = GoogleCloudStorageProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            RedirectUri = string.IsNullOrWhiteSpace(config.RedirectUri) ? GoogleCloudStorageConfigModel.DefaultRedirectUri : config.RedirectUri;
            Bucket = config.Bucket;
            Domain = config.Domain;
            ObjectPrefix = config.ObjectPrefix;
            RemoveExtensionImage = config.RemoveExtensionImage;
            RemoveExtensionVideo = config.RemoveExtensionVideo;
            RemoveExtensionText = config.RemoveExtensionText;
            SetPublicACL = config.SetPublicACL;
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Google Cloud Storage settings.";
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
            StatusMessage = GoogleCloudStorageProvider.MissingClientMessage;
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

    private GoogleCloudStorageConfigModel CreateModel() => new()
    {
        SecretKey = _secretKey,
        RedirectUri = RedirectUri?.Trim() ?? string.Empty,
        Bucket = Bucket?.Trim() ?? string.Empty,
        Domain = Domain?.Trim() ?? string.Empty,
        ObjectPrefix = ObjectPrefix ?? string.Empty,
        RemoveExtensionImage = RemoveExtensionImage,
        RemoveExtensionVideo = RemoveExtensionVideo,
        RemoveExtensionText = RemoveExtensionText,
        SetPublicACL = SetPublicACL
    };

    /// <summary>The account's settings and token, for the settings page's own requests (loading folders and the like).</summary>
    private GoogleCloudStorageUploader CreateUploader()
    {
        var authInfo = new OAuth2Info(ClientId.Trim(), ClientSecret.Trim());
        if (_secrets?.GetSecret(GoogleCloudStorageProvider.Id, _secretKey, GoogleCloudStorageProvider.TokenSecret) is { Length: > 0 } tokenJson &&
            JsonConvert.DeserializeObject<OAuth2Token>(tokenJson) is { } token)
        {
            authInfo.Token = token;
        }

        return new GoogleCloudStorageUploader(authInfo, refreshed =>
            _secrets?.SetSecret(GoogleCloudStorageProvider.Id, _secretKey, GoogleCloudStorageProvider.TokenSecret, JsonConvert.SerializeObject(refreshed, Formatting.None)), CreateModel());
    }

    private void LoadSecrets()
    {
        if (_secrets == null) return;
        ClientId = _secrets.GetSecret(GoogleCloudStorageProvider.Id, _secretKey, GoogleCloudStorageProvider.ClientIdSecret) ?? string.Empty;
        ClientSecret = _secrets.GetSecret(GoogleCloudStorageProvider.Id, _secretKey, GoogleCloudStorageProvider.ClientSecretSecret) ?? string.Empty;
        IsConnected = !string.IsNullOrEmpty(_secrets.GetSecret(GoogleCloudStorageProvider.Id, _secretKey, GoogleCloudStorageProvider.TokenSecret));
    }

    private void PersistClient()
    {
        if (_secrets == null) return;
        Persist(GoogleCloudStorageProvider.ClientIdSecret, ClientId);
        Persist(GoogleCloudStorageProvider.ClientSecretSecret, ClientSecret);
    }

    private void Persist(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _secrets!.DeleteSecret(GoogleCloudStorageProvider.Id, _secretKey, name);
        else _secrets!.SetSecret(GoogleCloudStorageProvider.Id, _secretKey, name, value.Trim());
    }
}
