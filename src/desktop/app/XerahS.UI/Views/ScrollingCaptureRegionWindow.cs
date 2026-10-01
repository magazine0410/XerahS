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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using XerahS.Platform.Abstractions;

namespace XerahS.UI.Views;

/// <summary>
/// ShareX's "Show scrolling capture region": a 1-pixel lime border just outside the captured area,
/// so it is not part of the frames. Clicks and the mouse wheel pass through the window.
/// </summary>
public sealed class ScrollingCaptureRegionWindow : Window
{
    private const int BorderPixels = 1;
    private readonly System.Drawing.Rectangle _region;

    public ScrollingCaptureRegionWindow()
        : this(new System.Drawing.Rectangle(0, 0, 640, 420))
    {
    }

    public ScrollingCaptureRegionWindow(System.Drawing.Rectangle region)
    {
        _region = region;
        Title = "XerahS - Scrolling capture region";
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(region.X - BorderPixels, region.Y - BorderPixels);
        Content = new Border { BorderBrush = Brushes.Lime, IsHitTestVisible = false };
        ApplyGeometry(1);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyGeometry(Screens.ScreenFromPoint(Position)?.Scaling ?? 1);

        if (TryGetPlatformHandle()?.Handle is IntPtr handle && handle != IntPtr.Zero && PlatformServices.IsWindowServiceInitialized)
        {
            PlatformServices.Window.SetWindowClickThrough(handle);
        }
    }

    private void ApplyGeometry(double scaling)
    {
        scaling = Math.Max(0.5, scaling);
        Width = (_region.Width + BorderPixels * 2) / scaling;
        Height = (_region.Height + BorderPixels * 2) / scaling;
        if (Content is Border border)
        {
            border.BorderThickness = new Thickness(BorderPixels / scaling);
        }
    }
}
