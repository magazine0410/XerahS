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

namespace ShareX.ImgFish.Plugin.ViewModels;

public partial class ImgFishConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private int _fileIDLength = ImgFishConfigModel.MinFileIDLength;
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    [RelayCommand]
    private static void OpenWebsite() => URLHelpers.OpenURL("https://img.fish/");

    public void LoadFromJson(string json)
    {
        try
        {
            ImgFishConfigModel config = ImgFishProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            ApiKey = config.APIKey ?? string.Empty;
            FileIDLength = config.FileIDLength;
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the img.fish settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(new ImgFishConfigModel { SecretKey = _secretKey, FileIDLength = FileIDLength }, Formatting.Indented);
    }

    public bool Validate()
    {
        if (FileIDLength is < ImgFishConfigModel.MinFileIDLength or > ImgFishConfigModel.MaxFileIDLength)
        {
            StatusMessage = $"The file ID length must be {ImgFishConfigModel.MinFileIDLength} to {ImgFishConfigModel.MaxFileIDLength}.";
            return false;
        }

        PersistSecrets();
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
        string? stored = _secrets.GetSecret(ImgFishProvider.Id, _secretKey, ImgFishProvider.ApiKeySecret);
        if (!string.IsNullOrEmpty(stored)) ApiKey = stored;
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        if (string.IsNullOrWhiteSpace(ApiKey)) _secrets.DeleteSecret(ImgFishProvider.Id, _secretKey, ImgFishProvider.ApiKeySecret);
        else _secrets.SetSecret(ImgFishProvider.Id, _secretKey, ImgFishProvider.ApiKeySecret, ApiKey.Trim());
    }
}
