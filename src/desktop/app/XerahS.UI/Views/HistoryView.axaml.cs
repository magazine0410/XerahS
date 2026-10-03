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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using Avalonia.Markup.Xaml;
using XerahS.History;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views
{
    public partial class HistoryView : PageView
    {
        private readonly ListBox? _gridHistoryListBox;
        private readonly ListBox? _listHistoryListBox;
        private bool _isSynchronizingSelection;
        private ScrollViewer? _gridScrollViewer;
        private HistoryViewModel? _observedViewModel;

        private void OnHistoryLoaded(object? sender, RoutedEventArgs e)
        {
            if (_observedViewModel != null) return;
            _observedViewModel = DataContext as HistoryViewModel;
            if (_observedViewModel != null) _observedViewModel.PropertyChanged += OnHistoryPropertyChanged;
            _gridScrollViewer = _gridHistoryListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_gridScrollViewer != null) _gridScrollViewer.ScrollChanged += OnHistoryScrollChanged;
            QueueLoadMoreCheck();
        }

        private void OnHistoryUnloaded(object? sender, RoutedEventArgs e)
        {
            if (_observedViewModel != null) _observedViewModel.PropertyChanged -= OnHistoryPropertyChanged;
            if (_gridScrollViewer != null) _gridScrollViewer.ScrollChanged -= OnHistoryScrollChanged;
            _observedViewModel = null;
            _gridScrollViewer = null;
        }

        private void OnHistoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HistoryViewModel.HistoryItems) && _gridScrollViewer != null)
                _gridScrollViewer.Offset = default;
            if (e.PropertyName is nameof(HistoryViewModel.IsLoading) or nameof(HistoryViewModel.IsGridView) or nameof(HistoryViewModel.AutoLoadMoreItems))
                QueueLoadMoreCheck();
        }

        private void OnHistoryScrollChanged(object? sender, ScrollChangedEventArgs e) => QueueLoadMoreCheck();

        private void QueueLoadMoreCheck() => Dispatcher.UIThread.Post(() =>
        {
            if (_observedViewModel is not { IsGridView: true, AutoLoadMoreItems: true, IsLoading: false, CanLoadMoreItems: true } vm ||
                _gridScrollViewer is not { Viewport.Height: > 0 } scroll) return;
            double remaining = scroll.Extent.Height - scroll.Offset.Y - scroll.Viewport.Height;
            if (remaining <= Math.Max(120, vm.ThumbnailHeight)) vm.LoadMoreItemsCommand.Execute(null);
        }, DispatcherPriority.Background);

        public HistoryView() : this(UiViewModelFactoryAccessor.GetRequired().CreateHistoryViewModel())
        {
        }

        internal HistoryView(HistoryViewModel viewModel)
        {
            InitializeComponent();
            _gridHistoryListBox = this.FindControl<ListBox>("GridHistoryListBox");
            _listHistoryListBox = this.FindControl<ListBox>("ListHistoryListBox");
            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isSynchronizingSelection || sender is not ListBox sourceListBox || DataContext is not HistoryViewModel vm)
            {
                return;
            }

            var selectedItems = sourceListBox.SelectedItems?.OfType<HistoryItem>().ToList() ?? new List<HistoryItem>();
            vm.SetSelectedHistoryItems(selectedItems);

            _isSynchronizingSelection = true;

            try
            {
                SyncSelection(_gridHistoryListBox, sourceListBox, selectedItems);
                SyncSelection(_listHistoryListBox, sourceListBox, selectedItems);
            }
            finally
            {
                _isSynchronizingSelection = false;
            }
        }

        private async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not HistoryItem item || DataContext is not HistoryViewModel vm)
            {
                return;
            }

            await vm.EditImageCommand.ExecuteAsync(item);
            e.Handled = true;
        }

        private static void SyncSelection(ListBox? targetListBox, ListBox sourceListBox, IReadOnlyCollection<HistoryItem> selectedItems)
        {
            if (targetListBox == null || ReferenceEquals(targetListBox, sourceListBox) || targetListBox.SelectedItems == null)
            {
                return;
            }

            targetListBox.SelectedItems.Clear();

            foreach (var selectedItem in selectedItems)
            {
                targetListBox.SelectedItems.Add(selectedItem);
            }
        }
    }
}
