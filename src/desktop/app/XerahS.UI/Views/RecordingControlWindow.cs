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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using XerahS.Bootstrap;
using XerahS.Common;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.RegionCapture.ScreenRecording;
using XerahS.UI.Services;

namespace XerahS.UI.Views;

/// <summary>
/// The recording controls, after ShareX's ScreenRecordWindow toolbar: a timer, Start/Stop, Pause/Resume, Restart and
/// Abort, placed below the recorded area, with the abort confirmation shown in the toolbar itself.
/// </summary>
internal sealed class RecordingControlWindow : Window
{
    // ShareX's ScreenRecordWindow: a 1 px frame around the region, then a 3 px gap above the toolbar.
    private const int BorderPixels = 1;
    private const int ToolbarGapPixels = 3;

    private readonly IScreenRecordingCoordinator _coordinator;
    private readonly TextBlock _timer = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly Border _timerHandle;
    private readonly StackPanel _controls = new() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _abortConfirmation = new() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, IsVisible = false };
    private readonly Button _startStop;
    private readonly Button _pause;
    private readonly Button _restart;
    private readonly Button _abort;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _sinceStatus = new();
    private RecordingOptions _options;
    private RecordingStatus _status = RecordingStatus.Waiting;
    private TimeSpan _statusDuration;
    private bool _allowClose;
    private Task _highlightWork = Task.CompletedTask;
    private IAsyncDisposable? _highlight;
    private bool _highlightWanted;

    /// <summary>The open recording controls; the tray and the main window's recording page ask them to abort.</summary>
    public static RecordingControlWindow? Current { get; private set; }

    public RecordingControlWindow(IScreenRecordingCoordinator coordinator, RecordingOptions options)
    {
        _coordinator = coordinator;
        _options = options;
        Title = "Screen recorder";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        ShowActivated = !options.AutoStart;

        // As ShareX's timer, the timer is the handle for moving the toolbar.
        _timerHandle = new Border { Child = _timer, Padding = new Thickness(8, 0), MinWidth = 96, Cursor = new Cursor(StandardCursorType.SizeAll) };
        _timerHandle.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        _controls.Children.Add(_timerHandle);
        _startStop = AddButton(_controls, StartStop);
        _pause = AddButton(_controls, PauseResume);
        _restart = AddButton(_controls, () => { _coordinator.RequestRestart(); return Task.CompletedTask; });
        _abort = AddButton(_controls, () => { RequestAbort(); return Task.CompletedTask; });

        _abortConfirmation.Children.Add(new TextBlock
        {
            Text = "Abort this recording?",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Thickness(10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        AddButton(_abortConfirmation, () => { ShowAbortConfirmation(false); return Task.CompletedTask; }).Content = "Cancel";
        AddButton(_abortConfirmation, AbortAsync).Content = "Abort";

        Content = new Border
        {
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Avalonia.Media.Brushes.Gray,
            Child = new Panel { Children = { _controls, _abortConfirmation } }
        };

        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            RequestAbort();
        };
        Closed += (_, _) =>
        {
            _refresh.Stop();
            SetHighlight(false);
            if (ReferenceEquals(Current, this)) Current = null;
        };
        Opened += (_, _) => Current = this;
        _refresh.Tick += (_, _) => UpdateTimer();
        Reset(options);
        PlaceOutsideRecording();
    }

    /// <summary>Starts a new take in the same controls (Restart), as ShareX keeps its window.</summary>
    public void Reset(RecordingOptions options)
    {
        _options = options;
        _timerHandle.IsVisible = options.ShowTimer;
        SetStatus(new RecordingStatusEventArgs(RecordingStatus.Waiting, TimeSpan.Zero));
        if (!IsVisible && Current == this) Show();
    }

    /// <summary>The Abort button, the tray's Abort and the main window's Abort: asks first when the workflow says so.</summary>
    public void RequestAbort()
    {
        if (_options.AskConfirmationOnAbort)
        {
            if (!IsVisible) Show();
            ShowAbortConfirmation(true);
            Activate();
        }
        else
        {
            _ = AbortAsync();
        }
    }

    public static Task RequestAbortAsync(IScreenRecordingCoordinator coordinator)
    {
        if (Current is { } controls)
        {
            controls.RequestAbort();
            return Task.CompletedTask;
        }
        return coordinator.AbortRecordingAsync();
    }

    public void SetStatus(RecordingStatusEventArgs e)
    {
        bool changed = e.Status != _status;
        _status = e.Status;
        _statusDuration = e.Duration;
        _sinceStatus.Restart();
        if (changed) ShowAbortConfirmation(false);
        UpdateButtons();
        UpdateTimer();
        if (e.Status is RecordingStatus.Waiting or RecordingStatus.Recording) _refresh.Start();
        else _refresh.Stop();
        SetHighlight(e.Status == RecordingStatus.Recording && _options.HighlightMouse);

        // ShareX hides its toolbar while encoding; the tray shows the progress.
        if (e.Status == RecordingStatus.Finalizing && IsVisible) Hide();
        if (e.Status is RecordingStatus.Idle or RecordingStatus.Error) Finish();
    }

    public void Finish()
    {
        _allowClose = true;
        Close();
    }

    internal string TimerText => _timer.Text ?? string.Empty;
    internal bool IsAbortConfirmationVisible => _abortConfirmation.IsVisible;
    internal string StartStopLabel => ToolTip.GetTip(_startStop) as string ?? string.Empty;
    internal string PauseLabel => ToolTip.GetTip(_pause) as string ?? string.Empty;

    /// <summary>The timer value: the start delay counting down, then the elapsed time, or with a fixed duration the time left.</summary>
    internal static TimeSpan GetTimerValue(RecordingStatus status, TimeSpan statusDuration, TimeSpan sinceStatus, RecordingOptions options)
    {
        if (status == RecordingStatus.Waiting)
            return statusDuration > TimeSpan.Zero ? Max(TimeSpan.Zero, statusDuration - sinceStatus) : TimeSpan.Zero;
        TimeSpan elapsed = status == RecordingStatus.Recording ? statusDuration + sinceStatus : statusDuration;
        return options.Duration > 0 ? Max(TimeSpan.Zero, TimeSpan.FromSeconds(options.Duration) - elapsed) : elapsed;
    }

    /// <summary>
    /// Below the recorded area and centered on it, as ShareX places its toolbar. Above it, then inside its bottom edge,
    /// when the toolbar would leave the screens.
    /// </summary>
    internal static PixelPoint PlaceToolbar(PixelRect recording, PixelSize toolbar, IReadOnlyList<PixelRect> workingAreas)
    {
        int x = recording.X + (recording.Width - toolbar.Width) / 2;
        int gap = BorderPixels + ToolbarGapPixels;
        PixelRect below = new(x, recording.Bottom + gap, toolbar.Width, toolbar.Height);
        PixelRect above = new(x, recording.Y - gap - toolbar.Height, toolbar.Width, toolbar.Height);
        PixelRect inside = new(x, recording.Bottom - gap - toolbar.Height, toolbar.Width, toolbar.Height);
        return (Fit(below, workingAreas) ?? Fit(above, workingAreas) ?? Fit(inside, workingAreas) ?? inside).Position;
    }

    private static PixelRect? Fit(PixelRect candidate, IReadOnlyList<PixelRect> workingAreas)
    {
        foreach (PixelRect area in workingAreas)
        {
            bool vertical = candidate.Y >= area.Y && candidate.Bottom <= area.Bottom;
            bool horizontal = candidate.X < area.Right && candidate.Right > area.X;
            if (!vertical || !horizontal) continue;
            int x = Math.Clamp(candidate.X, area.X, Math.Max(area.X, area.Right - candidate.Width));
            return candidate.WithX(x);
        }
        return null;
    }

    private void PlaceOutsideRecording()
    {
        if (!TryGetRecordingArea(_options, out PixelRect area)) return;
        try
        {
            ((Control)Content!).Measure(Size.Infinity);
            Size desired = ((Control)Content!).DesiredSize;
            var screen = Screens.ScreenFromPoint(area.Center) ?? Screens.Primary;
            double scaling = screen?.Scaling ?? 1;
            var size = new PixelSize((int)Math.Ceiling(desired.Width * scaling), (int)Math.Ceiling(desired.Height * scaling));
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = PlaceToolbar(area, size, Screens.All.Select(s => s.WorkingArea).ToList());
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "RecordingControlWindow: could not place the controls below the recording");
        }
    }

    /// <summary>The recorded area when XerahS knows it: a region, or a window on X11. The portal's choice is not reported.</summary>
    private static bool TryGetRecordingArea(RecordingOptions options, out PixelRect area)
    {
        area = default;
        System.Drawing.Rectangle bounds = default;
        if (options.Mode == CaptureMode.Region) bounds = options.Region;
        else if (options.Mode == CaptureMode.Window && options.TargetWindowHandle != IntPtr.Zero && PlatformServices.IsInitialized)
        {
            try { bounds = PlatformServices.Window.GetWindowBounds(options.TargetWindowHandle); }
            catch (Exception ex) { DebugHelper.WriteException(ex, "RecordingControlWindow: could not read the recorded window's bounds"); }
        }
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;
        area = new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        return true;
    }

    private Task StartStop()
    {
        switch (_status)
        {
            case RecordingStatus.Waiting:
                _coordinator.SignalStart();
                break;
            case RecordingStatus.Initializing:
                // As ShareX: stopping before the recording has started aborts it, without asking.
                return AbortAsync();
            default:
                _coordinator.SignalStop();
                break;
        }
        return Task.CompletedTask;
    }

    private Task PauseResume()
    {
        // As ShareX, "Resume" also starts a waiting recording.
        if (_status == RecordingStatus.Waiting)
        {
            _coordinator.SignalStart();
            return Task.CompletedTask;
        }
        return _coordinator.TogglePauseResumeAsync();
    }

    private async Task AbortAsync()
    {
        ShowAbortConfirmation(false);
        try { await _coordinator.AbortRecordingAsync(); }
        catch (Exception ex) { UploadWorkflowService.ReportError(ex, "Recording"); }
    }

    private void ShowAbortConfirmation(bool show)
    {
        _controls.IsVisible = !show;
        _abortConfirmation.IsVisible = show;
    }

    private static Button AddButton(Panel panel, Func<Task> action)
    {
        var button = new Button { MinWidth = 40, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += async (_, _) =>
        {
            // Errors are reported as notifications; a dialog would bring the main window over the recording.
            try { await action(); }
            catch (Exception ex) { UploadWorkflowService.ReportError(ex, "Recording"); }
        };
        panel.Children.Add(button);
        return button;
    }

    private void SetLabel(Button button, string icon, string label)
    {
        button.Content = _options.ShowButtonLabels ? $"{icon}  {label}" : icon;
        ToolTip.SetTip(button, label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
    }

    private void UpdateButtons()
    {
        bool waiting = _status == RecordingStatus.Waiting;
        bool paused = _status == RecordingStatus.Paused;
        SetLabel(_startStop, waiting ? "▶" : "■", waiting ? "Start" : "Stop");
        SetLabel(_pause, waiting || paused ? "▶" : "⏸", waiting || paused ? "Resume" : "Pause");
        SetLabel(_restart, "↻", "Restart");
        SetLabel(_abort, "✕", "Abort");
        _startStop.IsEnabled = _status is RecordingStatus.Waiting or RecordingStatus.Initializing or RecordingStatus.Recording or RecordingStatus.Paused;
        _pause.IsEnabled = waiting || (_status is RecordingStatus.Recording or RecordingStatus.Paused && _coordinator.CurrentCapabilities.SupportsPauseResume);
        _restart.IsEnabled = _status is RecordingStatus.Recording or RecordingStatus.Paused;
        _abort.IsEnabled = _status is not (RecordingStatus.Finalizing or RecordingStatus.Idle or RecordingStatus.Error);
    }

    private void UpdateTimer() =>
        _timer.Text = GetTimerValue(_status, _statusDuration, _sinceStatus.Elapsed, _options).ToString(@"mm\:ss\:ff");

    /// <summary>Starts or stops the recording highlight; calls run one at a time, so a quick pause/resume cannot interleave.</summary>
    private void SetHighlight(bool wanted)
    {
        if (_highlightWanted == wanted) return;
        _highlightWanted = wanted;
        _highlightWork = UpdateHighlightAsync(_highlightWork);
    }

    private async Task UpdateHighlightAsync(Task previous)
    {
        await previous;
        try
        {
            if (_highlightWanted && _highlight == null)
            {
                _highlight = await MouseHighlighterManager.BeginRecordingAsync(
                    (_options as WorkflowRecordingOptions)?.MouseHighlighterOptions ?? new());
            }
            else if (!_highlightWanted && _highlight is { } highlight)
            {
                _highlight = null;
                await highlight.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            // Report it once, then record without highlighting.
            _options.HighlightMouse = false;
            _highlightWanted = false;
            UploadWorkflowService.ReportError(ex, "Mouse highlighter");
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
