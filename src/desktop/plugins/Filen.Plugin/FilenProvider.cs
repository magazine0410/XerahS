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

namespace ShareX.Filen.Plugin;

/// <summary>Filen (filen.io), an end-to-end encrypted cloud storage service. ShareX has no Filen uploader.</summary>
public sealed class FilenProvider : UploaderProviderBase, IInstanceSecretBackupProvider
{
    internal const string Id = "filen";
    internal const string ApiKeySecret = "apiKey";
    internal const string KeysSecret = "keys";
    internal const string LoginRequiredMessage = "Filen login is required. Log in from the destination settings.";

    public override string ProviderId => Id;
    public override string Name => "Filen";
    public override string Description => "Upload files to Filen";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.Text, UploaderCategory.File];
    public override Type ConfigModelType => typeof(FilenConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        FilenConfigModel config = DeserializeConfig(settingsJson);
        return new FilenUploader(ResolveSecret(config.SecretKey, ApiKeySecret), FilenKeys.Deserialize(ResolveSecret(config.SecretKey, KeysSecret)))
        {
            FolderID = config.FolderID,
            LinkExpiration = config.LinkExpiration,
            ShowDownloadButton = config.ShowDownloadButton
        };
    }

    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            FilenConfigModel config = DeserializeConfig(settingsJson);
            return !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, ApiKeySecret)) &&
                FilenKeys.Deserialize(ResolveSecret(config.SecretKey, KeysSecret)) != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => FileTypes.All();

    public override object? CreateConfigView() => new Views.FilenConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.FilenConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson) =>
        InstanceSecretHelper.GetReferences(ProviderId, settingsJson, ApiKeySecret, KeysSecret);

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static FilenConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new FilenConfigModel()
            : JsonConvert.DeserializeObject<FilenConfigModel>(settingsJson) ?? new FilenConfigModel();
}
