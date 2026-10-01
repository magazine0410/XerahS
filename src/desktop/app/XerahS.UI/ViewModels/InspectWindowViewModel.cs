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

using XerahS.Core;
using XerahS.Common;
using XerahS.UI.Services;
using XerahS.Platform.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Drawing;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace XerahS.UI.ViewModels;

public sealed record InspectWindowProperty(string Name, string Value, bool IsMultiline = false);

public sealed partial class InspectWindowViewModel : ViewModelBase, IDisposable
{
    private WindowDetails? _selectedWindow;
    private readonly IWindowService _windows;
    public InspectWindowViewModel() : this(PlatformServices.Window) { }
    public InspectWindowViewModel(IWindowService windows) => _windows = windows;
    public bool CanPickControl => _windows.SupportsChildWindowPicking;
    public bool CanChangeTopmost => _windows.SupportsTopmost;
    public bool CanChangeOpacity => _windows.SupportsWindowOpacity;
    private bool _updating;
    private IntPtr _ignoredWindowHandle;

    public ObservableCollection<InspectWindowListItem> Windows { get; } = [];

    [ObservableProperty]
    private InspectWindowListItem? _selectedListItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _isTopLevelWindow;

    [ObservableProperty]
    private string _selectedTitle = "No target selected";

    [ObservableProperty]
    private string _selectedSubtitle = "Pick a window or control to inspect its properties";

