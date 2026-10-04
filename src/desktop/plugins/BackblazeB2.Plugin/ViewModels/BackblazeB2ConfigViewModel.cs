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

namespace ShareX.BackblazeB2.Plugin.ViewModels;

public partial class BackblazeB2ConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _applicationKeyId = string.Empty;
    [ObservableProperty] private string _applicationKey = string.Empty;
    [ObservableProperty] private string _bucketName = string.Empty;
    [ObservableProperty] private string _uploadPath = "ShareX/%y/%mo";
    [ObservableProperty] private bool _useCustomUrl;
    [ObservableProperty] private string _customUrl = "https://example.com";
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    /// <summary>ShareX's preview of the URL an upload gets.</summary>
    public string PreviewUrl => BackblazeB2Uploader.BuildUrl("https://f001.backblazeb2.com", CreateModel(),
        URLHelpers.CombineURL(NameParser.Parse(NameParserType.FilePath, UploadPath ?? string.Empty), "example.png"));

    partial void OnBucketNameChanged(string value) => OnPropertyChanged(nameof(PreviewUrl));
    partial void OnUploadPathChanged(string value) => OnPropertyChanged(nameof(PreviewUrl));
    partial void OnUseCustomUrlChanged(bool value) => OnPropertyChanged(nameof(PreviewUrl));
    partial void OnCustomUrlChanged(string value) => OnPropertyChanged(nameof(PreviewUrl));

    [RelayCommand]
    private static void OpenApplicationKeys() => URLHelpers.OpenURL("https://secure.backblaze.com/app_keys.htm");

    public void LoadFromJson(string json)
    {
        try
        {
            BackblazeB2ConfigModel config = BackblazeB2Provider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            ApplicationKeyId = config.ApplicationKeyId ?? string.Empty;
            ApplicationKey = config.ApplicationKey ?? string.Empty;
            BucketName = config.BucketName;
            UploadPath = config.UploadPath;
            UseCustomUrl = config.UseCustomUrl;
            CustomUrl = config.CustomUrl;
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Backblaze B2 settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(CreateModel(), Formatting.Indented);
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(ApplicationKeyId) || string.IsNullOrWhiteSpace(ApplicationKey))
        {
            StatusMessage = BackblazeB2Provider.MissingKeyMessage;
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

    private BackblazeB2ConfigModel CreateModel() => new()
    {
        SecretKey = _secretKey,
        BucketName = BucketName?.Trim() ?? string.Empty,
        UploadPath = UploadPath ?? string.Empty,
        UseCustomUrl = UseCustomUrl,
        CustomUrl = CustomUrl?.Trim() ?? string.Empty
    };

    private void LoadSecrets()
    {
        if (_secrets == null) return;
        if (_secrets.GetSecret(BackblazeB2Provider.Id, _secretKey, BackblazeB2Provider.KeyIdSecret) is { Length: > 0 } keyId) ApplicationKeyId = keyId;
        if (_secrets.GetSecret(BackblazeB2Provider.Id, _secretKey, BackblazeB2Provider.KeySecret) is { Length: > 0 } key) ApplicationKey = key;
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        Persist(BackblazeB2Provider.KeyIdSecret, ApplicationKeyId);
        Persist(BackblazeB2Provider.KeySecret, ApplicationKey);
    }

    private void Persist(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _secrets!.DeleteSecret(BackblazeB2Provider.Id, _secretKey, name);
        else _secrets!.SetSecret(BackblazeB2Provider.Id, _secretKey, name, value.Trim());
    }
}
