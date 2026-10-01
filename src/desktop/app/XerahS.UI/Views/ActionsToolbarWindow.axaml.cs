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

#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using XerahS.UI.Helpers;
using XerahS.UI.Services;
using XerahS.Core;
using XerahS.Core.Helpers;
using XerahS.Bootstrap;
using XerahS.Platform.Abstractions;
using XerahS.Common;

using System;
using System.Linq;
using DrawingPoint = System.Drawing.Point;

namespace XerahS.UI.Views;

public partial class ActionsToolbarWindow : Window
{
    private readonly IDesktopTaskManager? _taskManager;
    private bool _positionReady;
    private bool _adjustingPosition;
    private bool _closing;

    public ActionsToolbarWindow() : this(null) { }

    public ActionsToolbarWindow(IDesktopTaskManager? taskManager)
    {
        _taskManager = taskManager;
        InitializeComponent();
        Topmost = SettingsManager.Settings.ActionsToolbarStayTopMost;
        SettingsManager.Settings.ActionsToolbarList ??= [];

        ToolTip.SetTip(TitleHandle, "Drag to move; right-click for menu");
        ToolTip.SetPlacement(TitleHandle, PlacementMode.Top);
        ToolTip.SetVerticalOffset(TitleHandle, -4);
        ToolTip.SetShowDelay(TitleHandle, 400);
        ToolTip.SetBetweenShowDelay(TitleHandle, 100);
        TitleHandle.ContextFlyout = CreateToolbarMenu();
        UpdateTitleCursor();
        RefreshToolbar();

        Opened += OnOpened;
        PositionChanged += OnPositionChanged;
        Closed += (_, _) => _closing = true;
    }

