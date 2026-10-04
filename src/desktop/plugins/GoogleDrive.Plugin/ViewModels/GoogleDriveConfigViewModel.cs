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

namespace ShareX.GoogleDrive.Plugin.ViewModels;

public partial class GoogleDriveConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _clientId = string.Empty;
    [ObservableProperty] private string _clientSecret = string.Empty;
    [ObservableProperty] private string _redirectUri = GoogleDriveConfigModel.DefaultRedirectUri;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isPublic = true;
    [ObservableProperty] private bool _directLink;
    [ObservableProperty] private bool _useFolder;
    [ObservableProperty] private string _folderID = string.Empty;
    [ObservableProperty] private GoogleDriveItem? _selectedDrive;
    [ObservableProperty] private GoogleDriveItem? _selectedFolder;

    public ObservableCollection<GoogleDriveItem> Drives { get; } = new();
    public ObservableCollection<GoogleDriveItem> Folders { get; } = new();

    private static GoogleDriveItem MyDrive => new() { ID = string.Empty, Name = "My drive" };

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;
    private CancellationTokenSource? _loginCts;

    public string ConnectionStatus => IsConnected ? "Connected" : "Not connected";

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectionStatus));

    [RelayCommand]
    private static void OpenAppRegistration() => URLHelpers.OpenURL(GoogleDriveProvider.AppRegistrationUrl);

    /// <summary>Opens the authorization page and receives the code on the local redirect URI.</summary>
    [RelayCommand]
    private async Task AuthorizeAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            StatusMessage = GoogleDriveProvider.MissingClientMessage;
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
            var uploader = new GoogleDriveUploader(authInfo, null, new GoogleDriveConfigModel());
            OAuth2Token token = await GoogleDriveProvider.OAuth.LoginWithBrowserAsync(uploader, authInfo, redirectUri!, URLHelpers.OpenURL, _loginCts.Token);
            _secrets?.SetSecret(GoogleDriveProvider.Id, _secretKey, GoogleDriveProvider.TokenSecret, JsonConvert.SerializeObject(token, Formatting.None));
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
        _secrets?.DeleteSecret(GoogleDriveProvider.Id, _secretKey, GoogleDriveProvider.TokenSecret);
        IsConnected = false;
        StatusMessage = null;
    }

    partial void OnSelectedFolderChanged(GoogleDriveItem? value)
    {
        if (value != null) FolderID = value.ID;
    }

    /// <summary>ShareX's shared drive list, with My drive first.</summary>
    [RelayCommand]
    private async Task LoadDrivesAsync()
    {
        IsBusy = true;
        try
        {
            GoogleDriveUploader uploader = CreateUploader();
            List<GoogleDriveItem>? drives = await Task.Run(uploader.GetDrives);
            string? previous = SelectedDrive?.ID;
            Drives.Clear();
            Drives.Add(MyDrive);
            foreach (GoogleDriveItem drive in drives ?? []) Drives.Add(drive);
            SelectedDrive = Drives.FirstOrDefault(drive => drive.ID == previous) ?? Drives[0];
            StatusMessage = drives == null ? "Loading the shared drives failed. " + string.Join(" ", uploader.Errors.Errors.Select(e => e.Text)) : null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>ShareX's folder list: the folders you can write to, in the chosen drive.</summary>
    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        IsBusy = true;
        try
        {
            GoogleDriveUploader uploader = CreateUploader();
            string driveID = SelectedDrive?.ID ?? string.Empty;
            List<GoogleDriveItem>? folders = await Task.Run(() => uploader.GetFolders(driveID));
            Folders.Clear();
            foreach (GoogleDriveItem folder in folders ?? []) Folders.Add(folder);
            SelectedFolder = Folders.FirstOrDefault(folder => folder.ID == FolderID);
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
            GoogleDriveConfigModel config = GoogleDriveProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            RedirectUri = string.IsNullOrWhiteSpace(config.RedirectUri) ? GoogleDriveConfigModel.DefaultRedirectUri : config.RedirectUri;
            IsPublic = config.IsPublic;
            DirectLink = config.DirectLink;
            UseFolder = config.UseFolder;
            FolderID = config.FolderID;
            Drives.Clear();
            Drives.Add(MyDrive);
            if (!string.IsNullOrEmpty(config.DriveID)) Drives.Add(new GoogleDriveItem { ID = config.DriveID, Name = config.DriveName });
            SelectedDrive = Drives.FirstOrDefault(drive => drive.ID == config.DriveID) ?? Drives[0];
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Google Drive settings.";
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
            StatusMessage = GoogleDriveProvider.MissingClientMessage;
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

    private GoogleDriveConfigModel CreateModel() => new()
    {
        SecretKey = _secretKey,
        RedirectUri = RedirectUri?.Trim() ?? string.Empty,
        IsPublic = IsPublic,
        DirectLink = DirectLink,
        UseFolder = UseFolder,
        FolderID = FolderID?.Trim() ?? string.Empty,
        DriveID = SelectedDrive?.ID ?? string.Empty,
        DriveName = string.IsNullOrEmpty(SelectedDrive?.ID) ? string.Empty : SelectedDrive.Name
    };

    /// <summary>The account's settings and token, for the settings page's own requests (loading folders and the like).</summary>
    private GoogleDriveUploader CreateUploader()
    {
        var authInfo = new OAuth2Info(ClientId.Trim(), ClientSecret.Trim());
        if (_secrets?.GetSecret(GoogleDriveProvider.Id, _secretKey, GoogleDriveProvider.TokenSecret) is { Length: > 0 } tokenJson &&
            JsonConvert.DeserializeObject<OAuth2Token>(tokenJson) is { } token)
        {
            authInfo.Token = token;
        }

        return new GoogleDriveUploader(authInfo, refreshed =>
            _secrets?.SetSecret(GoogleDriveProvider.Id, _secretKey, GoogleDriveProvider.TokenSecret, JsonConvert.SerializeObject(refreshed, Formatting.None)), CreateModel());
    }

    private void LoadSecrets()
    {
        if (_secrets == null) return;
        ClientId = _secrets.GetSecret(GoogleDriveProvider.Id, _secretKey, GoogleDriveProvider.ClientIdSecret) ?? string.Empty;
        ClientSecret = _secrets.GetSecret(GoogleDriveProvider.Id, _secretKey, GoogleDriveProvider.ClientSecretSecret) ?? string.Empty;
        IsConnected = !string.IsNullOrEmpty(_secrets.GetSecret(GoogleDriveProvider.Id, _secretKey, GoogleDriveProvider.TokenSecret));
    }

    private void PersistClient()
    {
        if (_secrets == null) return;
        Persist(GoogleDriveProvider.ClientIdSecret, ClientId);
        Persist(GoogleDriveProvider.ClientSecretSecret, ClientSecret);
    }

    private void Persist(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _secrets!.DeleteSecret(GoogleDriveProvider.Id, _secretKey, name);
        else _secrets!.SetSecret(GoogleDriveProvider.Id, _secretKey, name, value.Trim());
    }
}
