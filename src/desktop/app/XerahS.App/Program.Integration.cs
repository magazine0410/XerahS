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

using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Helpers;
using XerahS.Core.Services;
using XerahS.Platform.Abstractions;
using XerahS.Services.Abstractions;
using XerahS.UI.Services;

namespace XerahS.App;

internal partial class Program
{
    private static readonly SemaphoreSlim IntegrationGate = new(1, 1);

    private static async Task ProcessIntegrationRequestsAsync(IReadOnlyList<IntegrationRequest> requests)
    {
        await IntegrationGate.WaitAsync();
        try
        {
            foreach (var request in requests)
            {
                try
                {
                    if (!File.Exists(request.Path)) throw new FileNotFoundException("The selected file does not exist.", request.Path);
                    var factory = UiViewModelFactoryAccessor.GetRequired();
                    switch (request.Action)
                    {
                        case IntegrationAction.ImageEditor:
                            using (var image = SKBitmap.Decode(request.Path) ?? throw new InvalidDataException("The selected file is not a supported image."))
                            using (await PlatformServices.UI.ShowEditorAsync(image, request.Path)) { }
                            break;
                        case IntegrationAction.CustomUploader:
                            await factory.CreateDestinationSettingsViewModel().ImportCustomUploaderFileAsync(request.Path);
                            break;
                        case IntegrationAction.ImageEffect:
                            await ImageEffectsImportService.ImportAsync(request.Path);
                            break;
                        case IntegrationAction.NativeMessaging:
                            string json;
                            try
                            {
                                if (new FileInfo(request.Path).Length > NativeMessagingHost.MaximumInputBytes) throw new InvalidDataException("Native message is too large.");
                                json = await File.ReadAllTextAsync(request.Path);
                            }
                            finally { BrowserNativeMessagingHost.DeleteConsumedInput(request.Path); }
                            var manager = (Application.Current as XerahS.UI.App)?.ServiceProvider?.GetRequiredService<ITaskManager>()
                                ?? throw new InvalidOperationException("Task manager unavailable.");
                            await BrowserExtensionService.ProcessAsync(json, manager, CloneTaskSettings(SettingsManager.DefaultTaskSettings));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    DebugHelper.WriteException(ex, "Desktop integration");
                    await new AvaloniaDialogServiceAdapter().ShowErrorAsync("Desktop integration failed", ex.Message);
                }
            }
        }
        finally { IntegrationGate.Release(); }
    }

    private static async Task UploadPathsFromIntegrationAsync(IReadOnlyList<string> files, IReadOnlyList<string> folders)
    {
        try
        {
            var paths = await Task.Run(() => IntegrationArguments.ExpandUploadPaths(files, folders));
            if (paths.Count > 10 && SettingsManager.Settings.ShowMultiUploadWarning &&
                !await new AvaloniaDialogServiceAdapter().ShowConfirmationAsync("Upload files", $"Are you sure you want to upload {paths.Count} files?")) return;
            await UploadFilesFromIntegrationAsync(paths);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Upload folder from desktop integration");
            await new AvaloniaDialogServiceAdapter().ShowErrorAsync("Upload failed", ex.Message);
        }
    }
}
