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

using CommunityToolkit.Mvvm.Input;
using ShareX.ImageEditor.Core.Persistence;
using XerahS.Common;
using XerahS.Core;
using XerahS.History;
using XerahS.UI.Services;

namespace XerahS.UI.ViewModels;

/// <summary>
/// The history management actions of ShareX's history windows: edit tag, edit item, rename file,
/// delete entries (with or without their files), import a folder, and statistics.
/// </summary>
public partial class HistoryViewModel
{
    /// <summary>Shows the "Edit history item" dialog; returns whether it was saved. Replaceable for tests.</summary>
    internal Func<HistoryItemEditViewModel, Task<bool>> ShowEditItemDialogAsync { get; set; } = vm =>
        ModalDialogHost.ShowAsync(vm, set => vm.CloseRequested = set, dismissResult: false, debugSource: nameof(HistoryItemEditViewModel));

    /// <summary>Shows the "Import folder" dialog until it is closed. Replaceable for tests.</summary>
    internal Func<HistoryImportViewModel, Task> ShowImportDialogAsync { get; set; } = vm =>
        ModalDialogHost.ShowAsync(vm, set => vm.CloseRequested = set, dismissResult: false, debugSource: nameof(HistoryImportViewModel));

    /// <summary>Shows the "History statistics" dialog until it is closed. Replaceable for tests.</summary>
    internal Func<HistoryStatsViewModel, Task> ShowStatsDialogAsync { get; set; } = vm =>
        ModalDialogHost.ShowAsync(vm, set => vm.CloseRequested = set, dismissResult: true, debugSource: nameof(HistoryStatsViewModel));

    /// <summary>"Edit tag...": sets the entry's Tag text, as in ShareX; an empty text removes it.</summary>
    [RelayCommand]
    private async Task EditTagAsync(HistoryItem? item)
    {
        if (item == null || item.Id <= 0) return;
        string? tag = await _coreDialogService.ShowInputAsync("Edit tag", "Enter a tag for this history item.", item.Tag ?? string.Empty);
        if (tag == null) return;
        try
        {
            item.Tag = tag;
            await PersistHistoryItemAsync(item);
            ReplaceLoadedHistoryItem(item);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to edit the history item tag");
            await _coreDialogService.ShowErrorAsync("Edit tag", "Could not save the tag. " + ex.Message);
        }
    }

    /// <summary>"Edit item...": edits every field of the entry except its date, and the tags as one key=value per line, as in ShareX.</summary>
    [RelayCommand]
    private async Task EditItemAsync(HistoryItem? item)
    {
        if (item == null || item.Id <= 0) return;
        var editor = new HistoryItemEditViewModel(item);
        if (!await ShowEditItemDialogAsync(editor)) return;
        try
        {
            editor.ApplyTo(item);
            await PersistHistoryItemAsync(item);
            ReplaceLoadedHistoryItem(item);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to edit the history item");
            await _coreDialogService.ShowErrorAsync("Edit history item", "Could not save the history item. " + ex.Message);
        }
    }

