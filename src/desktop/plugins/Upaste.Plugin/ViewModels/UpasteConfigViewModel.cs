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

namespace ShareX.Upaste.Plugin.ViewModels;

public partial class UpasteConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _userKey = string.Empty;
    [ObservableProperty] private bool _isPublic;
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    [RelayCommand]
    private static void OpenAccountPage() => URLHelpers.OpenURL(UpasteProvider.AccountSettingsUrl);

    public void LoadFromJson(string json)
    {
        try
        {
            UpasteConfigModel config = UpasteProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            UserKey = config.UserKey ?? string.Empty;
            IsPublic = config.IsPublic;
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the uPaste settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(new UpasteConfigModel { SecretKey = _secretKey, IsPublic = IsPublic }, Formatting.Indented);
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(UserKey))
        {
            StatusMessage = UpasteProvider.MissingUserKeyMessage;
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
        string? stored = _secrets.GetSecret(UpasteProvider.Id, _secretKey, UpasteProvider.UserKeySecret);
        if (!string.IsNullOrEmpty(stored)) UserKey = stored;
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        if (string.IsNullOrWhiteSpace(UserKey)) _secrets.DeleteSecret(UpasteProvider.Id, _secretKey, UpasteProvider.UserKeySecret);
        else _secrets.SetSecret(UpasteProvider.Id, _secretKey, UpasteProvider.UserKeySecret, UserKey.Trim());
    }
}
