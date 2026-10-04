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
using Avalonia.Media.Imaging;
using XerahS.Common;

namespace XerahS.UI.Controls;

public sealed class HistoryThumbnail : Image
{
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<HistoryThumbnail, string?>(nameof(FilePath));
    public static readonly StyledProperty<int> DecodeWidthProperty =
        AvaloniaProperty.Register<HistoryThumbnail, int>(nameof(DecodeWidth), 250);
    private static readonly SemaphoreSlim DecodeSlots = new(4);
    private Bitmap? _bitmap;
    private int _version;
    private bool _attached;

    public string? FilePath { get => GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public int DecodeWidth { get => GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _ = LoadThumbnailAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _version++;
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_attached && (change.Property == FilePathProperty || change.Property == DecodeWidthProperty))
            _ = LoadThumbnailAsync();
    }

    private async Task LoadThumbnailAsync()
    {
        int version = ++_version;
        string? path = FilePath;
        int width = Math.Clamp(DecodeWidth, 32, 800);
        Bitmap? bitmap = null;
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        if (string.IsNullOrWhiteSpace(path) || !FileHelpers.IsImageFile(path)) return;
        await DecodeSlots.WaitAsync();
        try
        {
            if (!_attached || version != _version) return;
            bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, width);
            });
        }
        catch (Exception)
        {
            // Deleted or unreadable history images remain usable as metadata-only entries.
        }
        finally
        {
            DecodeSlots.Release();
        }
        if (!_attached || version != _version)
        {
            bitmap?.Dispose();
            return;
        }
        _bitmap = bitmap;
        Source = bitmap;
    }
}