    /// <summary>
    /// "Rename file...": renames the file on disk, keeping its extension, and updates the entry, as in ShareX.
    /// As in ShareX, a missing file only renames the entry. Unlike ShareX, a failed rename leaves the entry unchanged.
    /// A default annotation sidecar (.xann) next to the file is renamed with it.
    /// </summary>
    [RelayCommand]
    private async Task RenameFileAsync(HistoryItem? item)
    {
        if (item == null || item.Id <= 0 || string.IsNullOrWhiteSpace(item.FilePath)) return;
        string oldName = Path.GetFileNameWithoutExtension(item.FilePath);
        string? input = await _coreDialogService.ShowInputAsync("Rename file", "Enter a new file name.", oldName);
        string newName = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newName) || newName.Equals(oldName, StringComparison.OrdinalIgnoreCase)) return;
        if (Path.HasExtension(item.FilePath)) newName += Path.GetExtension(item.FilePath);

        try
        {
            string oldPath = item.FilePath;
            string newPath = await Task.Run(() => RenameHistoryFile(item, newName));
            item.FileName = newName;
            item.FilePath = newPath;
            await PersistHistoryItemAsync(item);
            ReplaceLoadedHistoryItem(item);
            DebugHelper.WriteLine($"Renamed history file: {oldPath} -> {newPath}");
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to rename the history file");
            await _coreDialogService.ShowErrorAsync("Rename file", "Could not rename the file. " + ex.Message);
        }
    }

    private static string RenameHistoryFile(HistoryItem item, string newFileName)
    {
        string filePath = item.FilePath;
        if (!File.Exists(filePath)) return filePath;

        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string newPath = Path.Combine(directory, newFileName);
        File.Move(filePath, newPath);

        string oldSidecar = XannProjectFileService.GetDefaultSidecarPath(filePath);
        bool usesDefaultSidecar = string.IsNullOrWhiteSpace(item.AnnotationSidecarPath) ||
            string.Equals(item.AnnotationSidecarPath, oldSidecar, StringComparison.Ordinal);
        if (usesDefaultSidecar && File.Exists(oldSidecar))
        {
            string newSidecar = XannProjectFileService.GetDefaultSidecarPath(newPath);
            try
            {
                File.Move(oldSidecar, newSidecar);
                if (!string.IsNullOrWhiteSpace(item.AnnotationSidecarPath)) item.AnnotationSidecarPath = newSidecar;
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, $"The annotation sidecar '{oldSidecar}' could not be renamed with its image");
            }
        }

        return newPath;
    }

    /// <summary>
    /// Deletes the entry, or every selected entry when it is selected, after one confirmation worded as ShareX's.
    /// With <paramref name="deleteFiles"/>, the files (and their annotation sidecars) are deleted first; an entry whose
    /// file could not be deleted is kept and the error is shown.
    /// </summary>
    private async Task DeleteHistoryItemsAsync(HistoryItem? item, bool deleteFiles)
    {
        if (item == null) return;
        HistoryItem[] items = GetHistoryActionItems(item).ToArray();
        if (items.Length == 0) return;

        string noun = items.Length == 1
            ? (deleteFiles ? "this file" : "this item")
            : (deleteFiles ? $"these {items.Length:N0} files" : $"these {items.Length:N0} items");
        bool confirmed = await _coreDialogService.ShowConfirmationAsync(
            deleteFiles ? "Delete files?" : "Delete history items?",
            $"Do you really want to delete {noun}? This action cannot be undone.");
        if (!confirmed) return;

        var failures = new List<string>();
        if (deleteFiles)
        {
            List<HistoryItem> deletable = await Task.Run(() =>
            {
                var kept = new List<HistoryItem>();
                foreach (HistoryItem entry in items)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath))
                        {
                            File.Delete(entry.FilePath);
                            XannProjectFileService.TryDeleteSidecar(entry.FilePath);
                            if (!string.IsNullOrWhiteSpace(entry.AnnotationSidecarPath)) FileHelpers.DeleteFile(entry.AnnotationSidecarPath);
                        }

                        kept.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        DebugHelper.WriteException(ex, $"Failed to delete the history file '{entry.FilePath}'");
                        failures.Add($"{entry.FilePath}: {ex.Message}");
                    }
                }

                return kept;
            });
            items = deletable.ToArray();
        }

        if (items.Length > 0)
        {
            try
            {
                lock (_databaseLock)
                {
                    if (!_disposed) _historyManager.Delete(items);
                }

                var ocrIndex = new HistoryOcrIndexStore(SettingsManager.GetHistoryFilePath());
                foreach (HistoryItem entry in items)
                {
                    ocrIndex.Delete(entry.Id);
                    DebugHelper.WriteLine($"Deleted history item: {entry.FileName}");
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Failed to delete history items");
                failures.Add(ex.Message);
            }

            RemoveFromLoadedHistory(items);
        }

        if (failures.Count > 0)
        {
            await _coreDialogService.ShowErrorAsync(deleteFiles ? "Delete files" : "Delete history items",
                "Could not delete:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    /// <summary>"Import": adds every file of a folder and its subfolders as entries, as in ShareX, then reads the history again.</summary>
    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var dialog = new HistoryImportViewModel { ImportRequester = ImportHistoryFolderAsync };
        await ShowImportDialogAsync(dialog);
    }

    internal async Task<int> ImportHistoryFolderAsync(string folderPath, bool onlyImages, bool skipDuplicates)
    {
        List<HistoryItem>? loaded = _allHistoryItems?.ToList();
        int imported = await Task.Run(() =>
        {
            HashSet<string>? existing = null;
            if (skipDuplicates)
            {
                List<HistoryItem> all;
                lock (_databaseLock)
                {
                    if (_disposed) return 0;
                    all = loaded ?? _historyManager.GetHistoryItems(0, int.MaxValue);
                }

                // As in ShareX, paths are compared ignoring case.
                existing = all.Where(entry => !string.IsNullOrWhiteSpace(entry.FilePath))
                    .Select(entry => entry.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            List<HistoryItem> items = HistoryHelpers.CreateImportItems(folderPath, onlyImages, existing);
            if (items.Count == 0) return 0;
            lock (_databaseLock)
            {
                if (_disposed) return 0;
                if (!_historyManager.AppendHistoryItems(items))
                {
                    throw new IOException("The history could not be written. See the XerahS log for details.");
                }
            }

            return items.Count;
        });

        if (imported > 0)
        {
            ShowHistoryBackupFailureToastIfPresent(_historyManager.LastBackupFailureReason);
            CurrentPage = 1;
            await LoadHistoryAsync();
        }

        return imported;
    }

    /// <summary>"Stats": the counts of every entry (not only the filtered ones) by type, year, extension, host, and process name, as in ShareX.</summary>
    [RelayCommand]
    private async Task ShowStatsAsync()
    {
        var dialog = new HistoryStatsViewModel();
        Task shown = ShowStatsDialogAsync(dialog);
        List<HistoryItem>? loaded = _allHistoryItems?.ToList();
        try
        {
            dialog.Text = await Task.Run(() =>
            {
                List<HistoryItem> all;
                lock (_databaseLock)
                {
                    if (_disposed) return string.Empty;
                    all = loaded ?? _historyManager.GetHistoryItems(0, int.MaxValue);
                }

                return HistoryHelpers.OutputStats(all);
            });
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to calculate history statistics");
            dialog.Text = "Could not calculate statistics. " + ex.Message;
        }

        await shown;
    }
}
