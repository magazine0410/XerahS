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

namespace ShareX.ImageChest.Plugin;

public sealed class ImageChestProvider : UploaderProviderBase, IInstanceSecretBackupProvider
{
    internal const string Id = "imagechest";
    internal const string AccessTokenSecret = "accessToken";
    internal const string MissingTokenMessage =
        "Enter an Image Chest access token (Image Chest > your profile > Security > New Token).";

    public override string ProviderId => Id;
    public override string Name => "Image Chest";
    public override string Description => "Upload images to Image Chest";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image];
    public override Type ConfigModelType => typeof(ImageChestConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        ImageChestConfigModel config = DeserializeConfig(settingsJson);
        return new ImageChestUploader(config, ResolveSecret(config.SecretKey, AccessTokenSecret));
    }

    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            ImageChestConfigModel config = DeserializeConfig(settingsJson);
            return !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, AccessTokenSecret));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new()
    {
        [UploaderCategory.Image] = ["png", "jpg", "jpeg", "gif", "webp"]
    };

    public override object? CreateConfigView() => new Views.ImageChestConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.ImageChestConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson)
    {
        string? secretKey;
        try
        {
            secretKey = string.IsNullOrWhiteSpace(settingsJson) ? null : JObject.Parse(settingsJson).Value<string>(nameof(ImageChestConfigModel.SecretKey));
        }
        catch (JsonException)
        {
            return [];
        }

        return string.IsNullOrWhiteSpace(secretKey) ? [] : [new(ProviderId, secretKey, AccessTokenSecret)];
    }

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static ImageChestConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new ImageChestConfigModel()
            : JsonConvert.DeserializeObject<ImageChestConfigModel>(settingsJson) ?? new ImageChestConfigModel();
}
