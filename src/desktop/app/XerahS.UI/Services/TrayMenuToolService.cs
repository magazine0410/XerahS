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
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using XerahS.Platform.Abstractions;

namespace XerahS.UI.Services;

/// <summary>ShareX's tray-menu hotkey opens a transient menu at the pointer.</summary>
internal static class TrayMenuToolService
{
    private static Window? _anchor;
    private static MenuFlyout? _menu;

    internal static bool IsOpen => _anchor != null;

    public static void Toggle()
    {
        if (IsOpen) { Close(); return; }
        TrayIconHelper.Instance.BuildTrayMenu();
        var cursor = PlatformServices.IsInitialized ? PlatformServices.Input.GetCursorPosition() : System.Drawing.Point.Empty;
        Open(TrayIconHelper.Instance.TrayMenu, new PixelPoint(cursor.X, cursor.Y));
    }

    internal static Window Open(NativeMenu source, PixelPoint position)
    {
        Close();
        var anchor = new Window
        {
            Width = 1, Height = 1, MinWidth = 1, MinHeight = 1,
            Position = position, WindowStartupLocation = WindowStartupLocation.Manual,
            WindowDecorations = WindowDecorations.None, ShowInTaskbar = false,
            CanResize = false, Topmost = true, Opacity = 0, Background = Brushes.Transparent
        };
        var menu = CreateMenu(source);
        _anchor = anchor;
        _menu = menu;
        anchor.Deactivated += OnDismissed;
        anchor.Closed += OnDismissed;
        menu.Closed += OnDismissed;
        try
        {
            anchor.Show();
            anchor.Position = position;
            anchor.Activate();
            menu.ShowAt(anchor);
            return anchor;
        }
        catch
        {
            Close();
            throw;
        }
    }

    internal static MenuFlyout CreateMenu(NativeMenu source)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var item in source.Items) menu.Items.Add(CreateItem(item));
        return menu;
    }

    private static Control CreateItem(NativeMenuItemBase source)
    {
        if (source is NativeMenuItemSeparator) return new Separator();
        var item = (NativeMenuItem)source;
        var result = new MenuItem
        {
            Header = item.Header, Command = item.Command, CommandParameter = item.CommandParameter,
            IsEnabled = item.IsEnabled, IsVisible = item.IsVisible,
            IsChecked = item.IsChecked, ToggleType = (MenuItemToggleType)item.ToggleType
        };
        if (item.Menu != null)
            foreach (var child in item.Menu.Items) result.Items.Add(CreateItem(child));
        return result;
    }

    private static void OnDismissed(object? sender, EventArgs e) => Close();

    internal static void Close()
    {
        var anchor = _anchor;
        var menu = _menu;
        _anchor = null;
        _menu = null;
        if (menu != null)
        {
            menu.Closed -= OnDismissed;
            menu.Hide();
        }
        if (anchor != null)
        {
            anchor.Deactivated -= OnDismissed;
            anchor.Closed -= OnDismissed;
            anchor.Close();
        }
    }
}