    internal void RefreshToolbar()
    {
        while (ToolbarItems.Children.Count > 1)
        {
            ToolbarItems.Children.RemoveAt(1);
        }

        foreach (WorkflowType action in SettingsManager.Settings.ActionsToolbarList)
        {
            if (action == WorkflowType.None)
            {
                ToolbarItems.Children.Add(new Border
                {
                    Width = 1,
                    Height = 22,
                    Margin = new Thickness(3, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Classes = { "toolbar-separator" }
                });
                continue;
            }

            if (!WorkflowCatalog.IsAvailable(action)) continue;

            TextBlock icon = new()
            {
                Text = WorkflowIcons.GetIcon(action),
                FontSize = 17,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                IsHitTestVisible = false
            };
            icon.Classes.Add("icon");
            icon.Classes.Add("toolbar-icon");

            Button button = new()
            {
                Content = icon,
                Tag = action
            };
            button.Classes.Add("toolbar-action");
            ToolTip.SetTip(button, action.GetLocalizedDescription());
            ToolTip.SetPlacement(button, PlacementMode.Top);
            ToolTip.SetVerticalOffset(button, -4);
            ToolTip.SetShowDelay(button, 400);
            ToolTip.SetBetweenShowDelay(button, 100);
            button.Click += OnActionClick;
            ToolbarItems.Children.Add(button);
        }

        Dispatcher.UIThread.Post(ClampAndSavePosition, DispatcherPriority.Loaded);
    }

    private async void OnActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WorkflowType action })
        {
            return;
        }

        bool restoreTopmost = SettingsManager.Settings.ActionsToolbarStayTopMost;
        if (restoreTopmost)
        {
            Topmost = false;
        }

        try
        {
            await XerahS.Core.Helpers.TaskHelpers.ExecuteJob(action, UploadWorkflowService.CreateExecutionSettings(SettingsManager.DefaultTaskSettings, action));
        }
        catch (Exception ex) { UploadWorkflowService.ReportError(ex); }
        finally
        {
            if (!_closing)
            {
                Topmost = SettingsManager.Settings.ActionsToolbarStayTopMost;
            }
        }
    }

    private MenuFlyout CreateToolbarMenu()
    {
        MenuFlyout menu = new();

        MenuItem close = new() { Header = "Close" };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        menu.Items.Add(new Separator());

        MenuItem lockPosition = new()
        {
            Header = "Lock position",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = SettingsManager.Settings.ActionsToolbarLockPosition
        };
        lockPosition.Click += (_, _) =>
        {
            SettingsManager.Settings.ActionsToolbarLockPosition = lockPosition.IsChecked;
            UpdateTitleCursor();
            SaveSettings();
        };
        menu.Items.Add(lockPosition);

        MenuItem stayTopmost = new()
        {
            Header = "Stay on top",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = SettingsManager.Settings.ActionsToolbarStayTopMost
        };
        stayTopmost.Click += (_, _) =>
        {
            SettingsManager.Settings.ActionsToolbarStayTopMost = stayTopmost.IsChecked;
            Topmost = stayTopmost.IsChecked;
            SaveSettings();
        };
        menu.Items.Add(stayTopmost);

        MenuItem runAtStartup = new()
        {
            Header = "Open at XerahS startup",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = SettingsManager.Settings.ActionsToolbarRunAtStartup
        };
        runAtStartup.Click += (_, _) =>
        {
            SettingsManager.Settings.ActionsToolbarRunAtStartup = runAtStartup.IsChecked;
            SaveSettings();
        };
        menu.Items.Add(runAtStartup);
        menu.Items.Add(new Separator());

        MenuItem edit = new() { Header = "Edit..." };
        edit.Click += async (_, _) => await ShowEditorAsync();
        menu.Items.Add(edit);

        return menu;
    }

    private async System.Threading.Tasks.Task ShowEditorAsync()
    {
        bool restoreTopmost = SettingsManager.Settings.ActionsToolbarStayTopMost;
        Topmost = false;

        try
        {
            ActionsToolbarEditorWindow editor = new(RefreshToolbar);
            await editor.ShowDialog(this);
        }
        finally
        {
            if (!_closing)
            {
                Topmost = restoreTopmost;
                RefreshToolbar();
            }
        }
    }

    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerUpdateKind kind = e.GetCurrentPoint(TitleHandle).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.LeftButtonPressed && !SettingsManager.Settings.ActionsToolbarLockPosition)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
        else if (kind == PointerUpdateKind.MiddleButtonPressed)
        {
            Close();
            e.Handled = true;
        }
    }

    private void UpdateTitleCursor()
    {
        TitleHandle.Cursor = new Cursor(SettingsManager.Settings.ActionsToolbarLockPosition
            ? StandardCursorType.Arrow
            : StandardCursorType.SizeAll);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        RestorePosition();
        _positionReady = true;
        ClampAndSavePosition();
        Activate();
    }

    private void RestorePosition()
    {
        if (PlatformServices.IsWindowServiceInitialized && !PlatformServices.Window.SupportsWindowPositioning) return;
        DrawingPoint saved = SettingsManager.Settings.ActionsToolbarPosition;
        if (!saved.IsEmpty)
        {
            PixelPoint point = new(saved.X, saved.Y);
            if (Screens.All.Any(screen => screen.WorkingArea.Contains(point)))
            {
                Position = point;
                return;
            }
        }

        DrawingPoint cursor = PlatformServices.IsInitialized ? PlatformServices.Input.GetCursorPosition() : DrawingPoint.Empty;
        Screen? screen = Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? Screens.Primary;
        if (screen == null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        PixelSize size = PixelSize.FromSize(ClientSize, screen.Scaling);
        Position = new PixelPoint(area.Right - size.Width, area.Bottom - size.Height);
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_positionReady)
        {
            ClampAndSavePosition();
        }
    }

    private void ClampAndSavePosition()
    {
        if (!_positionReady || _adjustingPosition || (PlatformServices.IsWindowServiceInitialized && !PlatformServices.Window.SupportsWindowPositioning))
        {
            return;
        }

        Screen? screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen == null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        PixelSize size = PixelSize.FromSize(ClientSize, screen.Scaling);
        int maxX = Math.Max(area.X, area.Right - size.Width);
        int maxY = Math.Max(area.Y, area.Bottom - size.Height);
        PixelPoint adjusted = new(
            Math.Clamp(Position.X, area.X, maxX),
            Math.Clamp(Position.Y, area.Y, maxY));

        if (adjusted != Position)
        {
            _adjustingPosition = true;
            Position = adjusted;
            _adjustingPosition = false;
        }

        SettingsManager.Settings.ActionsToolbarPosition = new DrawingPoint(adjusted.X, adjusted.Y);
    }

    private void OnDragEnter(object? sender, DragEventArgs e) => UpdateDragState(e);

    private void OnDragOver(object? sender, DragEventArgs e) => UpdateDragState(e);

    private void UpdateDragState(DragEventArgs e)
    {
        bool supported = DragDropUploadWindow.HasSupportedData(e.DataTransfer);
        e.DragEffects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = supported;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        e.Handled = true;
        e.DragEffects = DragDropEffects.Copy;
        if (_taskManager == null) return;
        try
        {
            await UploadWorkflowService.UploadDroppedDataAsync(e.DataTransfer,
                UploadWorkflowService.CreateExecutionSettings(SettingsManager.DefaultTaskSettings, WorkflowType.DragDropUpload), _taskManager);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { UploadWorkflowService.ReportError(ex); }
    }

    private static void SaveSettings() => _ = SettingsManager.SaveApplicationConfigAsync();
}
