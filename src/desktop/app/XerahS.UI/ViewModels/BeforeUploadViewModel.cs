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
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace XerahS.UI.ViewModels;

public sealed record BeforeUploadDestination(string InstanceId, string Name);

public partial class BeforeUploadViewModel : ObservableObject, IDisposable
{
    private readonly TaskInfo _info;
    private readonly SKBitmap? _image;
    public IReadOnlyList<BeforeUploadDestination> Destinations { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Prompt), nameof(CanUpload))]
    private BeforeUploadDestination? _selectedDestination;
    public bool CanUpload => SelectedDestination != null;
    public string Prompt => SelectedDestination == null ? "Choose an upload destination."
        : string.IsNullOrEmpty(_info.FileName) ? $"Upload to {SelectedDestination.Name}?"
        : $"Upload {_info.FileName} to {SelectedDestination.Name}?";
    public Bitmap? Preview { get; }
    public string ImageSize => _image == null ? _info.FileName : $"{_image.Width} × {_image.Height}";
    public bool HasPreview => Preview != null;

    public BeforeUploadViewModel(TaskInfo info)
    {
        _info = info;
        var category = info.Job == TaskJob.ShareURL ? UploaderCategory.UrlSharing : info.DataType switch
        {
            EDataType.Image => UploaderCategory.Image,
            EDataType.Text => UploaderCategory.Text,
            EDataType.URL => UploaderCategory.UrlShortener,
            _ => UploaderCategory.File
        };
        var manager = InstanceManager.Instance;
        string? defaultName = manager.GetDefaultInstance(category)?.DisplayName;
        var options = new List<BeforeUploadDestination> { new("", string.IsNullOrEmpty(defaultName) ? "Default destination" : $"Default: {defaultName}") };
        var instances = manager.GetInstancesByCategory(category);
        if (category is UploaderCategory.Image or UploaderCategory.Text && info.TaskSettings.AllowCrossCategoryFallback)
            instances = instances.Concat(manager.GetInstancesByCategory(UploaderCategory.File)).DistinctBy(instance => instance.InstanceId).ToList();
        options.AddRange(instances.Where(instance => instance.IsAvailable).Select(instance => new BeforeUploadDestination(instance.InstanceId, instance.DisplayName)));
        Destinations = options;
        string? selectedId = info.Job == TaskJob.ShareURL
            ? info.TaskSettings.UrlSharingDestinationInstanceId
            : info.TaskSettings.GetDestinationInstanceIdForDataType(info.DataType);
        SelectedDestination = options.FirstOrDefault(option => option.InstanceId == selectedId) ?? options[0];

        // File actions may have replaced the captured image. Preview what will actually be uploaded.
        if (!string.IsNullOrEmpty(info.FilePath) && FileHelpers.IsImageFile(info.FilePath)) _image = ImageHelpers.LoadBitmap(info.FilePath);
        else if (string.IsNullOrEmpty(info.FilePath)) _image = info.Metadata.Image?.Copy();
        if (_image != null)
        {
            using var encoded = _image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = encoded.AsStream();
            Preview = new Bitmap(stream);
        }
    }

    public void ApplySelection()
    {
        if (SelectedDestination == null) return;
        if (_info.Job == TaskJob.ShareURL) _info.TaskSettings.UrlSharingDestinationInstanceId = SelectedDestination.InstanceId;
        else if (_info.DataType == EDataType.URL) _info.TaskSettings.UrlShortenerDestinationInstanceId = SelectedDestination.InstanceId;
        else _info.TaskSettings.DestinationInstanceId = SelectedDestination.InstanceId;
    }

    public void CopyPreview()
    {
        if (_image != null) Platform.Abstractions.PlatformServices.Clipboard.SetImage(_image);
    }

    public byte[]? GetPreviewData()
    {
        if (_image == null) return null;
        using var encoded = _image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    public void Dispose() { Preview?.Dispose(); _image?.Dispose(); }
}
