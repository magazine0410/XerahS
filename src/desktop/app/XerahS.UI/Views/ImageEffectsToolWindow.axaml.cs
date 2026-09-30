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
using Avalonia.Platform.Storage;
using ShareX.ImageEditor.Core.Editor;
using SkiaSharp;
using XerahS.Core;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

/// <summary>
/// ShareX's Image effects tool window (<c>ImageEffectsWindowMode.Tool</c>): the task's preset editor with a
/// preview of the chosen image, which can be replaced from a file, the clipboard, or by dropping a file.
/// Save and Upload use the image with the preset applied.
/// </summary>
public partial class ImageEffectsToolWindow : SurfaceWindow
{
    private readonly ImageEffectsViewModel _viewModel;
    private readonly Func<SKBitmap, Task>? _uploadImage;
    private SKBitmap? _source;
    private string? _filePath;

    public ImageEffectsToolWindow()
        : this(new ImageEffectsViewModel(new TaskSettingsImage(), new EditorCore(), null!), null, null, null)
    {
    }

    /// <param name="source">The image to preview; the window keeps its own copy.</param>
    /// <param name="uploadImage">Uploads the result; the window disposes the bitmap afterwards.</param>
    public ImageEffectsToolWindow(ImageEffectsViewModel viewModel, SKBitmap? source, string? filePath,
        Func<SKBitmap, Task>? uploadImage)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _uploadImage = uploadImage;
        Editor.DataContext = viewModel;
        UploadButton.IsVisible = uploadImage != null;
        SetSource(source?.Copy(), filePath);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += (_, _) =>
        {
            _viewModel.ReleasePreview();
            _source?.Dispose();
            _source = null;
        };
    }

    internal ImageEffectsViewModel ViewModel => _viewModel;

    /// <summary>Takes ownership of <paramref name="image"/>.</summary>
    internal void SetSource(SKBitmap? image, string? filePath)
    {
        _source?.Dispose();
        _source = image;
        _filePath = filePath;
        _viewModel.SetPreviewSource(image);
        SaveButton.IsEnabled = image != null;
        UploadButton.IsEnabled = image != null;
        string name = filePath != null ? Path.GetFileName(filePath) : image != null ? "Clipboard image" : "No image";
        Title = image != null ? $"Image effects - {name}" : "Image effects";
        SourceText.Text = image != null ? $"{name} ({image.Width} x {image.Height})" : "Open an image to preview the preset on it.";
    }

    /// <summary>The source with the preset applied. The caller owns the result.</summary>
    internal SKBitmap? CreateResult() => _source != null ? _viewModel.ApplyEffects(_source) : null;

    internal bool LoadImageFile(string path)
    {
        var image = File.Exists(path) ? SKBitmap.Decode(path) : null;
        if (image == null) return false;
        SetSource(image, path);
        return true;
    }

    private async void OnOpenImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Image for Effects",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All]
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && !LoadImageFile(path))
            {
                throw new IOException("The selected image could not be opened. Check that it exists and uses a supported image format.");
            }
        }
        catch (Exception ex)
        {
            UploadWorkflowService.ReportError(ex, "Could not open the image");
        }
    }

    private async void OnClipboardClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var image = PlatformServices.IsInitialized ? await PlatformServices.Clipboard.GetImageAsync() : null;
            if (image == null) throw new InvalidOperationException("The clipboard does not contain an image.");
            SetSource(image, null);
        }
        catch (Exception ex)
        {
            UploadWorkflowService.ReportError(ex, "Could not paste the image");
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string baseName = _filePath != null ? Path.GetFileNameWithoutExtension(_filePath) : "image";
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Image",
                SuggestedFileName = $"{baseName}.png",
                DefaultExtension = "png",
                FileTypeChoices = [FilePickerFileTypes.ImagePng, FilePickerFileTypes.ImageJpg, FilePickerFileTypes.ImageWebp]
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            using var result = CreateResult();
            if (result != null) ImageHelpers.SaveBitmap(result, path);
        }
        catch (Exception ex)
        {
            UploadWorkflowService.ReportError(ex, "Could not save the image");
        }
    }

    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        if (_uploadImage == null) return;
        try
        {
            using var result = CreateResult();
            if (result != null) await _uploadImage(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            UploadWorkflowService.ReportError(ex);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private static string? GetDroppedImagePath(IDataTransfer data) =>
        UploadContentWindow.GetDroppedStorageItems(data).Select(item => item.TryGetLocalPath())
            .FirstOrDefault(path => path != null && File.Exists(path));

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = GetDroppedImagePath(e.DataTransfer) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (GetDroppedImagePath(e.DataTransfer) is { } path && !LoadImageFile(path))
        {
            UploadWorkflowService.ReportError(new IOException("The dropped file is not a supported image."), "Could not open the image");
        }
    }
}
