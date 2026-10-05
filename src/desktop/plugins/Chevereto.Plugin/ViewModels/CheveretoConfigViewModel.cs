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
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Chevereto.Plugin.ViewModels;

public partial class CheveretoConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _uploadURL = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private bool _directURL = true;
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    public void LoadFromJson(string json)
    {
        try
        {
            CheveretoConfigModel config = CheveretoProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            UploadURL = config.UploadURL ?? string.Empty;
            DirectURL = config.DirectURL;
            ApiKey = config.APIKey ?? string.Empty;
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Chevereto settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(CreateConfig(), Formatting.Indented);
    }

    public bool Validate()
    {
        StatusMessage = CheveretoProvider.GetConfigError(CreateConfig(), ApiKey);
        if (StatusMessage != null) return false;
        PersistSecrets();
        return true;
    }

    public void SetContext(IProviderContext context)
    {
        _secrets = context.Secrets;
        LoadSecrets();
    }

    private CheveretoConfigModel CreateConfig() => new()
    {
        SecretKey = _secretKey,
        UploadURL = UploadURL?.Trim() ?? string.Empty,
        DirectURL = DirectURL
    };

    private void LoadSecrets()
    {
        if (_secrets == null) return;
        string? stored = _secrets.GetSecret(CheveretoProvider.Id, _secretKey, CheveretoProvider.ApiKeySecret);
        if (!string.IsNullOrEmpty(stored)) ApiKey = stored;
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        if (string.IsNullOrWhiteSpace(ApiKey)) _secrets.DeleteSecret(CheveretoProvider.Id, _secretKey, CheveretoProvider.ApiKeySecret);
        else _secrets.SetSecret(CheveretoProvider.Id, _secretKey, CheveretoProvider.ApiKeySecret, ApiKey.Trim());
    }
}
