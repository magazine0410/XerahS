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
using System.Collections.ObjectModel;
using XerahS.Core;
using XerahS.History;

namespace XerahS.UI.ViewModels;

public partial class HistoryViewModel
{
    private HistorySettings _historySettings = null!;
    private ImageHistorySettings _imageSettings = null!;
    private bool _loadingPreferences;
    private bool _preferencesDirty;

    [ObservableProperty] private bool _filterDate;
    [ObservableProperty] private DateTimeOffset? _fromDate = DateTime.Today;
    [ObservableProperty] private DateTimeOffset? _toDate = DateTime.Today;
    [ObservableProperty] private bool _filterType;
    [ObservableProperty] private string? _selectedType;
    [ObservableProperty] private bool _filterHost;
    [ObservableProperty] private string _hostFilter = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _availableTypes = new();
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _rememberSearchText;
    [ObservableProperty] private bool _autoLoadMoreItems;
    [ObservableProperty] private bool _hideMissingFiles;
    [ObservableProperty] private bool _imagesOnly;
    [ObservableProperty] private int _thumbnailWidth;
    [ObservableProperty] private int _thumbnailHeight;
    [ObservableProperty] private int _imageBatchSize;
    [ObservableProperty] private ImageHistoryThumbnailHeaderPosition _thumbnailHeaderPosition;

    public ImageHistoryThumbnailHeaderPosition[] ThumbnailHeaderPositions { get; } = Enum.GetValues<ImageHistoryThumbnailHeaderPosition>();
    public bool ShowTopThumbnailHeader => ThumbnailHeaderPosition == ImageHistoryThumbnailHeaderPosition.Top;
    public bool ShowBottomThumbnailHeader => ThumbnailHeaderPosition == ImageHistoryThumbnailHeaderPosition.Bottom;
    public double ThumbnailCardWidth => ThumbnailWidth + 26;
    public bool CanLoadMoreItems => IsGridView && HistoryItems.Count < TotalItems;
    public string ImageBatchInfo => $"{HistoryItems.Count} / {TotalItems} items";
    public bool CanUseAdvancedFilters => !FavoritesOnly;
    public bool CanSearch => !FavoritesOnly || IsGridView;
    public bool IsDateRangeInvalid => FilterDate && FromDate?.Date > ToDate?.Date;

    private void InitializeHistoryOptions()
    {
        _historySettings = SettingsManager.Settings.HistorySettings ??= new HistorySettings();
        _imageSettings = SettingsManager.Settings.ImageHistorySettings ??= new ImageHistorySettings();
        _loadingPreferences = true;
        AutoLoadMoreItems = _imageSettings.AutoLoadMoreItems;
        HideMissingFiles = _imageSettings.FilterMissingFiles;
        ImagesOnly = _imageSettings.ImageOnly;
        ThumbnailWidth = Math.Clamp(_imageSettings.ThumbnailSize.Width, 32, 800);
        ThumbnailHeight = Math.Clamp(_imageSettings.ThumbnailSize.Height, 32, 800);
        ImageBatchSize = Math.Clamp(_imageSettings.MaxItemCount, 0, 100000);
        ThumbnailHeaderPosition = _imageSettings.ThumbnailHeaderPosition;
        RestoreViewPreferences();
    }

