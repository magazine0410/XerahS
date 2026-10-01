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
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using BitmapConversionHelpers = ShareX.ImageEditor.Presentation.Rendering.BitmapConversionHelpers;
using XerahS.Platform.Abstractions;
using XerahS.RegionCapture;

namespace XerahS.UI.ViewModels;

/// <summary>The selected area and the window under it.</summary>
public readonly record struct ScrollingCaptureTarget(IntPtr WindowHandle, System.Drawing.Rectangle Region);

/// <summary>
/// ShareX's scrolling capture window: it starts with an area selection, captures while scrolling the
/// window under the area, and then shows the stitched result.
/// </summary>
public partial class ScrollingCaptureViewModel : ViewModelBase
{
    public const string ReadyText = "Ready";
    public const string CapturingText = "Capturing...";
    public const string StoppingText = "Stopping capture...";
    public const string SelectionCancelledText = "Window selection cancelled";
    public const string SuccessfulText = "Capture successful";
    public const string PartiallySuccessfulText = "Capture partially successful";
    public const string FailedText = "Capture failed";
    public const string UnsupportedText = "Scrolling capture is not supported on this platform.";
    public const string InputUnavailableText = "Capture failed: the window system did not allow XerahS to scroll the window.";

    private readonly ScrollingCaptureOptions _options;
    private CancellationTokenSource? _captureCts;
    private SKBitmap? _capturedSkBitmap;
    private bool _busy;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _previewImage;

    [ObservableProperty]
    private string _statusText = ReadyText;

    /// <summary>Null while no capture has finished.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private ScrollingCaptureStatus? _status;

    /// <summary>The status dot's color, as ShareX colors its status icon.</summary>
    public Avalonia.Media.IBrush StatusBrush => IsCapturing ? Avalonia.Media.Brushes.DodgerBlue : Status switch
    {
        ScrollingCaptureStatus.Successful => Avalonia.Media.Brushes.MediumSeaGreen,
        ScrollingCaptureStatus.PartiallySuccessful => Avalonia.Media.Brushes.Goldenrod,
        ScrollingCaptureStatus.Failed => Avalonia.Media.Brushes.IndianRed,
        _ => Avalonia.Media.Brushes.Gray
    };

