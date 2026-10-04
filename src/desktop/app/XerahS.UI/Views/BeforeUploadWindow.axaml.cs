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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XerahS.Core;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

public partial class BeforeUploadWindow : SurfaceWindow
{
    private readonly BeforeUploadViewModel _viewModel;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _accepted;
    public BeforeUploadWindow() : this(new TaskInfo()) { }
    public BeforeUploadWindow(TaskInfo info)
    {
        InitializeComponent();
        RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme();
        DataContext = _viewModel = new BeforeUploadViewModel(info);
        Opened += (_, _) => Activate();
        Closed += (_, _) => { _viewModel.Dispose(); _completion.TrySetResult(_accepted); };
    }
    internal async Task<bool> ShowAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Show();
        using var registration = token.Register(() => Dispatcher.UIThread.Post(Close));
        bool accepted = await _completion.Task;
        token.ThrowIfCancellationRequested();
        if (accepted) _viewModel.ApplySelection();
        return accepted;
    }
    private void OnUpload(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanUpload) return;
        _accepted = true;
        Close();
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    private void OnCopy(object? sender, RoutedEventArgs e) => _viewModel.CopyPreview();
    private void OnPreview(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _viewModel.GetPreviewData() is { } data)
            new ImageViewerWindow(data).Show();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
