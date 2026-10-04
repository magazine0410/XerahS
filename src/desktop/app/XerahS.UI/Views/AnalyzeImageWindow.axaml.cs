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
using Avalonia.Platform.Storage;
using SkiaSharp;
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

public partial class AnalyzeImageWindow : SurfaceWindow
{
    private readonly AnalyzeImageViewModel _viewModel;
    private readonly Func<Task<SKBitmap?>> _captureRegion;
    private readonly AnalyzeImageService _service;

    public AnalyzeImageWindow() : this(new AnalyzeImageViewModel(new AIOptions(), new AnalyzeImageService(), _ => null),
        () => Task.FromResult<SKBitmap?>(null), new AnalyzeImageService())
    {
    }

    public AnalyzeImageWindow(AnalyzeImageViewModel viewModel, Func<Task<SKBitmap?>> captureRegion, AnalyzeImageService service)
    {
        InitializeComponent();
        RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme();
        _viewModel = viewModel;
        _captureRegion = captureRegion;
        _service = service;
        DataContext = viewModel;

        viewModel.SelectImageRequested = SelectImageAsync;
        viewModel.SelectRegionRequested = SelectRegionAsync;
        viewModel.CopyTextRequested = text => XerahS.Platform.Abstractions.PlatformServices.Clipboard.SetTextAsync(text);
        viewModel.EditOptionsRequested = EditOptionsAsync;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += OnOpened;
        Closed += (_, _) => viewModel.Dispose();
    }

    /// <summary>Called after the options window saves.</summary>
    public Action? OptionsSaved { get; set; }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Activate();
        await _viewModel.InitializeAsync();
    }

    private async Task<string?> SelectImageAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<SKBitmap?> SelectRegionAsync()
    {
        WindowState previousState = WindowState;
        try
        {
            WindowState = WindowState.Minimized;
            await Task.Delay(250);
            return await _captureRegion();
        }
        finally
        {
            WindowState = previousState;
            Activate();
        }
    }

    private async Task EditOptionsAsync()
    {
        // Reading the keyring starts secret-tool, so load the three keys off the UI thread first.
        var keys = await Task.Run(() => Enum.GetValues<AIProvider>().ToDictionary(provider => provider, AIApiKeys.Get));
        var viewModel = new AnalyzeImageOptionsViewModel(_viewModel.Options, _service, provider => keys[provider], AIApiKeys.Set);
        var window = new AnalyzeImageOptionsWindow(viewModel);
        if (await window.ShowDialog<bool>(this)) OptionsSaved?.Invoke();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _viewModel.IsBusy || !e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.None : DragDropEffects.Copy;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        string? path = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault()?.TryGetLocalPath();
        if (!_viewModel.IsBusy && path != null)
        {
            _viewModel.LoadImage(path);
            e.Handled = true;
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _viewModel.GetImageData() is not { Length: > 0 } data) return;
        new ImageViewerWindow(data, _viewModel.ImageDescription).Show(this);
        e.Handled = true;
    }
}