    [ObservableProperty]
    private string _resultSize = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseControls))]
    [NotifyPropertyChangedFor(nameof(CanUseResult))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseResult))]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isOptionsOpen;

    // The values in the options panel; OK writes them to the options.
    [ObservableProperty] private decimal _startDelay;
    [ObservableProperty] private decimal _scrollDelay;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScrollAmountVisible))]
    private ScrollMethod _scrollMethod;
    [ObservableProperty] private decimal _scrollAmount;
    [ObservableProperty] private bool _autoScrollTop;
    [ObservableProperty] private bool _autoIgnoreBottomEdge;
    [ObservableProperty] private bool _showRegion;
    [ObservableProperty] private bool _autoUpload;

    public ScrollingCaptureViewModel()
        : this(new ScrollingCaptureOptions(), Enum.GetValues<ScrollMethod>())
    {
    }

    public ScrollingCaptureViewModel(ScrollingCaptureOptions options, IReadOnlyList<ScrollMethod> scrollMethods)
    {
        _options = options;
        ScrollMethods = scrollMethods;
        LoadOptions();
    }

    public IReadOnlyList<ScrollMethod> ScrollMethods { get; }

    /// <summary>As in ShareX, Page down always scrolls one page, so it has no amount.</summary>
    public bool IsScrollAmountVisible => ScrollMethod != ScrollMethod.PageDown;

    public bool CanUseControls => !IsCapturing;
    public bool CanUseResult => HasResult && !IsCapturing;

    /// <summary>Lets the user select an area; returns it with the window under it, or null.</summary>
    public Func<Task<ScrollingCaptureTarget?>>? SelectTargetRequested { get; set; }

    /// <summary>Shows the border around the area while capturing; disposing the result hides it.</summary>
    public Func<System.Drawing.Rectangle, IDisposable?>? ShowRegionRequested { get; set; }

    /// <summary>Minimizes (true) or restores and activates (false) the window.</summary>
    public Action<bool>? SetMinimizedRequested { get; set; }

    /// <summary>Runs the after capture tasks for the result (ShareX's "Upload / Save").</summary>
    public Func<SKBitmap, Task>? UploadRequested { get; set; }

    /// <summary>Saves the options after OK in the options panel.</summary>
    public Action? SaveOptionsRequested { get; set; }

    /// <summary>Raised after a capture ends.</summary>
    public event EventHandler? CaptureFinished;

    /// <summary>
    /// ShareX's StartStop: stops a running capture, otherwise selects an area and captures it.
    /// The window calls it when it opens, and the Scrolling capture hotkey calls it again.
    /// </summary>
    public async Task StartStopAsync()
    {
        if (IsCapturing)
        {
            StopCapture();
            return;
        }

        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            await SelectAndCaptureAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    [RelayCommand]
    private Task CaptureAsync() => StartStopAsync();

    private async Task SelectAndCaptureAsync()
    {
        if (!PlatformServices.IsInitialized || PlatformServices.ScrollingCapture is not { IsSupported: true } scrollService)
        {
            StatusText = UnsupportedText;
            return;
        }

        IsOptionsOpen = false;
        SetMinimizedRequested?.Invoke(true);
        await Task.Delay(250);

        // Start scroll input before the selection: on Wayland this is a remote desktop session, whose
        // permission dialog and "session started" notification then come before, not during, the capture.
        if (!await scrollService.BeginAsync())
        {
            Status = ScrollingCaptureStatus.Failed;
            StatusText = InputUnavailableText;
            SetMinimizedRequested?.Invoke(false);
            return;
        }

        ScrollingCaptureTarget? target = null;
        try
        {
            target = SelectTargetRequested == null ? null : await SelectTargetRequested();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "ScrollingCapture selection");
        }

        if (target is not { } selected || selected.WindowHandle == IntPtr.Zero || selected.Region.IsEmpty)
        {
            await scrollService.EndAsync();
            StatusText = SelectionCancelledText;
            SetMinimizedRequested?.Invoke(false);
            return;
        }

        await CaptureTargetAsync(scrollService, selected);
    }

    private async Task CaptureTargetAsync(IScrollingCaptureService scrollService, ScrollingCaptureTarget target)
    {
        IsCapturing = true;
        Status = null;
        StatusText = CapturingText;
        ClearResult();
        _captureCts = new CancellationTokenSource();
        IDisposable? regionBorder = _options.ShowRegion ? ShowRegionRequested?.Invoke(target.Region) : null;

        try
        {
            var manager = new ScrollingCaptureManager(scrollService, PlatformServices.ScreenCapture, PlatformServices.Window);
            var region = target.Region;
            var result = await manager.CaptureAsync(
                target.WindowHandle,
                new SKRect(region.Left, region.Top, region.Right, region.Bottom),
                scrollMethod: _options.ScrollMethod,
                scrollAmount: _options.ScrollAmount,
                startDelayMs: _options.StartDelay,
                scrollDelayMs: _options.ScrollDelay,
                autoScrollTop: _options.AutoScrollTop,
                autoIgnoreBottomEdge: _options.AutoIgnoreBottomEdge,
                cancellationToken: _captureCts.Token);

            Status = result.Status;
            StatusText = result.InputUnavailable ? InputUnavailableText : result.Status switch
            {
                ScrollingCaptureStatus.Successful => SuccessfulText,
                ScrollingCaptureStatus.PartiallySuccessful => PartiallySuccessfulText,
                _ => FailedText
            };
            SetResult(result.Image);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "ScrollingCapture");
            Status = ScrollingCaptureStatus.Failed;
            StatusText = ex.Message;
        }
        finally
        {
            regionBorder?.Dispose();
            IsCapturing = false;
            _captureCts?.Dispose();
            _captureCts = null;
            SetMinimizedRequested?.Invoke(false);
        }

        if (_options.AutoUpload && _capturedSkBitmap != null)
        {
            await UploadAsync();
        }

        CaptureFinished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops a running capture; the frames captured so far are kept, as in ShareX.</summary>
    [RelayCommand]
    public void StopCapture()
    {
        if (IsCapturing && _captureCts is { IsCancellationRequested: false })
        {
            StatusText = StoppingText;
            _captureCts.Cancel();
        }
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (_capturedSkBitmap == null || UploadRequested == null) return;
        await UploadRequested(_capturedSkBitmap.Copy());
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (_capturedSkBitmap == null) return;

        try
        {
            PlatformServices.Clipboard.SetImage(_capturedSkBitmap);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to copy: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenOptions()
    {
        LoadOptions();
        IsOptionsOpen = true;
    }

    [RelayCommand]
    private void CancelOptions() => IsOptionsOpen = false;

    [RelayCommand]
    private void SaveOptions()
    {
        _options.StartDelay = (int)StartDelay;
        _options.ScrollDelay = (int)ScrollDelay;
        _options.ScrollMethod = ScrollMethod;
        _options.ScrollAmount = (int)ScrollAmount;
        _options.AutoScrollTop = AutoScrollTop;
        _options.AutoIgnoreBottomEdge = AutoIgnoreBottomEdge;
        _options.ShowRegion = ShowRegion;
        _options.AutoUpload = AutoUpload;
        SaveOptionsRequested?.Invoke();
        IsOptionsOpen = false;
    }

    [RelayCommand]
    private void OpenHelp() => URLHelpers.OpenURL(Links.DocsScrollingScreenshot);

    private void LoadOptions()
    {
        StartDelay = _options.StartDelay;
        ScrollDelay = _options.ScrollDelay;
        // A method this platform cannot perform (a Windows message on Linux) shows as the mouse wheel.
        ScrollMethod = ScrollMethods.Contains(_options.ScrollMethod) ? _options.ScrollMethod : ScrollMethod.MouseWheel;
        ScrollAmount = _options.ScrollAmount;
        AutoScrollTop = _options.AutoScrollTop;
        AutoIgnoreBottomEdge = _options.AutoIgnoreBottomEdge;
        ShowRegion = _options.ShowRegion;
        AutoUpload = _options.AutoUpload;
    }

    private void SetResult(SKBitmap? image)
    {
        if (image == null)
        {
            return;
        }

        _capturedSkBitmap = image;
        PreviewImage = BitmapConversionHelpers.ToAvaloniBitmap(image);
        ResultSize = $"{image.Width}x{image.Height}";
        HasResult = true;
    }

    private void ClearResult()
    {
        _capturedSkBitmap?.Dispose();
        _capturedSkBitmap = null;
        PreviewImage = null;
        ResultSize = "";
        HasResult = false;
    }

    public void Cleanup()
    {
        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _capturedSkBitmap?.Dispose();
        _capturedSkBitmap = null;
        PreviewImage = null;
    }
}
