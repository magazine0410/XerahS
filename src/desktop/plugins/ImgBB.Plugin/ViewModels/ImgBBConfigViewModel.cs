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

namespace ShareX.ImgBB.Plugin.ViewModels;

public sealed record ImgBBExpirationOption(string Label, int Seconds)
{
    public override string ToString() => Label;
}

public partial class ImgBBConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    /// <summary>The choices ImgBB's own upload page offers.</summary>
    public IReadOnlyList<ImgBBExpirationOption> ExpirationOptions { get; } =
    [
        new("Don't autodelete", 0),
        new("After 5 minutes", 300),
        new("After 15 minutes", 900),
        new("After 30 minutes", 1800),
        new("After 1 hour", 3600),
        new("After 3 hours", 10800),
        new("After 6 hours", 21600),
        new("After 12 hours", 43200),
        new("After 1 day", 86400),
        new("After 2 days", 172800),
        new("After 3 days", 259200),
        new("After 4 days", 345600),
        new("After 5 days", 432000),
        new("After 6 days", 518400),
        new("After 1 week", 604800),
        new("After 2 weeks", 1209600),
        new("After 3 weeks", 1814400),
        new("After 1 month", 2592000),
        new("After 2 months", 5184000),
        new("After 3 months", 7776000),
        new("After 4 months", 10368000),
        new("After 5 months", 12960000),
        new("After 6 months", 15552000)
    ];

    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private bool _directLink = true;
    [ObservableProperty] private ImgBBExpirationOption? _selectedExpiration;
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    public ImgBBConfigViewModel()
    {
        _selectedExpiration = ExpirationOptions[0];
    }

    [RelayCommand]
    private static void OpenApiKeyPage() => URLHelpers.OpenURL("https://api.imgbb.com/");

    public void LoadFromJson(string json)
    {
        try
        {
            ImgBBConfigModel config = ImgBBProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            DirectLink = config.DirectLink;
            SelectedExpiration = ExpirationOptions.FirstOrDefault(o => o.Seconds == config.Expiration) ??
                ExpirationOptions.Where(o => o.Seconds >= config.Expiration).MinBy(o => o.Seconds) ?? ExpirationOptions[^1];
            LoadSecrets();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the ImgBB settings.";
        }
    }

    public string ToJson()
    {
        PersistSecrets();
        return JsonConvert.SerializeObject(new ImgBBConfigModel
        {
            SecretKey = _secretKey,
            DirectLink = DirectLink,
            Expiration = SelectedExpiration?.Seconds ?? 0
        }, Formatting.Indented);
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = ImgBBProvider.MissingApiKeyMessage;
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
        string? stored = _secrets.GetSecret(ImgBBProvider.Id, _secretKey, ImgBBProvider.ApiKeySecret);
        if (!string.IsNullOrEmpty(stored)) ApiKey = stored;
    }

    private void PersistSecrets()
    {
        if (_secrets == null) return;
        if (string.IsNullOrWhiteSpace(ApiKey)) _secrets.DeleteSecret(ImgBBProvider.Id, _secretKey, ImgBBProvider.ApiKeySecret);
        else _secrets.SetSecret(ImgBBProvider.Id, _secretKey, ImgBBProvider.ApiKeySecret, ApiKey.Trim());
    }
}