    [ObservableProperty]
    private string _selectedType = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedIcon))]
    private AvaloniaBitmap? _selectedIcon;

    [ObservableProperty]
    private IReadOnlyList<InspectWindowProperty> _details = [];

    [ObservableProperty]
    private bool _isTopMost;

    [ObservableProperty]
    private double _opacity = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool CanRefresh => HasSelection;
    public bool HasSelectedIcon => SelectedIcon != null;
    public string ClipboardText => string.Join(Environment.NewLine + Environment.NewLine,
        Details.Select(x => $"{x.Name}{Environment.NewLine}{x.Value}"));

    public void SetIgnoredWindowHandle(IntPtr handle)
    {
        _ignoredWindowHandle = handle;
        ReloadWindowList();
    }

    public void SelectWindow(IntPtr handle, bool isTopLevelWindow)
    {
        if (handle == IntPtr.Zero || handle == _ignoredWindowHandle)
        {
            return;
        }

        _selectedWindow = _windows.GetWindowDetails(handle);
        IsTopLevelWindow = isTopLevelWindow;
        SelectedListItem = null;
        UpdateWindowInfo();
    }

    partial void OnSelectedListItemChanged(InspectWindowListItem? value)
    {
        if (!_updating && value != null)
        {
            SelectWindow(value.Handle, true);
        }
    }

    public void ReloadWindowList()
    {
        _updating = true;
        try
        {
            DisposeWindowList();
            Windows.Clear();
            foreach (InspectWindowListItem window in InspectWindowService.GetVisibleWindows(_ignoredWindowHandle, _windows))
            {
                Windows.Add(window);
            }
            SelectedListItem = null;
        }
        finally
        {
            _updating = false;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        if (_selectedWindow != null)
        {
            _selectedWindow = _windows.GetWindowDetails(_selectedWindow.Handle);
            UpdateWindowInfo();
        }
    }

    partial void OnIsTopMostChanged(bool value)
    {
        if (_updating || !IsTopLevelWindow || _selectedWindow == null)
        {
            return;
        }

        try
        {
            ErrorMessage = string.Empty;
            if (!_windows.SetWindowTopmost(_selectedWindow.Handle, value))
                throw new InvalidOperationException("Unable to change the window topmost state.");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Refresh();
            DebugHelper.WriteException(ex, "Failed to change window topmost state.");
        }
    }

    partial void OnOpacityChanged(double value)
    {
        if (_updating || !IsTopLevelWindow || _selectedWindow == null)
        {
            return;
        }

        try
        {
            ErrorMessage = string.Empty;
            double percentage = Math.Clamp(value, 10, 100);
            if (!_windows.SetWindowOpacity(_selectedWindow.Handle, (byte)Math.Round(percentage / 100d * 255d)))
                throw new InvalidOperationException("Unable to change the window opacity.");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Refresh();
            DebugHelper.WriteException(ex, "Failed to change window opacity.");
        }
    }

    private void UpdateWindowInfo()
    {
        if (_selectedWindow == null)
        {
            ClearSelection();
            return;
        }

        _updating = true;
        try
        {
            IntPtr handle = _selectedWindow.Handle;
            string title = TryGet(() => _selectedWindow.Title);
            string className = TryGet(() => _selectedWindow.ClassName);
            string processName = TryGet(() => _selectedWindow.ProcessName);
            string processFileName = TryGet(() => _selectedWindow.ProcessFileName);
            string processId = TryGet(() => _selectedWindow.ProcessId.ToString());
            Rectangle windowRectangle = TryGet(() => _selectedWindow.Bounds, Rectangle.Empty);
            Rectangle clientRectangle = TryGet(() => _selectedWindow.ClientBounds, Rectangle.Empty);
            string styles = TryGet(() => _selectedWindow.Styles ?? string.Empty);
            string extendedStyles = TryGet(() => _selectedWindow.ExtendedStyles ?? string.Empty);

            SelectedTitle = string.IsNullOrWhiteSpace(title) ? "Untitled window" : title;
            SelectedSubtitle = string.IsNullOrWhiteSpace(processName)
                ? className
                : string.IsNullOrWhiteSpace(className) ? processName : $"{processName}  |  {className}";
            SelectedType = IsTopLevelWindow ? "Window" : "Control";
            ReplaceSelectedIcon(InspectWindowService.GetWindowIcon(handle, _windows));
            Details =
            [
                new("Window handle", $"0x{handle.ToInt64():X8}"),
                new("Window title", title),
                new("Class name", className),
                new("Process name", processName),
                new("Process file name", processFileName),
                new("Process identifier", processId),
                new("Window rectangle", FormatRectangle(windowRectangle)),
                new("Client rectangle", FormatRectangle(clientRectangle)),
                new("Window styles", styles, true),
                new("Extended window styles", extendedStyles, true)
            ];

            Details = Details.Where(d => d.Name != "Window styles" || _selectedWindow.Styles != null)
                .Where(d => d.Name != "Extended window styles" || _selectedWindow.ExtendedStyles != null).ToArray();

            if (IsTopLevelWindow)
            {
                IsTopMost = TryGet(() => _selectedWindow.IsTopmost, false);
                byte opacity = TryGet(() => _selectedWindow.Opacity, (byte)255);
                Opacity = Math.Round(opacity / 255d * 100d);
            }

            HasSelection = true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to update inspected window information.");
            ClearSelection();
        }
        finally
        {
            _updating = false;
        }
    }

    private void ClearSelection()
    {
        _selectedWindow = null;
        HasSelection = false;
        IsTopLevelWindow = false;
        SelectedTitle = "No target selected";
        SelectedSubtitle = "Pick a window or control to inspect its properties";
        SelectedType = string.Empty;
        ReplaceSelectedIcon(null);
        Details = [];
        IsTopMost = false;
        Opacity = 100;
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return rectangle.IsEmpty
            ? string.Empty
            : string.Format("X: {0}  Y: {1}  |  Width: {2}  Height: {3}", rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private static string TryGet(Func<string> valueFactory)
    {
        try
        {
            return valueFactory() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static T TryGet<T>(Func<T> valueFactory, T fallback)
    {
        try
        {
            return valueFactory();
        }
        catch
        {
            return fallback;
        }
    }

    private void DisposeWindowList()
    {
        foreach (InspectWindowListItem window in Windows)
        {
            window.Dispose();
        }
    }

    private void ReplaceSelectedIcon(AvaloniaBitmap? icon)
    {
        SelectedIcon?.Dispose();
        SelectedIcon = icon;
    }

    public void Dispose()
    {
        ReplaceSelectedIcon(null);
        DisposeWindowList();
        Windows.Clear();
    }
}
