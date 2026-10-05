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

namespace ShareX.Mega.Plugin;

public sealed class MegaProvider : UploaderProviderBase, IInstanceSecretBackupProvider
{
    internal const string Id = "mega";
    internal const string SessionIDSecret = "sessionId";
    internal const string MasterKeySecret = "masterKey";
    internal const string LoginRequiredMessage = "MEGA login is required. Log in from the destination settings.";

    public override string ProviderId => Id;
    public override string Name => "MEGA";
    public override string Description => "Upload files to MEGA";
    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.Image, UploaderCategory.Text, UploaderCategory.File];
    public override Type ConfigModelType => typeof(MegaConfigModel);
    public override UploaderCapabilities Capabilities => UploaderCapabilities.Cancellation | UploaderCapabilities.Progress;

    public override Uploader CreateInstance(string settingsJson)
    {
        MegaConfigModel config = DeserializeConfig(settingsJson);
        string masterKey = ResolveSecret(config.SecretKey, MasterKeySecret);
        byte[]? masterKeyBytes = null;
        try
        {
            if (!string.IsNullOrEmpty(masterKey)) masterKeyBytes = MegaUploader.FromBase64URL(masterKey);
        }
        catch (FormatException)
        {
        }

        return new MegaUploader(ResolveSecret(config.SecretKey, SessionIDSecret), masterKeyBytes) { FolderID = config.FolderID };
    }

    // As in ShareX, a login (the session ID and master key) is required.
    public override bool ValidateSettings(string settingsJson)
    {
        try
        {
            MegaConfigModel config = DeserializeConfig(settingsJson);
            return !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, SessionIDSecret)) &&
                !string.IsNullOrWhiteSpace(ResolveSecret(config.SecretKey, MasterKeySecret));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() => FileTypes.All();

    public override object? CreateConfigView() => new Views.MegaConfigView();

    public override IUploaderConfigViewModel? CreateConfigViewModel() => new ViewModels.MegaConfigViewModel();

    public IReadOnlyList<InstanceSecretReference> GetSecretReferences(string settingsJson) =>
        InstanceSecretHelper.GetReferences(ProviderId, settingsJson, SessionIDSecret, MasterKeySecret);

    private string ResolveSecret(string secretKey, string name) =>
        Secrets == null || string.IsNullOrWhiteSpace(secretKey) ? string.Empty : Secrets.GetSecret(ProviderId, secretKey, name) ?? string.Empty;

    internal static MegaConfigModel DeserializeConfig(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? new MegaConfigModel()
            : JsonConvert.DeserializeObject<MegaConfigModel>(settingsJson) ?? new MegaConfigModel();
}
