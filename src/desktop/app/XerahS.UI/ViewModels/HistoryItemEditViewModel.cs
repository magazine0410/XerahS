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
using XerahS.History;

namespace XerahS.UI.ViewModels;

/// <summary>
/// The "Edit history item" dialog, as in ShareX: every field of the entry except its date, and the tags as one key=value per line.
/// </summary>
public partial class HistoryItemEditViewModel : ViewModelBase
{
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _date = string.Empty;
    [ObservableProperty] private string _type = string.Empty;
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _thumbnailUrl = string.Empty;
    [ObservableProperty] private string _deletionUrl = string.Empty;
    [ObservableProperty] private string _shortenedUrl = string.Empty;
    [ObservableProperty] private string _tags = string.Empty;

    public Action<bool>? CloseRequested { get; set; }

    public HistoryItemEditViewModel()
    {
    }

    public HistoryItemEditViewModel(HistoryItem item)
    {
        FileName = item.FileName ?? string.Empty;
        FilePath = item.FilePath ?? string.Empty;
        Date = item.DateTime.ToString("G");
        Type = item.Type ?? string.Empty;
        Host = item.Host ?? string.Empty;
        Url = item.URL ?? string.Empty;
        ThumbnailUrl = item.ThumbnailURL ?? string.Empty;
        DeletionUrl = item.DeletionURL ?? string.Empty;
        ShortenedUrl = item.ShortenedURL ?? string.Empty;
        Tags = HistoryHelpers.FormatTags(item.Tags);
    }

    /// <summary>Writes the edited fields to the entry, as ShareX's "Save" does.</summary>
    public void ApplyTo(HistoryItem item)
    {
        item.FileName = FileName ?? string.Empty;
        item.FilePath = FilePath ?? string.Empty;
        item.Type = Type ?? string.Empty;
        item.Host = Host ?? string.Empty;
        item.URL = Url ?? string.Empty;
        item.ThumbnailURL = ThumbnailUrl ?? string.Empty;
        item.DeletionURL = DeletionUrl ?? string.Empty;
        item.ShortenedURL = ShortenedUrl ?? string.Empty;
        item.Tags = HistoryHelpers.ParseTags(Tags);
    }

    [RelayCommand]
    private void Save() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
