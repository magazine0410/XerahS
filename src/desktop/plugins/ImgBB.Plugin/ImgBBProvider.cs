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

namespace ShareX.ImgBB.Plugin;

public sealed class ImgBBProvider : UploaderProviderBase, IInstanceSecretBackupProvider
{
    internal const string Id = "imgbb";
    internal const string ApiKeySecret = "apiKey";
    internal const string MissingApiKeyMessage = "Enter your ImgBB API key (from https://api.imgbb.com/).";

    public override string ProviderId => Id;
    public override string Name => "ImgBB";
    public override string Description => "Upload images to ImgBB";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image];
    public override Type ConfigModelType => typeof(ImgBBConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        ImgBBConfigModel config = DeserializeConfig(settingsJson);
        return new ImgBBUploader(config, ResolveSecret(config.SecretKey, ApiKeySecret));
    }

    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            ImgBBConfigModel config = DeserializeConfig(settingsJson);
            return !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, ApiKeySecret));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ImgBB accepts images up to 32 MB.
    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => new()
    {
        [UploaderCategory.Image] = ["png", "jpg", "jpeg", "gif", "bmp", "webp", "tiff", "tif", "heic", "avif"]
    };

    public override object? CreateConfigView() => new Views.ImgBBConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.ImgBBConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson)
    {
        string? secretKey;
        try
        {
            secretKey = string.IsNullOrWhiteSpace(settingsJson) ? null : JObject.Parse(settingsJson).Value<string>(nameof(ImgBBConfigModel.SecretKey));
        }
        catch (JsonException)
        {
            return [];
        }

        return string.IsNullOrWhiteSpace(secretKey) ? [] : [new(ProviderId, secretKey, ApiKeySecret)];
    }

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static ImgBBConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new ImgBBConfigModel()
            : JsonConvert.DeserializeObject<ImgBBConfigModel>(settingsJson) ?? new ImgBBConfigModel();
}
