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

using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

public partial class ScrollingCaptureWindow : SurfaceWindow
{
    private IDisposable? _escapeStop;
    private bool _escapeStopWanted;
    private bool _closeRequested;

    /// <summary>Shows a scroll method by its description, as ShareX's localized names.</summary>
    public static IValueConverter ScrollMethodNameConverter { get; } = new FuncValueConverter<ScrollMethod, string>(method => EnumExtensions.GetDescription(method));

    public ScrollingCaptureWindow()
    {
        InitializeComponent();
        // As in ShareX, the window starts minimized and the area selection starts when it opens.
        WindowState = WindowState.Minimized;
        // As in ShareX, clicking the window stops a running capture.
        Activated += (_, _) => ViewModel?.StopCapture();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private ScrollingCaptureViewModel? ViewModel => DataContext as ScrollingCaptureViewModel;

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.CaptureFinished += OnCaptureFinished;
        vm.SetMinimizedRequested = SetMinimized;
        await vm.StartStopAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (ViewModel is { IsCapturing: true } capturing)
        {
            // Stop first, then close when the capture has ended, as ShareX does.
            _closeRequested = true;
            capturing.StopCapture();
            e.Cancel = true;
            return;
        }

        UnregisterEscapeStopHotkey();
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.CaptureFinished -= OnCaptureFinished;
            vm.Cleanup();
        }

        base.OnClosing(e);
    }

    private void OnCaptureFinished(object? sender, EventArgs e)
    {
        if (_closeRequested)
        {
            Dispatcher.UIThread.Post(Close);
        }
    }

    private void SetMinimized(bool minimized)
    {
        if (minimized)
        {
            WindowState = WindowState.Minimized;
            return;
        }

        WindowState = WindowState.Normal;
        if (!IsVisible)
        {
            Show();
        }

        Activate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScrollingCaptureViewModel.IsCapturing) || ViewModel is not { } vm)
        {
            return;
        }

        if (vm.IsCapturing)
        {
            RegisterEscapeStopHotkey();
        }
        else
        {
            UnregisterEscapeStopHotkey();
        }
    }

    /// <summary>
    /// Escape stops a running capture, in addition to the hotkey and activating the window as in ShareX.
    /// The hotkey service binds it only while capturing; where it cannot (the GlobalShortcuts portal
    /// outside KDE, macOS), Escape does nothing here.
    /// </summary>
    private async void RegisterEscapeStopHotkey()
    {
        if (_escapeStopWanted || !PlatformServices.IsInitialized)
        {
            return;
        }

        _escapeStopWanted = true;
        try
        {
            IDisposable? registration = await PlatformServices.Hotkey.RegisterTemporaryHotkeyAsync(new HotkeyInfo(Key.Escape),
                () => Dispatcher.UIThread.Post(() => ViewModel?.StopCapture()));
            if (_escapeStopWanted && _escapeStop == null)
            {
                _escapeStop = registration;
            }
            else
            {
                // The capture ended while the key was being bound.
                registration?.Dispose();
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "ScrollingCapture: binding Escape");
        }
    }

    private void UnregisterEscapeStopHotkey()
    {
        _escapeStopWanted = false;
        IDisposable? registration = _escapeStop;
        _escapeStop = null;
        try
        {
            registration?.Dispose();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "ScrollingCapture: removing Escape");
        }
    }
}
