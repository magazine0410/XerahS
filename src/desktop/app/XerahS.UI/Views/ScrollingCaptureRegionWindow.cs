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
/// so it is not part of the frames. Each window is one edge of the border and is opaque: a single
/// transparent window over the area is drawn black by an X11 session without a compositor, and the
/// captured frames were then black. Clicks and the mouse wheel pass through the windows.
/// </summary>
public sealed class ScrollingCaptureRegionWindow : Window
{
    private const int BorderPixels = 1;
    private readonly System.Drawing.Rectangle _edge;

    public ScrollingCaptureRegionWindow()
        : this(new System.Drawing.Rectangle(0, 0, 640, BorderPixels))
    {
    }

    private ScrollingCaptureRegionWindow(System.Drawing.Rectangle edge)
    {
        _edge = edge;
        Title = "XerahS - Scrolling capture region";
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Lime;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(edge.X, edge.Y);
        ApplyGeometry(1);
    }

    /// <summary>The four edge windows around <paramref name="region"/>: top, bottom, left, and right.</summary>
    public static IReadOnlyList<ScrollingCaptureRegionWindow> CreateBorder(System.Drawing.Rectangle region)
    {
        int left = region.X - BorderPixels, top = region.Y - BorderPixels, width = region.Width + BorderPixels * 2;
        return
        [
            new(new System.Drawing.Rectangle(left, top, width, BorderPixels)),
            new(new System.Drawing.Rectangle(left, region.Bottom, width, BorderPixels)),
            new(new System.Drawing.Rectangle(left, region.Y, BorderPixels, region.Height)),
            new(new System.Drawing.Rectangle(region.Right, region.Y, BorderPixels, region.Height))
        ];
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
        Width = _edge.Width / scaling;
        Height = _edge.Height / scaling;
    }
}
