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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using XerahS.Common;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.ImageShack.Plugin.ViewModels;

public partial class ImageShackConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _isPublic;
    [ObservableProperty] private decimal? _thumbnailWidth = 256;
    [ObservableProperty] private decimal? _thumbnailHeight = 0;
    [ObservableProperty] private string _connectionStatus = "Not connected";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private string _authToken = string.Empty;
    private ISecretStore? _secrets;

    [RelayCommand]
    private async Task LogInAsync()
    {
        IsBusy = true;
        try
        {
            var uploader = new ImageShackUploader(new ImageShackConfigModel(), ApiKey, null);
            string username = Username.Trim();
            string password = Password;
            string? token = await Task.Run(() => uploader.Login(username, password));
            if (token != null)
            {
                _authToken = token;
                PersistSecrets();
                StatusMessage = null;
            }
            else
            {
                StatusMessage = uploader.Errors.ToString();
            }

            UpdateConnectionStatus(token == null ? "Login failed" : null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenPublicProfile()
    {
        if (!string.IsNullOrWhiteSpace(Username)) URLHelpers.OpenURL("https://imageshack.com/user/" + Uri.EscapeDataString(Username.Trim()));
    }

    [RelayCommand]
    private static void OpenMyImages() => URLHelpers.OpenURL("https://imageshack.com/my/images");

    [RelayCommand]
    private static void OpenApiKeyRequest() => URLHelpers.OpenURL("https://imageshack.com/contact/api");

    public void LoadFromJson(string json)
    {
        try
        {
            ImageShackConfigModel config = ImageShackProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            Username = config.Username ?? string.Empty;
            Password = config.Password ?? string.Empty;
            IsPublic = config.IsPublic;
            ThumbnailWidth = config.ThumbnailWidth;
            ThumbnailHeight = config.ThumbnailHeight;
            _authToken = config.AuthToken ?? string.Empty;
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the ImageShack settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(CreateConfig(), Formatting.Indented);
    }

    public bool Validate()
    {
        StatusMessage = ImageShackProvider.GetConfigError(ApiKey, _authToken);
        if (StatusMessage != null) return false;
        PersistSecrets();
        return true;
    }

    public void SetContext(IProviderContext context)
    {
        _secrets = context.Secrets;
        LoadSecrets();
    }

    private ImageShackConfigModel CreateConfig() => new()
    {
        SecretKey = _secretKey,
        Username = Username?.Trim() ?? string.Empty,
        IsPublic = IsPublic,
        ThumbnailWidth = (int)(ThumbnailWidth ?? 256),
        ThumbnailHeight = (int)(ThumbnailHeight ?? 0)
    };

    private void UpdateConnectionStatus(string? text = null) =>
        ConnectionStatus = text ?? (string.IsNullOrEmpty(_authToken) ? "Not connected" : "Connected");

    private void LoadSecrets()
    {
        if (_secrets == null)
        {
            UpdateConnectionStatus();
            return;
        }

        string? apiKey = _secrets.GetSecret(ImageShackProvider.Id, _secretKey, ImageShackProvider.ApiKeySecret);
        if (!string.IsNullOrEmpty(apiKey)) ApiKey = apiKey;
        string? password = _secrets.GetSecret(ImageShackProvider.Id, _secretKey, ImageShackProvider.PasswordSecret);
        if (!string.IsNullOrEmpty(password)) Password = password;
        string? token = _secrets.GetSecret(ImageShackProvider.Id, _secretKey, ImageShackProvider.AuthTokenSecret);
        if (!string.IsNullOrEmpty(token)) _authToken = token;

        UpdateConnectionStatus();
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        Store(ImageShackProvider.ApiKeySecret, ApiKey?.Trim());
        Store(ImageShackProvider.PasswordSecret, Password);
        Store(ImageShackProvider.AuthTokenSecret, _authToken);
    }

    private void Store(string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) _secrets!.DeleteSecret(ImageShackProvider.Id, _secretKey, name);
        else _secrets!.SetSecret(ImageShackProvider.Id, _secretKey, name, value);
    }
}
