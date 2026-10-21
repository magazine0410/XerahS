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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XerahS.Common;

namespace XerahS.UI.ViewModels;

/// <summary>
/// The "Import folder" dialog, as in ShareX: a folder, "Only import image files" (off), and "Skip duplicate files" (on).
/// The dialog stays open after an import and shows how many files were imported.
/// </summary>
public partial class HistoryImportViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _folderPath = string.Empty;

    [ObservableProperty] private bool _onlyImages;
    [ObservableProperty] private bool _skipDuplicates = true;
    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isBusy;

    public Func<Task<string?>>? BrowseFolderRequester { get; set; }

    /// <summary>Imports the folder (path, only images, skip duplicates) and returns how many entries were added.</summary>
    public Func<string, bool, bool, Task<int>>? ImportRequester { get; set; }

    public Action<bool>? CloseRequested { get; set; }

    public bool CanImport => !IsBusy && !string.IsNullOrWhiteSpace(FolderPath);

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (BrowseFolderRequester == null) return;
        string? path = await BrowseFolderRequester();
        if (!string.IsNullOrWhiteSpace(path)) FolderPath = path;
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task Import()
    {
        string folder = FolderPath?.Trim() ?? string.Empty;
        if (!Directory.Exists(folder))
        {
            Status = "The selected folder does not exist.";
            return;
        }

        if (ImportRequester == null) return;
        IsBusy = true;
        Status = "Importing files...";
        try
        {
            int imported = await ImportRequester(folder, OnlyImages, SkipDuplicates);
            Status = $"Successfully imported {imported:N0} files.";
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "History import failed");
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(false);
}
