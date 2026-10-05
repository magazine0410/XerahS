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

namespace ShareX.Box.Plugin.ViewModels;

public partial class BoxConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _clientId = string.Empty;
    [ObservableProperty] private string _clientSecret = string.Empty;
    [ObservableProperty] private string _redirectUri = BoxConfigModel.DefaultRedirectUri;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _share = true;
    [ObservableProperty] private BoxShareAccessLevel _shareAccessLevel = BoxShareAccessLevel.Open;
    [ObservableProperty] private BoxFolder? _selectedFolder;

    public ObservableCollection<BoxFolder> Folders { get; } = new();
    public IReadOnlyList<BoxShareAccessLevel> ShareAccessLevels { get; } = Enum.GetValues<BoxShareAccessLevel>();

    private static BoxFolder RootFolder => new() { ID = "0", Name = "Root folder" };

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;
    private CancellationTokenSource? _loginCts;

    public string ConnectionStatus => IsConnected ? "Connected" : "Not connected";

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectionStatus));

    [RelayCommand]
    private static void OpenAppRegistration() => URLHelpers.OpenURL(BoxProvider.AppRegistrationUrl);

    /// <summary>Opens the authorization page and receives the code on the local redirect URI.</summary>
    [RelayCommand]
    private async Task AuthorizeAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            StatusMessage = BoxProvider.MissingClientMessage;
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
            var uploader = new BoxUploader(authInfo, null, new BoxConfigModel());
            OAuth2Token token = await BoxProvider.OAuth.LoginWithBrowserAsync(uploader, authInfo, redirectUri!, URLHelpers.OpenURL, _loginCts.Token);
            _secrets?.SetSecret(BoxProvider.Id, _secretKey, BoxProvider.TokenSecret, JsonConvert.SerializeObject(token, Formatting.None));
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
        _secrets?.DeleteSecret(BoxProvider.Id, _secretKey, BoxProvider.TokenSecret);
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
            BoxUploader uploader = CreateUploader();
            List<BoxFolder>? folders = await Task.Run(() => uploader.GetFolders("0"));
            string? previous = SelectedFolder?.ID;
            Folders.Clear();
            Folders.Add(RootFolder);
            foreach (BoxFolder folder in folders ?? []) Folders.Add(folder);
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
            BoxConfigModel config = BoxProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            RedirectUri = string.IsNullOrWhiteSpace(config.RedirectUri) ? BoxConfigModel.DefaultRedirectUri : config.RedirectUri;
            Share = config.Share;
            ShareAccessLevel = config.ShareAccessLevel;
            Folders.Clear();
            Folders.Add(RootFolder);
            if (!string.IsNullOrEmpty(config.FolderID) && config.FolderID != "0") Folders.Add(new BoxFolder { ID = config.FolderID, Name = config.FolderName });
            SelectedFolder = Folders.FirstOrDefault(folder => folder.ID == config.FolderID) ?? Folders[0];
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Box settings.";
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
            StatusMessage = BoxProvider.MissingClientMessage;
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

    private BoxConfigModel CreateModel() => new()
    {
        SecretKey = _secretKey,
        RedirectUri = RedirectUri?.Trim() ?? string.Empty,
        FolderID = SelectedFolder?.ID ?? "0",
        FolderName = SelectedFolder?.ID is null or "0" ? string.Empty : SelectedFolder.Name,
        Share = Share,
        ShareAccessLevel = ShareAccessLevel
    };

    /// <summary>The account's settings and token, for the settings page's own requests (loading folders and the like).</summary>
    private BoxUploader CreateUploader()
    {
        var authInfo = new OAuth2Info(ClientId.Trim(), ClientSecret.Trim());
        if (_secrets?.GetSecret(BoxProvider.Id, _secretKey, BoxProvider.TokenSecret) is { Length: > 0 } tokenJson &&
            JsonConvert.DeserializeObject<OAuth2Token>(tokenJson) is { } token)
        {
            authInfo.Token = token;
        }

        return new BoxUploader(authInfo, refreshed =>
            _secrets?.SetSecret(BoxProvider.Id, _secretKey, BoxProvider.TokenSecret, JsonConvert.SerializeObject(refreshed, Formatting.None)), CreateModel());
    }

    private void LoadSecrets()
    {
        if (_secrets == null) return;
        ClientId = _secrets.GetSecret(BoxProvider.Id, _secretKey, BoxProvider.ClientIdSecret) ?? string.Empty;
        ClientSecret = _secrets.GetSecret(BoxProvider.Id, _secretKey, BoxProvider.ClientSecretSecret) ?? string.Empty;
        IsConnected = !string.IsNullOrEmpty(_secrets.GetSecret(BoxProvider.Id, _secretKey, BoxProvider.TokenSecret));
    }

    private void PersistClient()
    {
        if (_secrets == null) return;
        Persist(BoxProvider.ClientIdSecret, ClientId);
        Persist(BoxProvider.ClientSecretSecret, ClientSecret);
    }

    private void Persist(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _secrets!.DeleteSecret(BoxProvider.Id, _secretKey, name);
        else _secrets!.SetSecret(BoxProvider.Id, _secretKey, name, value.Trim());
    }
}