    private void RestoreViewPreferences()
    {
        _loadingPreferences = true;
        RememberSearchText = IsGridView ? _imageSettings.RememberSearchText : _historySettings.RememberSearchText;
        SearchText = RememberSearchText ? (IsGridView ? _imageSettings.SearchText : _historySettings.SearchText) : string.Empty;
        FavoritesOnly = IsGridView ? _imageSettings.Favorites : _historySettings.Favorites;
        _loadingPreferences = false;
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(CanUseAdvancedFilters));
    }

    partial void OnIsGridViewChanged(bool value)
    {
        RestoreViewPreferences();
        ScheduleHistoryFilter();
        NotifyImageBatchChanged();
    }

    partial void OnFilterDateChanged(bool value) { OnPropertyChanged(nameof(IsDateRangeInvalid)); ScheduleHistoryFilter(); }
    partial void OnFromDateChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(IsDateRangeInvalid)); ScheduleHistoryFilter(); }
    partial void OnToDateChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(IsDateRangeInvalid)); ScheduleHistoryFilter(); }
    partial void OnFilterTypeChanged(bool value) => ScheduleHistoryFilter();
    partial void OnSelectedTypeChanged(string? value) => ScheduleHistoryFilter();
    partial void OnFilterHostChanged(bool value) => ScheduleHistoryFilter();
    partial void OnHostFilterChanged(string value) => ScheduleHistoryFilter();

    partial void OnFavoritesOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseAdvancedFilters));
        OnPropertyChanged(nameof(CanSearch));
        if (_loadingPreferences) return;
        if (IsGridView) _imageSettings.Favorites = value;
        else _historySettings.Favorites = value;
        _preferencesDirty = true;
        ScheduleHistoryFilter();
    }

    partial void OnRememberSearchTextChanged(bool value)
    {
        if (_loadingPreferences) return;
        if (IsGridView) _imageSettings.RememberSearchText = value;
        else _historySettings.RememberSearchText = value;
        StoreSearchPreference();
        _preferencesDirty = true;
        ScheduleHistoryFilter();
    }

    private void StoreSearchPreference()
    {
        string value = RememberSearchText ? SearchText : string.Empty;
        if (IsGridView) _imageSettings.SearchText = value;
        else _historySettings.SearchText = value;
        if (RememberSearchText) _preferencesDirty = true;
    }

    partial void OnAutoLoadMoreItemsChanged(bool value) => StoreImageOptions();
    partial void OnHideMissingFilesChanged(bool value) => StoreImageOptions();
    partial void OnImagesOnlyChanged(bool value) => StoreImageOptions();
    partial void OnThumbnailWidthChanged(int value) { OnPropertyChanged(nameof(ThumbnailCardWidth)); StoreImageOptions(); }
    partial void OnThumbnailHeightChanged(int value) => StoreImageOptions();
    partial void OnImageBatchSizeChanged(int value) => StoreImageOptions();
    partial void OnThumbnailHeaderPositionChanged(ImageHistoryThumbnailHeaderPosition value)
    {
        OnPropertyChanged(nameof(ShowTopThumbnailHeader));
        OnPropertyChanged(nameof(ShowBottomThumbnailHeader));
        StoreImageOptions();
    }

    private void StoreImageOptions()
    {
        if (_loadingPreferences) return;
        _imageSettings.AutoLoadMoreItems = AutoLoadMoreItems;
        _imageSettings.FilterMissingFiles = HideMissingFiles;
        _imageSettings.ImageOnly = ImagesOnly;
        _imageSettings.ThumbnailSize = new System.Drawing.Size(Math.Clamp(ThumbnailWidth, 32, 800), Math.Clamp(ThumbnailHeight, 32, 800));
        _imageSettings.ThumbnailHeaderPosition = ThumbnailHeaderPosition;
        _imageSettings.MaxItemCount = Math.Clamp(ImageBatchSize, 0, 100000);
        _preferencesDirty = true;
        ScheduleHistoryFilter();
    }

    private async Task SaveHistoryPreferencesAsync()
    {
        if (!_preferencesDirty || _disposed) return;
        _preferencesDirty = false;
        if (!await SettingsManager.SaveApplicationConfigAsync())
        {
            _preferencesDirty = true;
            await _coreDialogService.ShowErrorAsync("History settings", "Could not save history settings. Check the settings folder is writable.");
        }
    }

    private void NotifyImageBatchChanged()
    {
        OnPropertyChanged(nameof(CanLoadMoreItems));
        OnPropertyChanged(nameof(ImageBatchInfo));
    }

    [RelayCommand]
    private void ResetFilters()
    {
        FilterDate = false;
        FilterType = false;
        FilterHost = false;
        FromDate = ToDate = DateTime.Today;
        SelectedType = null;
        HostFilter = string.Empty;
        // As in ShareX, Reset filters leaves Favorites and the search text alone.
        ScheduleHistoryFilter();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(HistoryItem? item)
    {
        if (item == null || item.Id <= 0) return;
        HistoryItem[] items = SelectedHistoryItems.Contains(item) ? SelectedHistoryItems.ToArray() : [item];
        try
        {
            // Read the current row so a concurrent upload/publish update is not overwritten.
            Dictionary<long, bool> saved = await Task.Run(() =>
            {
                var favorites = new Dictionary<long, bool>();
                lock (_databaseLock)
                {
                    if (_disposed) return favorites;
                    foreach (HistoryItem selected in items)
                    {
                        HistoryItem? current = _historyManager.GetHistoryItem(selected.Id);
                        if (current == null) continue;
                        current.Favorite = !current.Favorite;
                        _historyManager.Edit(current);
                        favorites[selected.Id] = current.Favorite;
                    }
                }
                return favorites;
            });

            // As in ShareX, only the Favorites view is filtered again. Otherwise the stars update in
            // place, and the loaded batches, scroll position, and selection stay as they were.
            foreach (HistoryItem selected in items)
            {
                if (saved.TryGetValue(selected.Id, out bool favorite)) selected.Favorite = favorite;
            }
            if (FavoritesOnly) await FilterHistoryAsync();
        }
        catch (Exception ex)
        {
            await _coreDialogService.ShowErrorAsync("Favorites", "Could not update favorites. " + ex.Message);
        }
    }
}
