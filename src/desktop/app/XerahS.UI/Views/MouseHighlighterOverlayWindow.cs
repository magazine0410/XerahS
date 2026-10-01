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
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using XerahS.Core;
using XerahS.Core.Tools;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;

namespace XerahS.UI.Views;

internal sealed class MouseHighlighterOverlayWindow : Window, IDisposable
{
    private readonly MouseHighlighterService _service;
    private readonly Rectangle _screenBounds;
    private readonly double _scaling;
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private WriteableBitmap? _bitmap;
    private bool _clickThrough;

    public MouseHighlighterOverlayWindow(MouseHighlighterService service, PixelRect bounds, double scaling)
    {
        _service = service;
        _screenBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        _scaling = scaling;
        Title = "XerahS - Mouse highlighter overlay";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _image;
    }

    public void Refresh()
    {
        MouseHighlighterOptions options = _service.Options;
        Rectangle bounds = GetEffectBounds(options);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            if (IsVisible) Hide();
            return;
        }
        PixelSize size = new(bounds.Width, bounds.Height);
        if (_bitmap == null || _bitmap.PixelSize != size)
        {
            _image.Source = null;
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _image.Source = _bitmap;
        }
        using (var buffer = _bitmap.Lock())
        using (var surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul), buffer.Address, buffer.RowBytes))
        {
            if (surface == null) throw new InvalidOperationException("Could not allocate the mouse highlighter surface.");
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Translate(-bounds.X, -bounds.Y);
            Draw(surface.Canvas, options);
            surface.Canvas.Flush();
        }
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width / _scaling;
        Height = bounds.Height / _scaling;
        if (!IsVisible)
        {
            Show();
            _clickThrough = false;
        }
        if (!_clickThrough)
        {
            if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
                throw new PlatformNotSupportedException("Mouse highlighting requires a compositor that supports transparent windows.");
            IntPtr handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero || !PlatformServices.Window.SetWindowClickThrough(handle))
                throw new PlatformNotSupportedException("This window system cannot create a click-through highlight overlay.");
            _clickThrough = true;
        }
        _image.InvalidateVisual();
    }

    private Rectangle GetEffectBounds(MouseHighlighterOptions options)
    {
        Rectangle bounds = Rectangle.Empty;
        if (options.Mode == MouseHighlightMode.Circle && options.AlwaysColor.A > 0)
        {
            bounds = Rectangle.Intersect(Around(_service.CursorPosition, options.Radius + 2), _screenBounds);
        }
        foreach (MouseHighlight highlight in _service.Highlights)
        {
            Color color = options.GetColor(highlight.Button);
            if (color.A == 0) continue;
            int radius = options.Mode == MouseHighlightMode.Ripple ? options.RippleSize / 2 + 12 : options.Radius + 2;
            Rectangle effect = Rectangle.Intersect(Around(highlight.Position, radius), _screenBounds);
            if (effect.Width > 0 && effect.Height > 0)
            {
                bounds = bounds.Width <= 0 || bounds.Height <= 0 ? effect : Rectangle.Union(bounds, effect);
            }
        }
        return Rectangle.Intersect(bounds, _screenBounds);
    }

    private static Rectangle Around(System.Drawing.Point center, int radius) =>
        new(center.X - radius, center.Y - radius, radius * 2 + 1, radius * 2 + 1);

    private void Draw(SKCanvas canvas, MouseHighlighterOptions options)
    {
        double time = _service.Time;
        SKPoint cursor = new(_service.CursorPosition.X, _service.CursorPosition.Y);
        if (options.Mode == MouseHighlightMode.Circle)
        {
            using SKPaint always = Paint(options.AlwaysColor);
            canvas.DrawCircle(cursor, options.Radius, always);
        }
        foreach (MouseHighlight highlight in _service.Highlights)
        {
            Color color = options.GetColor(highlight.Button);
            SKPoint center = new(highlight.Position.X, highlight.Position.Y);
            if (options.Mode == MouseHighlightMode.Circle)
            {
                using SKPaint fill = Paint(color, FadeOpacity(highlight, options, time));
                canvas.DrawCircle(center, options.Radius, fill);
            }
            else
            {
                double progress = Math.Clamp((time - highlight.Started) / options.RippleDuration, 0, 1);
                double fade = highlight.Released.HasValue
                    ? 1 - Math.Clamp((time - highlight.Released.Value) / options.RippleDuration, 0, 1) : 1;
                float radius = (float)(options.RippleSize / 2d * (0.35 + 0.65 * progress));
                using SKPaint ring = Paint(color, fade * options.RippleIntensity);
                ring.Style = SKPaintStyle.Stroke;
                ring.StrokeWidth = (float)(3 * options.RippleIntensity);
                if (highlight.Crosshairs)
                {
                    float gap = radius * 0.4f;
                    canvas.DrawLine(center.X - radius, center.Y, center.X - gap, center.Y, ring);
                    canvas.DrawLine(center.X + gap, center.Y, center.X + radius, center.Y, ring);
                    canvas.DrawLine(center.X, center.Y - radius, center.X, center.Y - gap, ring);
                    canvas.DrawLine(center.X, center.Y + gap, center.X, center.Y + radius, ring);
                }
                else
                {
                    using SKPaint glow = Paint(color, fade * options.RippleIntensity * 0.3);
                    using SKMaskFilter blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
                    glow.MaskFilter = blur;
                    canvas.DrawCircle(center, radius, glow);
                    canvas.DrawCircle(center, radius, ring);
                }
            }
        }
    }

    private static double FadeOpacity(MouseHighlight highlight, MouseHighlighterOptions options, double time)
    {
        if (!highlight.Released.HasValue) return 1;
        double age = time - highlight.Released.Value - options.FadeDelay;
        if (age < 0) return 1;
        return options.FadeDuration == 0 ? 0 : Math.Clamp(1 - age / options.FadeDuration, 0, 1);
    }

    private static SKPaint Paint(Color color, double opacity = 1) => new()
    {
        IsAntialias = true,
        Color = new SKColor(color.R, color.G, color.B, (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255))
    };

    public void Dispose()
    {
        Close();
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
