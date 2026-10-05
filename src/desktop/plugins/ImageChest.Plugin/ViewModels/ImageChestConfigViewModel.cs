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

namespace ShareX.ImageChest.Plugin.ViewModels;

public sealed record ImageChestPrivacyOption(string Label, string Value)
{
    public override string ToString() => Label;
}

public partial class ImageChestConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    public IReadOnlyList<ImageChestPrivacyOption> PrivacyOptions { get; } =
    [
        new("Hidden (default)", "hidden"),
        new("Public", "public"),
        new("Secret", "secret")
    ];

    [ObservableProperty] private string _accessToken = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private ImageChestPrivacyOption? _selectedPrivacy;
    [ObservableProperty] private bool _nsfw;
    [ObservableProperty] private bool _anonymous;
    [ObservableProperty] private bool _directLink = true;
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    public ImageChestConfigViewModel()
    {
        _selectedPrivacy = PrivacyOptions[0];
    }

    [RelayCommand]
    private static void OpenProfilePage() => URLHelpers.OpenURL("https://imgchest.com/profile");

    public void LoadFromJson(string json)
    {
        try
        {
            ImageChestConfigModel config = ImageChestProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            Title = config.Title ?? string.Empty;
            SelectedPrivacy = PrivacyOptions.FirstOrDefault(o => o.Value == config.Privacy) ?? PrivacyOptions[0];
            Nsfw = config.Nsfw;
            Anonymous = config.Anonymous;
            DirectLink = config.DirectLink;
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Image Chest settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(new ImageChestConfigModel
        {
            SecretKey = _secretKey,
            Title = Title?.Trim() ?? string.Empty,
            Privacy = SelectedPrivacy?.Value ?? "hidden",
            Nsfw = Nsfw,
            Anonymous = Anonymous,
            DirectLink = DirectLink
        }, Formatting.Indented);
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            StatusMessage = ImageChestProvider.MissingTokenMessage;
            return false;
        }

        StatusMessage = null;
        PersistSecrets();
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
        string? stored = _secrets.GetSecret(ImageChestProvider.Id, _secretKey, ImageChestProvider.AccessTokenSecret);
        if (!string.IsNullOrEmpty(stored)) AccessToken = stored;
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        if (string.IsNullOrWhiteSpace(AccessToken)) _secrets.DeleteSecret(ImageChestProvider.Id, _secretKey, ImageChestProvider.AccessTokenSecret);
        else _secrets.SetSecret(ImageChestProvider.Id, _secretKey, ImageChestProvider.AccessTokenSecret, AccessToken.Trim());
    }
}
