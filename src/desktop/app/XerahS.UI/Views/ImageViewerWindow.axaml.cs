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
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using XerahS.Platform.Abstractions;
using XerahS.UI.ViewModels;
using XerahS.UI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XerahS.UI.Views;

public partial class ImageViewerWindow : Window
{
    private readonly ImageViewerViewModel _viewModel = new();
    private bool _closeOnDeactivate;

    public ImageViewerWindow()
    {
        Initialize();
    }

    public ImageViewerWindow(string filePath)
    {
        Initialize();
        _closeOnDeactivate = _viewModel.LoadFile(filePath);
    }

    public ImageViewerWindow(IReadOnlyList<string> filePaths, int selectedIndex)
    {
        Initialize();
        _closeOnDeactivate = _viewModel.LoadFiles(filePaths, selectedIndex);
    }

    public ImageViewerWindow(byte[] imageData, string? displayName = null)
    {
        Initialize();
        _closeOnDeactivate = _viewModel.LoadEncodedImage(imageData, displayName);
    }

    private void Initialize()
    {
        DataContext = _viewModel;
        AvaloniaXamlLoader.Load(this);
        WindowStartupLocation = WindowStartupLocation.Manual;
        var cursor = PlatformServices.IsInitialized ? PlatformServices.Input.GetCursorPosition() : System.Drawing.Point.Empty;
        var screen = Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? Screens.Primary;
        if (screen != null) Position = screen.Bounds.Position;
        Title = "Image viewer";
        Focusable = true;
        Opened += (_, _) => { Activate(); Focus(); };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Deactivated += OnDeactivated;
        Closed += (_, _) => _viewModel.Dispose();
    }

    /// <summary>
    /// Asks for an image, then shows it. ShareX opens the full-screen viewer first and asks from it;
    /// on Linux the portal's file dialog is a separate window that KWin keeps below the topmost
    /// full-screen viewer, so the file is chosen before the viewer opens.
    /// </summary>
    public static async Task OpenAsync(Window? owner)
    {
        try
        {
            var storage = StorageProviderResolver.Resolve(owner);
            if (storage == null) return;
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open image",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });

            if (files.FirstOrDefault()?.TryGetLocalPath() is not { Length: > 0 } filePath) return;
            var window = new ImageViewerWindow(filePath);
            if (window._viewModel.HasImage) window.Show();
            else window.Close();
        }
        catch (Exception ex)
        {
            UploadWorkflowService.ReportError(ex, "Could not open image");
        }
    }

    private void OnPreviousClick(object? sender, RoutedEventArgs e) => _viewModel.Navigate(-1);
    private void OnNextClick(object? sender, RoutedEventArgs e) => _viewModel.Navigate(1);

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton is MouseButton.Left or MouseButton.Right)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0 && _viewModel.CanNavigateLeft)
        {
            _viewModel.Navigate(-1);
            e.Handled = true;
        }
        else if (e.Delta.Y < 0 && _viewModel.CanNavigateRight)
        {
            _viewModel.Navigate(1);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                _viewModel.Navigate(-1);
                e.Handled = true;
                break;
            case Key.Right:
                _viewModel.Navigate(1);
                e.Handled = true;
                break;
            case Key.Escape:
            case Key.Enter:
            case Key.Space:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_closeOnDeactivate)
        {
            Close();
        }
    }
}
