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

namespace ShareX.Mega.Plugin.ViewModels;

public partial class MegaConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    private static readonly MegaFolderInfo RootFolder = new() { ID = string.Empty, Name = "Cloud Drive (root folder)" };

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _twoFactorCode = string.Empty;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private MegaFolderInfo? _selectedFolder = RootFolder;
    [ObservableProperty] private string? _statusMessage;

    public ObservableCollection<MegaFolderInfo> Folders { get; } = new() { RootFolder };

    public string ConnectionStatus => IsConnected ? "Connected" : "Not connected";

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;
    private bool _loading;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectionStatus));

    // As in ShareX, changing the email disconnects the account.
    partial void OnEmailChanged(string? oldValue, string newValue)
    {
        if (!_loading && !string.Equals(oldValue, newValue, StringComparison.Ordinal)) Disconnect();
    }

    [RelayCommand]
    private async Task LogInAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            StatusMessage = "Enter your MEGA email and password.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Logging in...";
        try
        {
            var uploader = new MegaUploader(Email, Password);
            string? code = string.IsNullOrWhiteSpace(TwoFactorCode) ? null : TwoFactorCode;
            (string sessionID, string masterKey) = await Task.Run(() => uploader.Login(code));
            if (_secrets != null)
            {
                _secrets.SetSecret(MegaProvider.Id, _secretKey, MegaProvider.SessionIDSecret, sessionID);
                _secrets.SetSecret(MegaProvider.Id, _secretKey, MegaProvider.MasterKeySecret, masterKey);
            }

            // As in ShareX, the password is cleared after the login.
            Password = string.Empty;
            TwoFactorCode = string.Empty;
            IsConnected = true;
            StatusMessage = "Connected.";
        }
        catch (MegaApiException ex) when (ex.ErrorCode == -26)
        {
            StatusMessage = "MEGA requires a two-factor authentication code. Enter the current code and log in again.";
        }
        catch (Exception ex)
        {
            StatusMessage = "MEGA login failed: " + ex.Message;
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
            _secrets.DeleteSecret(MegaProvider.Id, _secretKey, MegaProvider.SessionIDSecret);
            _secrets.DeleteSecret(MegaProvider.Id, _secretKey, MegaProvider.MasterKeySecret);
        }

        IsConnected = false;
        Folders.Clear();
        Folders.Add(RootFolder);
        SelectedFolder = RootFolder;
    }

    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        if (!IsConnected || _secrets == null)
        {
            StatusMessage = MegaProvider.LoginRequiredMessage;
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading folders...";
        try
        {
            var uploader = new MegaUploader(_secrets.GetSecret(MegaProvider.Id, _secretKey, MegaProvider.SessionIDSecret),
                MegaUploader.FromBase64URL(_secrets.GetSecret(MegaProvider.Id, _secretKey, MegaProvider.MasterKeySecret) ?? string.Empty));
            IReadOnlyList<MegaFolderInfo> folders = await Task.Run(uploader.GetAllFolders);
            string? previous = SelectedFolder?.ID;
            Folders.Clear();
            Folders.Add(RootFolder);
            foreach (MegaFolderInfo folder in folders) Folders.Add(folder);
            SelectedFolder = Folders.FirstOrDefault(folder => folder.ID == previous) ?? RootFolder;
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = "Loading the MEGA folders failed: " + ex.Message;
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
            MegaConfigModel config = MegaProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            _loading = true;
            Email = config.Email;
            _loading = false;
            Folders.Clear();
            Folders.Add(RootFolder);
            if (!string.IsNullOrEmpty(config.FolderID))
            {
                Folders.Add(new MegaFolderInfo { ID = config.FolderID, Name = config.FolderName });
            }

            SelectedFolder = Folders.FirstOrDefault(folder => folder.ID == config.FolderID) ?? RootFolder;
            RefreshConnection();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the MEGA settings.";
        }
    }

    public string ToJson() => JsonConvert.SerializeObject(new MegaConfigModel
    {
        SecretKey = _secretKey,
        Email = Email?.Trim() ?? string.Empty,
        FolderID = SelectedFolder?.ID ?? string.Empty,
        FolderName = SelectedFolder == RootFolder ? string.Empty : SelectedFolder?.Name ?? string.Empty
    }, Formatting.Indented);

    public bool Validate()
    {
        if (!IsConnected)
        {
            StatusMessage = MegaProvider.LoginRequiredMessage;
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
            !string.IsNullOrEmpty(_secrets.GetSecret(MegaProvider.Id, _secretKey, MegaProvider.SessionIDSecret)) &&
            !string.IsNullOrEmpty(_secrets.GetSecret(MegaProvider.Id, _secretKey, MegaProvider.MasterKeySecret));
    }
}
