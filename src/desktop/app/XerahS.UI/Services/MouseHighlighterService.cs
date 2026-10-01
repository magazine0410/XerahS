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
using Avalonia.Threading;
using System.Diagnostics;
using XerahS.Core;
using XerahS.Core.Tools;
using XerahS.Platform.Abstractions;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

internal sealed class MouseHighlighterService : IDisposable
{
    private readonly List<MouseHighlighterOverlayWindow> _windows = [];
    private readonly DispatcherTimer _timer;
    private readonly IGlobalMouseMonitor? _hook;
    private readonly Window _screenProbe = new();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private readonly MouseHighlighterState _state;
    private bool _disposed;
    public MouseHighlighterOptions Options => _state.Options;
    public System.Drawing.Point CursorPosition => _state.CursorPosition;
    public IReadOnlyList<MouseHighlight> Highlights => _state.Highlights;
    public double Time => _state.Time;

    public MouseHighlighterService(MouseHighlighterOptions options)
    {
        var input = new MouseHighlighterInputBuffer(PlatformServices.Input.GetCursorPosition());
        _state = new MouseHighlighterState(options, input, _startTimestamp);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000d / 60) };
        _timer.Tick += OnTick;
        try
        {
            _hook = PlatformServices.Input.CreateGlobalMouseMonitor(input);
            CreateOverlays();
            _screenProbe.Screens.Changed += OnScreensChanged;
            _timer.Start();
        }
        catch { Dispose(); throw; }
    }

    public void UpdateOptions(MouseHighlighterOptions options) => _state.UpdateOptions(options);

    private void CreateOverlays()
    {
        foreach (var screen in _screenProbe.Screens.All)
        {
            var window = new MouseHighlighterOverlayWindow(this, screen.Bounds, screen.Scaling);
            _windows.Add(window);
            window.Refresh();
        }
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        try
        {
            foreach (var window in _windows) window.Dispose();
            _windows.Clear();
            CreateOverlays();
        }
        catch (Exception ex) { MouseHighlighterManager.StopOnError(this, ex); }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_hook?.Failure is Exception failure) { MouseHighlighterManager.StopOnError(this, failure); return; }
        _state.Advance(Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds);
        try { foreach (var window in _windows) window.Refresh(); }
        catch (Exception ex) { MouseHighlighterManager.StopOnError(this, ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _hook?.Dispose();
        _screenProbe.Screens.Changed -= OnScreensChanged;
        foreach (var window in _windows) window.Dispose();
        _screenProbe.Close();
        _windows.Clear();
    }
}
