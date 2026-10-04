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

using Avalonia.Interactivity;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

public partial class PrintWindow : SurfaceWindow
{
    private readonly PrintOptionsViewModel? _viewModel;

    public PrintWindow()
    {
        InitializeComponent();
    }

    public PrintWindow(PrintOptionsViewModel viewModel) : this()
    {
        RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme();
        DataContext = _viewModel = viewModel;
        Opened += (_, _) => Activate();
    }

    public Func<Task>? PreviewRequested { get; set; }

    /// <summary>Prints. Returns true when the window can close.</summary>
    public Func<Task<bool>>? PrintRequested { get; set; }

    private async void OnPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (PreviewRequested != null) await RunAsync(PreviewRequested);
    }

    private async void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        if (PrintRequested == null) return;
        bool close = false;
        await RunAsync(async () => close = await PrintRequested());
        if (close) Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private async Task RunAsync(Func<Task> action)
    {
        if (_viewModel != null) _viewModel.IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            if (_viewModel != null) _viewModel.IsBusy = false;
            Activate();
        }
    }
}
