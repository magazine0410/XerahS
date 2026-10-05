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

namespace ShareX.Flickr.Plugin.ViewModels;

/// <summary>A choice for one of ShareX's Flickr settings; an empty value leaves Flickr's own default.</summary>
public sealed record FlickrOption(string Label, string Value)
{
    public override string ToString() => Label;
}

public partial class FlickrConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    public IReadOnlyList<FlickrOption> YesNoOptions { get; } = [new("Default", ""), new("Yes", "1"), new("No", "0")];
    public IReadOnlyList<FlickrOption> SafetyLevelOptions { get; } = [new("Default", ""), new("Safe", "1"), new("Moderate", "2"), new("Restricted", "3")];
    public IReadOnlyList<FlickrOption> ContentTypeOptions { get; } = [new("Default", ""), new("Photo", "1"), new("Screenshot", "2"), new("Other", "3")];
    public IReadOnlyList<FlickrOption> HiddenOptions { get; } =
        [new("Default", ""), new("Show in global search results", "1"), new("Hide from public searches", "2")];

    [ObservableProperty] private string _consumerKey = string.Empty;
    [ObservableProperty] private string _consumerSecret = string.Empty;
    [ObservableProperty] private string _verificationCode = string.Empty;
    [ObservableProperty] private string _connectionStatus = "Not connected";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _directLink = true;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _photoDescription = string.Empty;
    [ObservableProperty] private string _tags = string.Empty;
    [ObservableProperty] private FlickrOption? _isPublic;
    [ObservableProperty] private FlickrOption? _isFriend;
    [ObservableProperty] private FlickrOption? _isFamily;
    [ObservableProperty] private FlickrOption? _safetyLevel;
    [ObservableProperty] private FlickrOption? _contentType;
    [ObservableProperty] private FlickrOption? _hidden;
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private string _userName = string.Empty;
    private string _userToken = string.Empty;
    private string _userSecret = string.Empty;
    private (string Token, string Secret)? _requestToken;
    private ISecretStore? _secrets;

    public FlickrConfigViewModel()
    {
        Select(new FlickrConfigModel());
    }

    [RelayCommand]
    private static void OpenAppRegistration() => URLHelpers.OpenURL("https://www.flickr.com/services/apps/create/");

    [RelayCommand]
    private async Task OpenAuthorizationPageAsync()
    {
        IsBusy = true;
        try
        {
            var uploader = new FlickrUploader(new FlickrConfigModel { ConsumerKey = ConsumerKey.Trim() }, ConsumerSecret.Trim());
            var authorization = await Task.Run(uploader.GetAuthorization);
            if (authorization is { } value)
            {
                _requestToken = (value.Token, value.TokenSecret);
                URLHelpers.OpenURL(value.AuthorizationUrl);
                ConnectionStatus = "Authorization page opened. Enter the code Flickr shows, then choose Complete Authorization.";
                StatusMessage = null;
            }
            else
            {
                StatusMessage = uploader.Errors.ToString();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CompleteAuthorizationAsync()
    {
        if (_requestToken is not { } requestToken)
        {
            StatusMessage = "Open the authorization page first.";
            return;
        }

        IsBusy = true;
        try
        {
            var uploader = new FlickrUploader(new FlickrConfigModel { ConsumerKey = ConsumerKey.Trim() }, ConsumerSecret.Trim());
            string code = VerificationCode;
            var access = await Task.Run(() => uploader.GetAccessToken(requestToken.Token, requestToken.Secret, code));
            if (access is { } value)
            {
                (_userToken, _userSecret, _userName) = value;
                _requestToken = null;
                VerificationCode = string.Empty;
                PersistSecrets();
                StatusMessage = null;
                UpdateConnectionStatus();
            }
            else
            {
                StatusMessage = uploader.Errors.ToString();
                ConnectionStatus = "Authorization failed";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _userToken = _userSecret = _userName = string.Empty;
        _requestToken = null;
        PersistSecrets();
        UpdateConnectionStatus();
    }

    public void LoadFromJson(string json)
    {
        try
        {
            FlickrConfigModel config = FlickrProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            ConsumerKey = config.ConsumerKey ?? string.Empty;
            _userName = config.UserName ?? string.Empty;
            Select(config);
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Flickr settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(CreateConfig(), Formatting.Indented);
    }

    public bool Validate()
    {
        StatusMessage = FlickrProvider.GetConfigError(CreateConfig(), ConsumerSecret, _userToken, _userSecret);
        if (StatusMessage != null) return false;
        PersistSecrets();
        return true;
    }

    public void SetContext(IProviderContext context)
    {
        _secrets = context.Secrets;
        LoadSecrets();
    }

    private void Select(FlickrConfigModel config)
    {
        DirectLink = config.DirectLink;
        Title = config.Title ?? string.Empty;
        PhotoDescription = config.Description ?? string.Empty;
        Tags = config.Tags ?? string.Empty;
        IsPublic = Find(YesNoOptions, config.IsPublic);
        IsFriend = Find(YesNoOptions, config.IsFriend);
        IsFamily = Find(YesNoOptions, config.IsFamily);
        SafetyLevel = Find(SafetyLevelOptions, config.SafetyLevel);
        ContentType = Find(ContentTypeOptions, config.ContentType);
        Hidden = Find(HiddenOptions, config.Hidden);
    }

    private static FlickrOption Find(IReadOnlyList<FlickrOption> options, string? value) =>
        options.FirstOrDefault(o => o.Value == (value ?? string.Empty)) ?? options[0];

    private FlickrConfigModel CreateConfig() => new()
    {
        SecretKey = _secretKey,
        ConsumerKey = ConsumerKey?.Trim() ?? string.Empty,
        UserName = _userName,
        DirectLink = DirectLink,
        Title = Title ?? string.Empty,
        Description = PhotoDescription ?? string.Empty,
        Tags = Tags ?? string.Empty,
        IsPublic = IsPublic?.Value ?? string.Empty,
        IsFriend = IsFriend?.Value ?? string.Empty,
        IsFamily = IsFamily?.Value ?? string.Empty,
        SafetyLevel = SafetyLevel?.Value ?? string.Empty,
        ContentType = ContentType?.Value ?? string.Empty,
        Hidden = Hidden?.Value ?? string.Empty
    };

    private void UpdateConnectionStatus() =>
        ConnectionStatus = string.IsNullOrEmpty(_userToken) || string.IsNullOrEmpty(_userSecret)
            ? "Not connected"
            : string.IsNullOrEmpty(_userName) ? "Connected" : "Connected as " + _userName;

    private void LoadSecrets()
    {
        if (_secrets != null)
        {
            string? consumerSecret = _secrets.GetSecret(FlickrProvider.Id, _secretKey, FlickrProvider.ConsumerSecretName);
            if (!string.IsNullOrEmpty(consumerSecret)) ConsumerSecret = consumerSecret;
            _userToken = _secrets.GetSecret(FlickrProvider.Id, _secretKey, FlickrProvider.UserTokenSecret) ?? string.Empty;
            _userSecret = _secrets.GetSecret(FlickrProvider.Id, _secretKey, FlickrProvider.UserSecretName) ?? string.Empty;
        }

        UpdateConnectionStatus();
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        Store(FlickrProvider.ConsumerSecretName, ConsumerSecret?.Trim());
        Store(FlickrProvider.UserTokenSecret, _userToken);
        Store(FlickrProvider.UserSecretName, _userSecret);
    }

    private void Store(string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) _secrets!.DeleteSecret(FlickrProvider.Id, _secretKey, name);
        else _secrets!.SetSecret(FlickrProvider.Id, _secretKey, name, value);
    }
}
