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
using XerahS.Common;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Pushbullet.Plugin.ViewModels;

public partial class PushbulletConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _accessToken = string.Empty;
    [ObservableProperty] private PushbulletDevice? _selectedDevice;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    public ObservableCollection<PushbulletDevice> Devices { get; } = new();

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    [RelayCommand]
    private static void OpenAccountSettings() => URLHelpers.OpenURL(PushbulletProvider.AccountSettingsUrl);

    /// <summary>ShareX's "Get device list".</summary>
    [RelayCommand]
    private async Task LoadDevicesAsync()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            StatusMessage = PushbulletProvider.MissingAccessTokenMessage;
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading devices...";
        try
        {
            var uploader = new PushbulletUploader(AccessToken.Trim(), null);
            List<PushbulletDevice> devices = await Task.Run(uploader.GetDeviceList);
            string? previous = SelectedDevice?.Key;
            Devices.Clear();
            foreach (PushbulletDevice device in devices) Devices.Add(device);
            SelectedDevice = Devices.FirstOrDefault(device => device.Key == previous) ?? Devices.FirstOrDefault();
            StatusMessage = devices.Count == 0
                ? "No devices were found. " + (uploader.Errors.Count > 0 ? uploader.Errors.Errors[^1].Text : "Check the access token.")
                : null;
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
            PushbulletConfigModel config = PushbulletProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            AccessToken = config.UserAPIKey ?? string.Empty;
            Devices.Clear();
            foreach (PushbulletDevice device in config.DeviceList) Devices.Add(device);
            SelectedDevice = Devices.FirstOrDefault(device => device.Key == config.SelectedDeviceKey);
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Pushbullet settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(new PushbulletConfigModel
        {
            SecretKey = _secretKey,
            DeviceList = Devices.ToList(),
            SelectedDeviceKey = SelectedDevice?.Key ?? string.Empty
        }, Formatting.Indented);
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            StatusMessage = PushbulletProvider.MissingAccessTokenMessage;
            return false;
        }

        PersistSecrets();
        if (SelectedDevice == null)
        {
            StatusMessage = PushbulletProvider.NoDeviceMessage;
            return false;
        }

        StatusMessage = null;
        return true;
    }

    public void SetContext(IProviderContext context)
    {
        _secrets = context.Secrets;
        LoadSecrets();
    }

    private void LoadSecrets()
    {
        if (_secrets == null) return;
        string? stored = _secrets.GetSecret(PushbulletProvider.Id, _secretKey, PushbulletProvider.AccessTokenSecret);
        if (!string.IsNullOrEmpty(stored)) AccessToken = stored;
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        if (string.IsNullOrWhiteSpace(AccessToken)) _secrets.DeleteSecret(PushbulletProvider.Id, _secretKey, PushbulletProvider.AccessTokenSecret);
        else _secrets.SetSecret(PushbulletProvider.Id, _secretKey, PushbulletProvider.AccessTokenSecret, AccessToken.Trim());
    }
}
