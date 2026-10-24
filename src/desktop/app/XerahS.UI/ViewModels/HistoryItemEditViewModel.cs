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

    // The values shown when the dialog opened, to tell which ones the user changed.
    private readonly string[] _shownFields = [];
    private readonly Dictionary<string, string?> _shownTags = new();

    public HistoryItemEditViewModel()
    {
        _shownFields = Fields();
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
        _shownFields = Fields();
        _shownTags = HistoryHelpers.ParseTags(Tags);
    }

    /// <summary>
    /// Writes the fields the user changed to the entry, as ShareX's "Save" does. Unchanged fields and tags are left as
    /// the entry has them, so that values saved while the dialog was open (an upload's URLs, the OCR text) are kept.
    /// </summary>
    public void ApplyTo(HistoryItem item)
    {
        string[] fields = Fields();
        if (fields[0] != _shownFields[0]) item.FileName = fields[0];
        if (fields[1] != _shownFields[1]) item.FilePath = fields[1];
        if (fields[2] != _shownFields[2]) item.Type = fields[2];
        if (fields[3] != _shownFields[3]) item.Host = fields[3];
        if (fields[4] != _shownFields[4]) item.URL = fields[4];
        if (fields[5] != _shownFields[5]) item.ThumbnailURL = fields[5];
        if (fields[6] != _shownFields[6]) item.DeletionURL = fields[6];
        if (fields[7] != _shownFields[7]) item.ShortenedURL = fields[7];

        Dictionary<string, string?> tags = HistoryHelpers.ParseTags(Tags);
        item.Tags ??= new Dictionary<string, string?>();
        foreach (string removed in _shownTags.Keys.Where(key => !tags.ContainsKey(key)))
        {
            item.Tags.Remove(removed);
        }

        foreach ((string key, string? value) in tags)
        {
            if (!_shownTags.TryGetValue(key, out string? shown) || shown != value) item.Tags[key] = value;
        }
    }

    private string[] Fields() =>
    [
        FileName ?? string.Empty, FilePath ?? string.Empty, Type ?? string.Empty, Host ?? string.Empty,
        Url ?? string.Empty, ThumbnailUrl ?? string.Empty, DeletionUrl ?? string.Empty, ShortenedUrl ?? string.Empty
    ];

    [RelayCommand]
    private void Save() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
