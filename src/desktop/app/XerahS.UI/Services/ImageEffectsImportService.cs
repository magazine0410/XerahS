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
using Microsoft.Extensions.DependencyInjection;
using XerahS.Bootstrap;
using XerahS.Core;
using XerahS.Core.Helpers;

namespace XerahS.UI.Services;

/// <summary>ShareX's ImportImageEffect, for a .sxie or .xsie file opened from the file manager.</summary>
public static class ImageEffectsImportService
{
    public static async Task ImportAsync(string path)
    {
        var preset = ImageEffectPresetImporter.LoadPresetFile(path, out var skipped)
            ?? throw new InvalidDataException("Could not import the image effects file.");
        TaskSettings settings = SettingsManager.DefaultTaskSettings;
        var dialogs = new AvaloniaDialogServiceAdapter();

        // ShareX adds the import to its list of presets. XerahS keeps one preset per workflow, so the
        // current one would be lost: ask first when it has effects.
        var current = settings.ImageSettings.ImageEffectsPreset;
        if (current?.Effects is { Count: > 0 } effects &&
            !await dialogs.ShowConfirmationAsync("Import image effects",
                $"XerahS keeps one image effects preset per workflow. Replace \"{current.Name}\" ({effects.Count} effects) with \"{preset.Name}\"?"))
        {
            return;
        }

        if (skipped.Count > 0)
        {
            await dialogs.ShowWarningAsync("Import image effects", "Unsupported effects were skipped: " + string.Join(", ", skipped));
        }

        // As in ShareX, the image effects window opens with the imported preset.
        var taskManager = (Application.Current as App)?.ServiceProvider?.GetRequiredService<IDesktopTaskManager>()
            ?? throw new InvalidOperationException("Task manager unavailable.");
        var window = ImageEditingToolService.CreateImageEffectsWindow(null, null, settings, taskManager);
        window.ViewModel.ApplyImportedPreset(preset);
        window.Closed += async (_, _) => await ImageEditorOptionsStore.PersistAsync();
        window.Show();

        if (!settings.AfterCaptureJob.HasFlag(AfterCaptureTasks.AddImageEffects) &&
            await dialogs.ShowConfirmationAsync("XerahS", "Would you like to enable image effects?"))
        {
            settings.AfterCaptureJob |= AfterCaptureTasks.AddImageEffects;
        }

        await ImageEditorOptionsStore.PersistAsync();
    }
}
