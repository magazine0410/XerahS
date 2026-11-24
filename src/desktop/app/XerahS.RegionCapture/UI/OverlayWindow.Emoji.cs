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
using Avalonia.Layout;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Presentation.Emoji;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
using SkiaSharp;

namespace XerahS.RegionCapture.UI;

public partial class OverlayWindow
{
    // The image editor's default sticker size.
    private const float EmojiStickerSize = 160;

    private EmojiPickerDialogView? _emojiPicker;
    private Viewbox? _emojiPickerHost;

    /// <summary>
    /// Shows the image editor's emoji picker inside the overlay, as its Emoji button does there.
    /// </summary>
    private void OnEmojiPickerRequested()
    {
        if (_emojiPicker != null || !_options.EnableAnnotations || _monitorState == OverlayMonitorState.Inactive ||
            this.FindControl<Panel>("ModalLayer") is not { } modalLayer)
        {
            return;
        }

        CommitInlineText();

        _emojiPicker = new EmojiPickerDialogView
        {
            DataContext = new EmojiPickerDialogViewModel(InsertEmoji, CloseEmojiPicker),
            Focusable = true
        };

        // The picker has a fixed size of 760x640; on a smaller monitor it is scaled down to fit.
        _emojiPickerHost = new Viewbox
        {
            Child = _emojiPicker,
            StretchDirection = Avalonia.Media.StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(12)
        };
        modalLayer.Children.Add(_emojiPickerHost);
        modalLayer.IsVisible = true;

        // Keys must reach the overlay through the picker from the start; the search box takes the
        // focus itself once the emoji list has loaded.
        _emojiPicker.Focus();
    }

    private void CloseEmojiPicker()
    {
        if (_emojiPicker == null)
        {
            return;
        }

        if (this.FindControl<Panel>("ModalLayer") is { } modalLayer && _emojiPickerHost != null)
        {
            modalLayer.Children.Remove(_emojiPickerHost);
            modalLayer.IsVisible = false;
        }

        _emojiPickerHost = null;
        _emojiPicker = null;
        Focus();
        _captureControl.Focus();
    }

    /// <summary>
    /// Adds the picked emoji as the image editor does: a sticker in the middle of the canvas,
    /// selected, with the Select tool active so it can be moved and resized at once.
    /// </summary>
    private void InsertEmoji(EmojiCatalogEntry emoji)
    {
        CloseEmojiPicker();

        float left = (float)(Width - EmojiStickerSize) / 2;
        float top = (float)(Height - EmojiStickerSize) / 2;
        var annotation = new EmojiAnnotation
        {
            UnicodeSequence = emoji.Unicode,
            DisplayName = emoji.Name,
            StartPoint = new SKPoint(left, top),
            EndPoint = new SKPoint(left + EmojiStickerSize, top + EmojiStickerSize)
        };

        var editorCore = _viewModel.EditorCore;
        editorCore.AddAnnotation(annotation);
        editorCore.Select(annotation);
        _viewModel.SelectToolCommand.Execute(EditorTool.Select);
        SyncAnnotationState();
        RebuildAnnotationCanvas();
    }
}
