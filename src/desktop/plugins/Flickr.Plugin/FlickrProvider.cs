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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Flickr.Plugin;

public sealed class FlickrProvider : UploaderProviderBase, IInstanceSecretBackupProvider
{
    internal const string Id = "flickr";
    internal const string ConsumerSecretName = "consumerSecret";
    internal const string UserTokenSecret = "userToken";
    internal const string UserSecretName = "userSecret";

    public override string ProviderId => Id;
    public override string Name => "Flickr";
    public override string Description => "Upload images to Flickr";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image];
    public override Type ConfigModelType => typeof(FlickrConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        FlickrConfigModel config = DeserializeConfig(settingsJson);
        return new FlickrUploader(config, ResolveSecret(config.SecretKey, ConsumerSecretName),
            ResolveSecret(config.SecretKey, UserTokenSecret), ResolveSecret(config.SecretKey, UserSecretName));
    }

    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            FlickrConfigModel config = DeserializeConfig(settingsJson);
            return GetConfigError(config, ResolveSecret(config.SecretKey, ConsumerSecretName),
                ResolveSecret(config.SecretKey, UserTokenSecret), ResolveSecret(config.SecretKey, UserSecretName)) == null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>As in ShareX (OAuthInfo.CheckOAuth), uploads need the app's key and secret and an authorized account.</summary>
    internal static string? GetConfigError(FlickrConfigModel config, string? consumerSecret, string? userToken, string? userSecret)
    {
        if (string.IsNullOrWhiteSpace(config.ConsumerKey) || string.IsNullOrWhiteSpace(consumerSecret))
            return "Enter your Flickr app's key and secret (create an app at https://www.flickr.com/services/apps/create/).";
        if (string.IsNullOrEmpty(userToken) || string.IsNullOrEmpty(userSecret))
            return "Authorize your Flickr account in the destination settings.";
        return null;
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new()
    {
        [UploaderCategory.Image] = ["png", "jpg", "jpeg", "gif", "tiff", "tif"]
    };

    public override object? CreateConfigView() => new Views.FlickrConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.FlickrConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson)
    {
        string? secretKey;
        try
        {
            secretKey = string.IsNullOrWhiteSpace(settingsJson) ? null : JObject.Parse(settingsJson).Value<string>(nameof(FlickrConfigModel.SecretKey));
        }
        catch (JsonException)
        {
            return [];
        }

        return string.IsNullOrWhiteSpace(secretKey)
            ? []
            : [new(ProviderId, secretKey, ConsumerSecretName), new(ProviderId, secretKey, UserTokenSecret), new(ProviderId, secretKey, UserSecretName)];
    }

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static FlickrConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new FlickrConfigModel()
            : JsonConvert.DeserializeObject<FlickrConfigModel>(settingsJson) ?? new FlickrConfigModel();
}
