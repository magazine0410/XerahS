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
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using XerahS.Bootstrap;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;

namespace XerahS.UI.Views;

public partial class DragDropUploadWindow : Window
{
    private TaskSettings _settings = new();
    private IDesktopTaskManager? _taskManager;
    private int _offset;
    private ContentPlacement _alignment;
    private double _normalOpacity;
    private double _hoverOpacity;

    public DragDropUploadWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += (_, _) => Dispatcher.UIThread.Post(PositionWindow, DispatcherPriority.Loaded);
        ApplySettings(SettingsManager.Settings);
    }

    internal void Configure(TaskSettings settings, IDesktopTaskManager taskManager)
    {
        _settings = UploadWorkflowService.CreateExecutionSettings(settings, WorkflowType.DragDropUpload);
        _taskManager = taskManager;
    }

    internal void ApplySettings(ApplicationConfig settings)
    {
        Width = Height = Math.Clamp(settings.DropSize, 10, 300);
        _offset = Math.Clamp(settings.DropOffset, 0, 1000);
        _alignment = settings.DropAlignment;
        _normalOpacity = Math.Clamp(settings.DropOpacity, 1, 255) / 255d;
        _hoverOpacity = Math.Clamp(settings.DropHoverOpacity, 1, 255) / 255d;
        DropText.IsVisible = Width >= 55;
        DropText.FontSize = Math.Clamp(Width / 7, 10, 20);
        DropIcon.IsVisible = Width >= 35;
        SetHovered(false);
        if (IsVisible) PositionWindow();
    }

    private void PositionWindow()
    {
        if (Screens.Primary is not { } screen) return;
        Position = CalculatePosition(screen.WorkingArea, PixelSize.FromSize(new Size(Width, Height), screen.Scaling), _alignment, _offset);
    }

    internal static PixelPoint CalculatePosition(PixelRect area, PixelSize size, ContentPlacement alignment, int offset)
    {
        int x = alignment switch
        {
            ContentPlacement.TopLeft or ContentPlacement.MiddleLeft or ContentPlacement.BottomLeft => area.X + offset,
            ContentPlacement.TopCenter or ContentPlacement.MiddleCenter or ContentPlacement.BottomCenter => area.X + (area.Width - size.Width) / 2,
            _ => area.Right - size.Width - offset
        };
        int y = alignment switch
        {
            ContentPlacement.TopLeft or ContentPlacement.TopCenter or ContentPlacement.TopRight => area.Y + offset,
            ContentPlacement.MiddleLeft or ContentPlacement.MiddleCenter or ContentPlacement.MiddleRight => area.Y + (area.Height - size.Height) / 2,
            _ => area.Bottom - size.Height - offset
        };
        return new PixelPoint(Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width)),
            Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height)));
    }

    internal static bool HasSupportedData(IDataTransfer data) =>
        UploadContentWindow.GetDroppedStorageItems(data).Any(item => item.TryGetLocalPath() != null) ||
        data.Formats.Contains(DataFormat.Bitmap) || !string.IsNullOrWhiteSpace(data.TryGetText());

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool supported = HasSupportedData(e.DataTransfer);
        e.DragEffects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        SetHovered(supported);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        SetHovered(false);
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        SetHovered(false);
        try
        {
            await UploadDroppedDataAsync(e.DataTransfer);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { UploadWorkflowService.ReportError(ex); }
    }

    internal Task UploadDroppedDataAsync(IDataTransfer data) => _taskManager == null ? Task.CompletedTask :
        UploadWorkflowService.UploadDroppedDataAsync(data, _settings, _taskManager);

    private void SetHovered(bool hovered)
    {
        Opacity = hovered ? _hoverOpacity : _normalOpacity;
        DropSurface.Classes.Set("drag-over", hovered);
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); SetHovered(true); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); SetHovered(false); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.LeftButtonPressed) { BeginMoveDrag(e); e.Handled = true; }
        else if (kind == PointerUpdateKind.RightButtonPressed) { Close(); e.Handled = true; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
