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
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.ImgFish.Plugin;

public sealed class ImgFishProvider : UploaderProviderBase, IInstanceSecretBackupProvider, IInstanceSecretMigrator
{
    internal const string Id = "imgfish";
    internal const string ApiKeySecret = "apiKey";

    public override string ProviderId => Id;
    public override string Name => "img.fish";
    public override string Description => "Upload images, videos, and audio to img.fish";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.File];
    public override Type ConfigModelType => typeof(ImgFishConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        ImgFishConfigModel config = DeserializeConfig(settingsJson);
        return new ImgFishUploader(ResolveSecret(config.SecretKey, ApiKeySecret), config.FileIDLength);
    }

    // As in ShareX, the API key is optional and the file ID length must be 8 to 32.
    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            ImgFishConfigModel config = DeserializeConfig(settingsJson);
            return config.FileIDLength is >= ImgFishConfigModel.MinFileIDLength and <= ImgFishConfigModel.MaxFileIDLength;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // img.fish accepts images, videos, and audio.
    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes()
    {
        string[] media = ["png", "jpg", "jpeg", "gif", "bmp", "webp", "avif", "mp4", "webm", "mov", "mkv", "mp3", "ogg", "wav", "flac", "m4a"];
        return new() { [UploaderCategory.Image] = media, [UploaderCategory.File] = media };
    }

    public override object? CreateConfigView() => new Views.ImgFishConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.ImgFishConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson) =>
        InstanceSecretHelper.GetReferences(ProviderId, settingsJson, ApiKeySecret);

    public bool TryMigrateSecrets(string settingsJson, ISecretStore secrets, out string updatedSettingsJson, out int migratedSecretCount) =>
        InstanceSecretHelper.MigratePlaintext(settingsJson, secrets, ProviderId,
            new Dictionary<string, string> { [nameof(ImgFishConfigModel.APIKey)] = ApiKeySecret }, out updatedSettingsJson, out migratedSecretCount);

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static ImgFishConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new ImgFishConfigModel()
            : JsonConvert.DeserializeObject<ImgFishConfigModel>(settingsJson) ?? new ImgFishConfigModel();
}
