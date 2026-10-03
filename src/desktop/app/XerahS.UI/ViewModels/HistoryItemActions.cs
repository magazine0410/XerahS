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
using XerahS.History;

namespace XerahS.UI.ViewModels;

/// <summary>Actions which need a durable history row, rather than a completion toast.</summary>
public sealed class HistoryItemActions : ObservableObject
{
    private readonly HistoryViewModel _viewModel;
    private readonly HistoryItem _item;

    public HistoryItemActions(HistoryViewModel viewModel, HistoryItem item)
    {
        _viewModel = viewModel;
        _item = item;
        CopyCommand = new AsyncRelayCommand<HistoryCopyFormat>(format => _viewModel.CopyHistoryItemsAsync(_item, format), CanCopy);
        OpenShortenedUrlCommand = new RelayCommand(() => _viewModel.OpenHistoryUrl(_item.ShortenedURL), () => !string.IsNullOrWhiteSpace(_item.ShortenedURL));
        OpenThumbnailUrlCommand = new RelayCommand(() => _viewModel.OpenHistoryUrl(_item.ThumbnailURL), () => !string.IsNullOrWhiteSpace(_item.ThumbnailURL));
        OpenDeletionUrlCommand = new RelayCommand(() => _viewModel.OpenHistoryUrl(_item.DeletionURL), () => !string.IsNullOrWhiteSpace(_item.DeletionURL));
        ToggleFavoriteCommand = new AsyncRelayCommand(() => _viewModel.ToggleFavoriteCommand.ExecuteAsync(_item), () => _item.Id > 0);
        ShowErrorsCommand = new AsyncRelayCommand(() => _viewModel.ShowHistoryErrorsAsync(_item), () => _item.HasErrors);
    }

    public string FavoriteHeader => _item.Favorite ? "Unfavorite" : "Favorite";
    public IAsyncRelayCommand<HistoryCopyFormat> CopyCommand { get; }
    public IRelayCommand OpenShortenedUrlCommand { get; }
    public IRelayCommand OpenThumbnailUrlCommand { get; }
    public IRelayCommand OpenDeletionUrlCommand { get; }
    public IAsyncRelayCommand ToggleFavoriteCommand { get; }
    public IAsyncRelayCommand ShowErrorsCommand { get; }

    private bool CanCopy(HistoryCopyFormat format)
    {
        IReadOnlyList<HistoryItem> items = _viewModel.GetHistoryActionItems(_item);
        return format switch
        {
            HistoryCopyFormat.File => items.Any(item => File.Exists(item.FilePath)),
            HistoryCopyFormat.TextContents => items.Count == 1 && File.Exists(_item.FilePath) && FileHelpers.IsTextFile(_item.FilePath),
            HistoryCopyFormat.Image or HistoryCopyFormat.ImageDimensions => items.Count == 1 && File.Exists(_item.FilePath) && FileHelpers.IsImageFile(_item.FilePath),
            _ => items.Any(item => !string.IsNullOrWhiteSpace(HistoryCopyText.GetValue(item, format)))
        };
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(FavoriteHeader));
        CopyCommand.NotifyCanExecuteChanged();
        OpenShortenedUrlCommand.NotifyCanExecuteChanged();
        OpenThumbnailUrlCommand.NotifyCanExecuteChanged();
        OpenDeletionUrlCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        ShowErrorsCommand.NotifyCanExecuteChanged();
    }
}
