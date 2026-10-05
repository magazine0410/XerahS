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

using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareX.ImageEditor.Presentation.Rendering;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Services;

namespace XerahS.UI.ViewModels;

/// <summary>ShareX's Analyze image window: sends an image and a prompt to the configured AI provider.</summary>
public sealed partial class AnalyzeImageViewModel : ViewModelBase, IDisposable
{
    private readonly AIOptions _options;
    private readonly AnalyzeImageService _service;
    private readonly Func<AIProvider, string?> _getApiKey;
    private SKBitmap? _image;
    private string? _imagePath;
    private bool _hasApiKey;

    public IReadOnlyList<string> PresetPrompts { get; } =
    [
        "What is in this image?",
        "Thoroughly describe this image.",
        "Transcribe the image's text. Do not write anything else.",
        "Translate this text into English. Do not write anything else."
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(CanAnalyze))]
    private Bitmap? _previewImage;

    [ObservableProperty] private string _prompt;
    [ObservableProperty] private string? _selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultText = string.Empty;

    [ObservableProperty] private string _elapsedText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanAnalyze))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public Func<Task<string?>>? SelectImageRequested { get; set; }
    public Func<Task<SKBitmap?>>? SelectRegionRequested { get; set; }
    public Func<string, Task>? CopyTextRequested { get; set; }
    public Func<Task>? EditOptionsRequested { get; set; }
    /// <summary>As in ShareX, played after an analysis returns a result and after copying the result.</summary>
    public Action? PlayNotificationSound { get; set; }

    public bool HasImage => PreviewImage != null;
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);
    public bool IsIdle => !IsBusy;
    public bool CanAnalyze => HasImage && _hasApiKey && !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string ImageDescription => !string.IsNullOrWhiteSpace(_imagePath) ? Path.GetFileName(_imagePath) : "Captured region";
    public AIOptions Options => _options;

    /// <param name="image">Copied; the caller keeps ownership.</param>
    public AnalyzeImageViewModel(AIOptions options, AnalyzeImageService service, Func<AIProvider, string?> getApiKey,
        SKBitmap? image = null, string? imagePath = null)
    {
        _options = options;
        _service = service;
        _getApiKey = getApiKey;
        _prompt = options.Input;
        if (!string.IsNullOrWhiteSpace(imagePath)) LoadImage(imagePath);
        else if (image != null) SetImage(image.Copy(), null);
    }

    public async Task InitializeAsync()
    {
        await RefreshApiKeyAsync();
        if (!HasImage && _options.AutoStartRegion) await CaptureRegionAsync(analyzeAfterCapture: false);
        if (HasImage && _options.AutoStartAnalyze) await AnalyzeAsync();
    }

    partial void OnPromptChanged(string value) => _options.Input = value;

    partial void OnSelectedPresetChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Prompt = value;
    }

    [RelayCommand]
    private async Task SelectImageAsync()
    {
        if (IsBusy || SelectImageRequested == null) return;
        string? filePath = await SelectImageRequested();
        if (!string.IsNullOrWhiteSpace(filePath) && LoadImage(filePath)) await AnalyzeAsync();
    }

    [RelayCommand]
    private Task SelectRegionAsync() => CaptureRegionAsync(analyzeAfterCapture: true);

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (!CanAnalyze)
        {
            if (HasImage && !_hasApiKey) ErrorMessage = "Configure an API key in Options before analyzing.";
            return;
        }

        IsBusy = true;
        ResultText = "Thinking...";
        ElapsedText = string.Empty;
        ErrorMessage = string.Empty;
        var timer = Stopwatch.StartNew();
        try
        {
            AIOptions options = _options.Clone();
            SKBitmap image = _image!;
            string result = await Task.Run(async () => await _service.AnalyzeAsync(image, options, _getApiKey(options.Provider)));
            timer.Stop();
            ResultText = result.ReplaceLineEndings(Environment.NewLine);
            ElapsedText = $"Time: {timer.ElapsedMilliseconds:N0} ms";
            if (_options.AutoCopyResult && HasResult && CopyTextRequested != null) await CopyTextRequested(ResultText);
            if (HasResult) PlayNotificationSound?.Invoke();
        }
        catch (Exception ex)
        {
            ResultText = string.Empty;
            ErrorMessage = ex.Message;
            DebugHelper.WriteException(ex, "Image analysis failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyResultAsync()
    {
        if (HasResult && CopyTextRequested != null)
        {
            await CopyTextRequested(ResultText);
            PlayNotificationSound?.Invoke();
        }
    }

    [RelayCommand]
    private async Task EditOptionsAsync()
    {
        if (EditOptionsRequested == null) return;
        await EditOptionsRequested();
        await RefreshApiKeyAsync();
        if (HasError && _hasApiKey) ErrorMessage = string.Empty;
    }

    public bool LoadImage(string filePath)
    {
        try
        {
            var image = SKBitmap.Decode(filePath) ?? throw new InvalidDataException($"\"{Path.GetFileName(filePath)}\" is not a supported image.");
            SetImage(image, filePath);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>The image as PNG, for the image viewer.</summary>
    public byte[]? GetImageData()
    {
        if (_image == null) return null;
        using SKData data = _image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task RefreshApiKeyAsync()
    {
        // Reading the keyring starts secret-tool, so keep it off the UI thread and out of the CanAnalyze getter.
        AIProvider provider = _options.Provider;
        _hasApiKey = await Task.Run(() => !string.IsNullOrWhiteSpace(_getApiKey(provider)));
        OnPropertyChanged(nameof(CanAnalyze));
    }

    private async Task CaptureRegionAsync(bool analyzeAfterCapture)
    {
        if (IsBusy || SelectRegionRequested == null) return;
        SKBitmap? region = await SelectRegionRequested();
        if (region == null) return;
        SetImage(region, null);
        if (analyzeAfterCapture) await AnalyzeAsync();
    }

    private void SetImage(SKBitmap image, string? filePath)
    {
        Bitmap preview = BitmapConversionHelpers.ToAvaloniBitmap(image);
        PreviewImage?.Dispose();
        _image?.Dispose();
        _image = image;
        _imagePath = filePath;
        PreviewImage = preview;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(ImageDescription));
    }

    public void Dispose()
    {
        PreviewImage?.Dispose();
        _image?.Dispose();
    }
}
