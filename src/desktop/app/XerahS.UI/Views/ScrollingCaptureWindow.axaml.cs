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
    private static readonly HotkeyInfo s_escapeStopHotkey = new(Key.Escape);
    private bool _escapeHotkeyRegistered;
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

    private void RegisterEscapeStopHotkey()
    {
        // Windows only: on Wayland a global shortcut goes through the portal, which would bind Escape
        // for the whole desktop. ShareX stops with the hotkey again or by activating the window.
        if (_escapeHotkeyRegistered || !PlatformServices.IsInitialized || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (PlatformServices.Hotkey.RegisterHotkey(s_escapeStopHotkey))
            {
                _escapeHotkeyRegistered = true;
                PlatformServices.Hotkey.HotkeyTriggered += OnHotkeyTriggered;
            }
        }
        catch
        {
            // Ignore - hotkey may not be supported on this platform
        }
    }

    private void UnregisterEscapeStopHotkey()
    {
        if (!_escapeHotkeyRegistered || !PlatformServices.IsInitialized)
        {
            return;
        }

        try
        {
            PlatformServices.Hotkey.HotkeyTriggered -= OnHotkeyTriggered;
            PlatformServices.Hotkey.UnregisterHotkey(s_escapeStopHotkey);
        }
        finally
        {
            _escapeHotkeyRegistered = false;
        }
    }

    private void OnHotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        if (e.HotkeyInfo.Key != Key.Escape || ViewModel is not { IsCapturing: true } vm)
        {
            return;
        }

        Dispatcher.UIThread.Post(vm.StopCapture);
    }
}
