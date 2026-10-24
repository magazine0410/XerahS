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
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using XerahS.Common;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views
{
    public sealed record TaskListHotkeyTip(string Hotkey, string Description);

    /// <summary>The Tasks page's bindings: the task list, and ShareX's hotkey list shown while it is empty.</summary>
    public sealed class TasksPageViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public ObservableCollection<TaskThumbnailViewModel> Items => TaskListService.Instance.Items;

        public ObservableCollection<TaskListHotkeyTip> HotkeyTips { get; } = new();

        public string TipTitle => $"{AppResources.AppName} hotkeys";

        public bool IsTipVisible => Items.Count == 0 && SettingsManager.Settings.ShowMainWindowTip;

        public void Refresh()
        {
            HotkeyTips.Clear();
            foreach (var workflow in SettingsManager.WorkflowsConfig?.Hotkeys ?? [])
            {
                if (workflow.HotkeyInfo?.IsValid == true && workflow.Job != WorkflowType.None)
                {
                    HotkeyTips.Add(new TaskListHotkeyTip(workflow.HotkeyInfo.ToString(), workflow.TaskSettings?.ToString() ?? EnumExtensions.GetDescription(workflow.Job)));
                }
            }

            OnPropertyChanged(nameof(IsTipVisible));
        }
    }

    /// <summary>
    /// ShareX's main window task list (MainWindow's thumbnail view): the tasks of this session as thumbnails,
    /// with ShareX's thumbnail size, title, and click action settings, and its hotkey list while empty.
    /// </summary>
    public partial class TasksView : PageView
    {
        private readonly TasksPageViewModel _viewModel = new();
        private bool _suppressClickAction;

        private ObservableCollection<TaskThumbnailViewModel> Items => _viewModel.Items;

        public TasksView()
        {
            InitializeComponent();
            DataContext = _viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Items.CollectionChanged += OnItemsChanged;
            _viewModel.Refresh();
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            Items.CollectionChanged -= OnItemsChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => _viewModel.Refresh();

        private void OnThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control { DataContext: TaskThumbnailViewModel item } control) return;

            PointerPointProperties properties = e.GetCurrentPoint(control).Properties;
            bool isControlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool isShiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _suppressClickAction = isControlPressed || isShiftPressed;

            if (properties.IsLeftButtonPressed)
            {
                if (isControlPressed) item.IsSelected = !item.IsSelected;
                else if (isShiftPressed) SelectRange(item);
                else SelectOnly(item);
            }
            else if (properties.IsRightButtonPressed && !item.IsSelected)
            {
                SelectOnly(item);
            }
        }

        private void OnThumbnailPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is not Control { DataContext: TaskThumbnailViewModel item } control) return;

            switch (e.InitialPressMouseButton)
            {
                case MouseButton.Left:
                    if (!_suppressClickAction && SettingsManager.Settings.ThumbnailClickAction != ThumbnailViewClickAction.Select)
                    {
                        ExecuteClickAction(item, SettingsManager.Settings.ThumbnailClickAction);
                    }
                    e.Handled = true;
                    break;
                case MouseButton.Right:
                    BuildContextMenu(item).Open(control);
                    e.Handled = true;
                    break;
            }
        }

        private void OnThumbnailDoubleTapped(object? sender, TappedEventArgs e)
        {
            // With the "Select" click action, ShareX opens the file on a double click.
            if (sender is Control { DataContext: TaskThumbnailViewModel item } && !_suppressClickAction &&
                SettingsManager.Settings.ThumbnailClickAction == ThumbnailViewClickAction.Select)
            {
                ExecuteClickAction(item, ThumbnailViewClickAction.OpenFile);
                e.Handled = true;
            }
        }

        private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) e.Handled = true;
        }

        /// <summary>As in ShareX, clicking a title opens the URL, or the file when there is none.</summary>
        private void OnTitlePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is not Control { DataContext: TaskThumbnailViewModel item } || e.InitialPressMouseButton != MouseButton.Left) return;

            if (!string.IsNullOrEmpty(item.Url)) OpenUrl(item.Url);
            else if (!string.IsNullOrEmpty(item.FilePath)) OpenFile(item.FilePath);
            e.Handled = true;
        }

        private void SelectOnly(TaskThumbnailViewModel item)
        {
            foreach (TaskThumbnailViewModel other in Items) other.IsSelected = ReferenceEquals(other, item);
        }

        private void SelectRange(TaskThumbnailViewModel item)
        {
            int anchor = Items.ToList().FindIndex(i => i.IsSelected);
            int index = Items.IndexOf(item);
            if (anchor < 0) { SelectOnly(item); return; }
            for (int i = 0; i < Items.Count; i++) Items[i].IsSelected = i >= Math.Min(anchor, index) && i <= Math.Max(anchor, index);
        }

        /// <summary>ShareX's ExecuteThumbnailClick.</summary>
        private static void ExecuteClickAction(TaskThumbnailViewModel item, ThumbnailViewClickAction action)
        {
            string filePath = item.FilePath;
            switch (action)
            {
                case ThumbnailViewClickAction.Default:
                    if (File.Exists(filePath))
                    {
                        if (FileHelpers.IsImageFile(filePath)) ImageViewerWindow.OpenFile(filePath);
                        else OpenFile(filePath);
                    }
                    break;
                case ThumbnailViewClickAction.OpenImageViewer:
                    if (item.IsImageFile) ImageViewerWindow.OpenFile(filePath);
                    break;
                case ThumbnailViewClickAction.OpenFile:
                    if (!string.IsNullOrEmpty(filePath)) OpenFile(filePath);
                    break;
                case ThumbnailViewClickAction.OpenFolder:
                    if (!string.IsNullOrEmpty(filePath)) FileHelpers.OpenFolderWithFile(filePath);
                    break;
                case ThumbnailViewClickAction.OpenURL:
                    if (!string.IsNullOrEmpty(item.Url)) OpenUrl(item.Url);
                    break;
                case ThumbnailViewClickAction.EditImage:
                    if (item.IsImageFile) _ = EditImageAsync(filePath);
                    break;
            }
        }

        private ContextMenu BuildContextMenu(TaskThumbnailViewModel item)
        {
            List<TaskThumbnailViewModel> selected = Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) selected.Add(item);

            bool hasUrl = !string.IsNullOrEmpty(item.Url);
            bool hasFile = !string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath);

            var open = new MenuItem { Header = "Open" };
            open.Items.Add(MenuEntry("URL", () => OpenUrl(item.Url), hasUrl));
            open.Items.Add(MenuEntry("File", () => OpenFile(item.FilePath), hasFile));
            open.Items.Add(MenuEntry("Folder", () => FileHelpers.OpenFolderWithFile(item.FilePath), hasFile));

            var copy = new MenuItem { Header = "Copy" };
            copy.Items.Add(MenuEntry("URL", () => PlatformServices.Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(i => i.Url).Where(u => !string.IsNullOrEmpty(u)))), hasUrl));
            copy.Items.Add(MenuEntry("File", () => PlatformServices.Clipboard.SetFileDropList(selected.Select(i => i.FilePath).Where(File.Exists).ToArray()), hasFile));
            copy.Items.Add(MenuEntry("Image", () => CopyImage(item.FilePath), item.IsImageFile));
            copy.Items.Add(MenuEntry("File path", () => PlatformServices.Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(i => i.FilePath).Where(p => !string.IsNullOrEmpty(p)))), !string.IsNullOrEmpty(item.FilePath)));

            var menu = new ContextMenu();
            menu.Items.Add(open);
            menu.Items.Add(copy);
            menu.Items.Add(MenuEntry("Edit image...", () => _ = EditImageAsync(item.FilePath), item.IsImageFile));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("Stop upload", () => selected.ForEach(i => i.Task?.Stop()),
                selected.Any(i => i.IsWorking && i.Task?.IsRecordingInProgress != true)));
            menu.Items.Add(MenuEntry("Remove task from list", () => TaskListService.Instance.Remove(selected)));
            menu.Items.Add(MenuEntry("Clear task list", TaskListService.Instance.Clear, Items.Any(i => !i.IsWorking)));
            return menu;
        }

        private static MenuItem MenuEntry(string header, Action execute, bool isEnabled = true) =>
            new() { Header = header, Command = new RelayCommand(execute), IsEnabled = isEnabled };

        private static void OpenUrl(string url) => PlatformServices.System.OpenUrl(url);

        private static void OpenFile(string filePath) => PlatformServices.System.OpenFile(filePath);

        private static void CopyImage(string filePath)
        {
            using var image = ImageHelpers.LoadBitmap(filePath);
            if (image != null) PlatformServices.Clipboard.SetImage(image);
        }

        /// <summary>ShareX's AnnotateImageFromFile: the image opens in the image editor with the default task settings.</summary>
        private static async Task EditImageAsync(string filePath)
        {
            if (TaskListService.Instance.TaskManager is not { } taskManager) return;
            try
            {
                await ImageEditingToolService.OpenImageFileAsync(filePath, TaskSettings.GetSafeTaskSettings(SettingsManager.DefaultTaskSettings), false, taskManager);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Unable to edit the image");
            }
        }
    }
}
