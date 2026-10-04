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
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks.Processors;
using XerahS.Platform.Abstractions;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

internal static class AfterCaptureInteractionService
{
    public static Task<bool> ShowBeforeUploadAsync(TaskInfo info, CancellationToken token) =>
        Dispatcher.UIThread.InvokeAsync(() => new BeforeUploadWindow(info).ShowAsync(token));

    public static Task<QuickTaskMenuResult> ShowQuickTaskMenuAsync(TaskSettings settings, CancellationToken token) =>
        Dispatcher.UIThread.InvokeAsync(() => ShowQuickTaskMenuCoreAsync(settings, token));

    private static async Task<QuickTaskMenuResult> ShowQuickTaskMenuCoreAsync(TaskSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<QuickTaskMenuResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new Window
        {
            Width = 1, Height = 1, CanResize = false, ShowInTaskbar = false, Topmost = true,
            WindowDecorations = WindowDecorations.None, Background = Brushes.Transparent, Opacity = 0,
            RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme()
        };
        if (PlatformServices.IsInitialized && PlatformServices.Input.IsCursorPositionReliable)
        {
            var point = PlatformServices.Input.GetCursorPosition();
            host.WindowStartupLocation = WindowStartupLocation.Manual;
            host.Position = new PixelPoint(point.X, point.Y);
        }
        else host.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var accepted = QuickTaskMenuResult.Cancel;
        var menu = CreateQuickTaskMenu(settings, result => accepted = result);
        menu.Closed += (_, _) => { host.Close(); completion.TrySetResult(accepted); };
        host.Closed += (_, _) => { menu.Hide(); completion.TrySetResult(accepted); };
        host.Show();
        host.Activate();
        menu.ShowAt(host);
        Dispatcher.UIThread.Post(() => menu.Items.OfType<MenuItem>().First().Focus(), DispatcherPriority.Input);
        using var registration = token.Register(() => Dispatcher.UIThread.Post(host.Close));
        var result = await completion.Task;
        token.ThrowIfCancellationRequested();
        return result;
    }

    internal static MenuFlyout CreateQuickTaskMenu(TaskSettings settings, Action<QuickTaskMenuResult> select)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        void Add(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => { action(); menu.Hide(); };
            menu.Items.Add(item);
        }
        Add("Continue", () => select(QuickTaskMenuResult.Continue));
        menu.Items.Add(new Separator());
        foreach (var preset in SettingsManager.Settings.QuickTaskPresets ?? [])
        {
            if (!preset.IsValid) { menu.Items.Add(new Separator()); continue; }
            Add(preset.ToString(), () =>
            {
                settings.AfterCaptureJob = preset.AfterCapture;
                settings.AfterUploadJob = preset.AfterUpload;
                select(QuickTaskMenuResult.Preset);
            });
        }
        menu.Items.Add(new Separator());
        Add("Edit this menu...", () => Dispatcher.UIThread.Post(() => new QuickTaskMenuEditorWindow().Show()));
        menu.Items.Add(new Separator());
        Add("Cancel", () => select(QuickTaskMenuResult.Cancel));
        return menu;
    }

    public static Task<string?> SaveImageWithDialogAsync(TaskInfo info, CancellationToken token) =>
        Dispatcher.UIThread.InvokeAsync(() => SaveImageWithDialogCoreAsync(info, token));

    private static async Task<string?> SaveImageWithDialogCoreAsync(TaskInfo info, CancellationToken token)
    {
        var storage = StorageProviderResolver.Resolve() ?? throw new InvalidOperationException("The file picker is unavailable.");
        CaptureJobProcessor.EnsureImageFileName(info);
        string folder = SettingsManager.Settings.LastImageSaveDirectory;
        if (!Directory.Exists(folder)) folder = TaskHelpers.GetScreenshotsFolder(info.TaskSettings);
        string extension = Path.GetExtension(info.FileName).TrimStart('.');
        while (true)
        {
            token.ThrowIfCancellationRequested();
            using var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Choose a folder to save " + info.FileName,
                SuggestedFileName = info.FileName,
                DefaultExtension = extension,
                SuggestedStartLocation = Directory.Exists(folder) ? await storage.TryGetFolderFromPathAsync(folder) : null,
                FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }, FilePickerFileTypes.All],
                ShowOverwritePrompt = true
            });
            token.ThrowIfCancellationRequested();
            if (file == null) return null;
            string path = file.TryGetLocalPath() ?? throw new IOException("Choose a local folder to save the image.");
            try
            {
                await Task.Run(() => TaskHelpers.SaveImageToPathAsync(info.Metadata.Image!, path, info.TaskSettings), token);
                SettingsManager.Settings.LastImageSaveDirectory = Path.GetDirectoryName(path) ?? string.Empty;
                await SettingsManager.SaveApplicationConfigAsync();
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                UploadWorkflowService.ReportError(ex, "Could not save image. Choose another location.");
                folder = Path.GetDirectoryName(path) ?? folder;
            }
        }
    }
}
