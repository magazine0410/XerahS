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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Filen.Plugin.ViewModels;

public partial class FilenConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    private static readonly FilenFolderInfo RootFolder = new() { ID = string.Empty, Name = "Cloud Drive (root folder)" };

    public static IReadOnlyList<FilenLinkExpiration> LinkExpirations { get; } =
    [
        new("never", "Never"),
        new("1h", "1 hour"),
        new("6h", "6 hours"),
        new("1d", "1 day"),
        new("3d", "3 days"),
        new("7d", "7 days"),
        new("14d", "14 days"),
        new("30d", "30 days")
    ];

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _twoFactorCode = string.Empty;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private FilenFolderInfo? _selectedFolder = RootFolder;
    [ObservableProperty] private FilenLinkExpiration _selectedLinkExpiration = LinkExpirations[0];
    [ObservableProperty] private bool _showDownloadButton = true;
    [ObservableProperty] private string? _statusMessage;

    public ObservableCollection<FilenFolderInfo> Folders { get; } = new() { RootFolder };

    public string ConnectionStatus => IsConnected ? "Connected" : "Not connected";

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;
    private bool _loading;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectionStatus));

    // Changing the email disconnects the account, as for MEGA.
    partial void OnEmailChanged(string? oldValue, string newValue)
    {
        if (!_loading && !string.Equals(oldValue, newValue, StringComparison.Ordinal)) Disconnect();
    }

    [RelayCommand]
    private async Task LogInAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            StatusMessage = "Enter your Filen email and password.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Logging in...";
        try
        {
            var uploader = new FilenUploader(Email, Password);
            string? code = string.IsNullOrWhiteSpace(TwoFactorCode) ? null : TwoFactorCode;
            (string apiKey, FilenKeys keys) = await Task.Run(() => uploader.Login(code));
            if (_secrets != null)
            {
                _secrets.SetSecret(FilenProvider.Id, _secretKey, FilenProvider.ApiKeySecret, apiKey);
                _secrets.SetSecret(FilenProvider.Id, _secretKey, FilenProvider.KeysSecret, keys.Serialize());
            }

            // The password is not kept after the login.
            Password = string.Empty;
            TwoFactorCode = string.Empty;
            IsConnected = true;
            StatusMessage = "Connected.";
        }
        catch (FilenApiException ex) when (ex.Code == "enter_2fa")
        {
            StatusMessage = "Filen requires a two-factor authentication code. Enter the current code and log in again.";
        }
        catch (FilenApiException ex) when (ex.Code == "wrong_2fa")
        {
            StatusMessage = "The two-factor authentication code is wrong. Enter the current code and log in again.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Filen login failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        if (_secrets != null)
        {
            _secrets.DeleteSecret(FilenProvider.Id, _secretKey, FilenProvider.ApiKeySecret);
            _secrets.DeleteSecret(FilenProvider.Id, _secretKey, FilenProvider.KeysSecret);
        }

        IsConnected = false;
        Folders.Clear();
        Folders.Add(RootFolder);
        SelectedFolder = RootFolder;
    }

    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        FilenKeys? keys = _secrets == null ? null : FilenKeys.Deserialize(_secrets.GetSecret(FilenProvider.Id, _secretKey, FilenProvider.KeysSecret));
        if (!IsConnected || _secrets == null || keys == null)
        {
            StatusMessage = FilenProvider.LoginRequiredMessage;
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading folders...";
        try
        {
            var uploader = new FilenUploader(_secrets.GetSecret(FilenProvider.Id, _secretKey, FilenProvider.ApiKeySecret), keys);
            IReadOnlyList<FilenFolderInfo> folders = await Task.Run(uploader.GetAllFolders);
            string? previous = SelectedFolder?.ID;
            Folders.Clear();
            Folders.Add(RootFolder);
            foreach (FilenFolderInfo folder in folders) Folders.Add(folder);
            SelectedFolder = Folders.FirstOrDefault(folder => folder.ID == previous) ?? RootFolder;
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = "Loading the Filen folders failed: " + ex.Message;
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
            FilenConfigModel config = FilenProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            _loading = true;
            Email = config.Email;
            _loading = false;
            Folders.Clear();
            Folders.Add(RootFolder);
            if (!string.IsNullOrEmpty(config.FolderID))
            {
                Folders.Add(new FilenFolderInfo { ID = config.FolderID, Name = config.FolderName });
            }

            SelectedFolder = Folders.FirstOrDefault(folder => folder.ID == config.FolderID) ?? RootFolder;
            SelectedLinkExpiration = LinkExpirations.FirstOrDefault(option => option.Value == config.LinkExpiration) ?? LinkExpirations[0];
            ShowDownloadButton = config.ShowDownloadButton;
            RefreshConnection();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Filen settings.";
        }
    }

    public string ToJson() => JsonConvert.SerializeObject(new FilenConfigModel
    {
        SecretKey = _secretKey,
        Email = Email?.Trim() ?? string.Empty,
        FolderID = SelectedFolder?.ID ?? string.Empty,
        FolderName = SelectedFolder == RootFolder ? string.Empty : SelectedFolder?.Name ?? string.Empty,
        LinkExpiration = SelectedLinkExpiration?.Value ?? "never",
        ShowDownloadButton = ShowDownloadButton
    }, Formatting.Indented);

    public bool Validate()
    {
        if (!IsConnected)
        {
            StatusMessage = FilenProvider.LoginRequiredMessage;
            return false;
        }

        StatusMessage = null;
        return true;
    }

    public void SetContext(IProviderContext context)
    {
        _secrets = context.Secrets;
        RefreshConnection();
    }

    private void RefreshConnection()
    {
        IsConnected = _secrets != null &&
            !string.IsNullOrEmpty(_secrets.GetSecret(FilenProvider.Id, _secretKey, FilenProvider.ApiKeySecret)) &&
            FilenKeys.Deserialize(_secrets.GetSecret(FilenProvider.Id, _secretKey, FilenProvider.KeysSecret)) != null;
    }
}

public sealed record FilenLinkExpiration(string Value, string Label)
{
    public override string ToString() => Label;
}
