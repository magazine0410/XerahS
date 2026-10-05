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

namespace ShareX.OneDrive.Plugin.ViewModels;

public partial class OneDriveConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _clientId = string.Empty;
    [ObservableProperty] private string _clientSecret = string.Empty;
    [ObservableProperty] private string _redirectUri = OneDriveConfigModel.DefaultRedirectUri;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _autoCreateShareableLink = true;
    [ObservableProperty] private bool _useDirectLink;
    [ObservableProperty] private OneDriveFolder? _selectedFolder;

    public ObservableCollection<OneDriveFolder> Folders { get; } = new();

    private static OneDriveFolder RootFolder => new() { ID = string.Empty, Name = "Root folder" };

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;
    private CancellationTokenSource? _loginCts;

    public string ConnectionStatus => IsConnected ? "Connected" : "Not connected";

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectionStatus));

    [RelayCommand]
    private static void OpenAppRegistration() => URLHelpers.OpenURL(OneDriveProvider.AppRegistrationUrl);

    /// <summary>Opens the authorization page and receives the code on the local redirect URI.</summary>
    [RelayCommand]
    private async Task AuthorizeAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            StatusMessage = OneDriveProvider.MissingClientMessage;
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
            var uploader = new OneDriveUploader(authInfo, null, new OneDriveConfigModel());
            OAuth2Token token = await OneDriveProvider.OAuth.LoginWithBrowserAsync(uploader, authInfo, redirectUri!, URLHelpers.OpenURL, _loginCts.Token);
            _secrets?.SetSecret(OneDriveProvider.Id, _secretKey, OneDriveProvider.TokenSecret, JsonConvert.SerializeObject(token, Formatting.None));
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
        _secrets?.DeleteSecret(OneDriveProvider.Id, _secretKey, OneDriveProvider.TokenSecret);
        IsConnected = false;
        StatusMessage = null;
    }

    /// <summary>The folders in the root folder.</summary>
    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        IsBusy = true;
        try
        {
            OneDriveUploader uploader = CreateUploader();
            List<OneDriveFolder>? folders = await Task.Run(() => uploader.GetFolders(string.Empty));
            string? previous = SelectedFolder?.ID;
            Folders.Clear();
            Folders.Add(RootFolder);
            foreach (OneDriveFolder folder in folders ?? []) Folders.Add(folder);
            SelectedFolder = Folders.FirstOrDefault(folder => folder.ID == previous) ?? Folders[0];
            StatusMessage = folders == null ? "Loading the folders failed. " + string.Join(" ", uploader.Errors.Errors.Select(e => e.Text)) : null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void LoadFromJson(string json)
    {
        try
        {
            OneDriveConfigModel config = OneDriveProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            RedirectUri = string.IsNullOrWhiteSpace(config.RedirectUri) ? OneDriveConfigModel.DefaultRedirectUri : config.RedirectUri;
            AutoCreateShareableLink = config.AutoCreateShareableLink;
            UseDirectLink = config.UseDirectLink;
            Folders.Clear();
            Folders.Add(RootFolder);
            if (!string.IsNullOrEmpty(config.FolderID)) Folders.Add(new OneDriveFolder { ID = config.FolderID, Name = config.FolderName });
            SelectedFolder = Folders.FirstOrDefault(folder => folder.ID == config.FolderID) ?? Folders[0];
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the OneDrive settings.";
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
            StatusMessage = OneDriveProvider.MissingClientMessage;
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

    private OneDriveConfigModel CreateModel() => new()
    {
        SecretKey = _secretKey,
        RedirectUri = RedirectUri?.Trim() ?? string.Empty,
        FolderID = SelectedFolder?.ID ?? string.Empty,
        FolderName = string.IsNullOrEmpty(SelectedFolder?.ID) ? string.Empty : SelectedFolder.Name,
        AutoCreateShareableLink = AutoCreateShareableLink,
        UseDirectLink = UseDirectLink
    };

    /// <summary>The account's settings and token, for the settings page's own requests (loading folders and the like).</summary>
    private OneDriveUploader CreateUploader()
    {
        var authInfo = new OAuth2Info(ClientId.Trim(), ClientSecret.Trim());
        if (_secrets?.GetSecret(OneDriveProvider.Id, _secretKey, OneDriveProvider.TokenSecret) is { Length: > 0 } tokenJson &&
            JsonConvert.DeserializeObject<OAuth2Token>(tokenJson) is { } token)
        {
            authInfo.Token = token;
        }

        return new OneDriveUploader(authInfo, refreshed =>
            _secrets?.SetSecret(OneDriveProvider.Id, _secretKey, OneDriveProvider.TokenSecret, JsonConvert.SerializeObject(refreshed, Formatting.None)), CreateModel());
    }

    private void LoadSecrets()
    {
        if (_secrets == null) return;
        ClientId = _secrets.GetSecret(OneDriveProvider.Id, _secretKey, OneDriveProvider.ClientIdSecret) ?? string.Empty;
        ClientSecret = _secrets.GetSecret(OneDriveProvider.Id, _secretKey, OneDriveProvider.ClientSecretSecret) ?? string.Empty;
        IsConnected = !string.IsNullOrEmpty(_secrets.GetSecret(OneDriveProvider.Id, _secretKey, OneDriveProvider.TokenSecret));
    }

    private void PersistClient()
    {
        if (_secrets == null) return;
        Persist(OneDriveProvider.ClientIdSecret, ClientId);
        Persist(OneDriveProvider.ClientSecretSecret, ClientSecret);
    }

    private void Persist(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _secrets!.DeleteSecret(OneDriveProvider.Id, _secretKey, name);
        else _secrets!.SetSecret(OneDriveProvider.Id, _secretKey, name, value.Trim());
    }
}
