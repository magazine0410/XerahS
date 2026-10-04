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
using SkiaSharp;
using XerahS.Common;
using XerahS.History;
using XerahS.Platform.Abstractions;

namespace XerahS.UI.ViewModels;

public partial class HistoryViewModel
{
    internal IReadOnlyList<HistoryItem> GetHistoryActionItems(HistoryItem item) =>
        SelectedHistoryItems.Contains(item) ? SelectedHistoryItems.ToArray() : [item];

    internal async Task CopyHistoryItemsAsync(HistoryItem item, HistoryCopyFormat format)
    {
        IReadOnlyList<HistoryItem> items = GetHistoryActionItems(item);
        try
        {
            if (format == HistoryCopyFormat.File)
            {
                string[] paths = items.Select(entry => entry.FilePath).Where(File.Exists).ToArray();
                if (paths.Length > 0) PlatformServices.Clipboard.SetFileDropList(paths);
                return;
            }

            if (format == HistoryCopyFormat.Image)
            {
                if (items.Count != 1 || !File.Exists(item.FilePath) || !FileHelpers.IsImageFile(item.FilePath)) return;
                using var bitmap = await Task.Run(() => SKBitmap.Decode(item.FilePath));
                if (bitmap == null) throw new InvalidDataException("Could not read the image.");
                PlatformServices.Clipboard.SetImage(bitmap);
                return;
            }

            string? text;
            if (format == HistoryCopyFormat.TextContents)
            {
                if (items.Count != 1 || !FileHelpers.IsTextFile(item.FilePath) || !File.Exists(item.FilePath)) return;
                text = await File.ReadAllTextAsync(item.FilePath);
            }
            else if (format == HistoryCopyFormat.ImageDimensions)
            {
                if (items.Count != 1 || !File.Exists(item.FilePath)) return;
                text = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(item.FilePath);
                    using var codec = SKCodec.Create(stream);
                    return codec == null ? throw new InvalidDataException("Could not read the image dimensions.")
                        : $"{codec.Info.Width} x {codec.Info.Height}";
                });
            }
            else
            {
                text = string.Join(Environment.NewLine, items.Select(entry => HistoryCopyText.GetValue(entry, format))
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            if (text != null && (text.Length > 0 || format == HistoryCopyFormat.TextContents))
                await PlatformServices.Clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to copy history item");
            await _coreDialogService.ShowErrorAsync("Copy", "Could not copy the selected history item. " + ex.Message);
        }
    }

    internal void OpenHistoryUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            PlatformServices.System.OpenUrl(url);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to open history URL");
            ShowCloudToast("Open URL", "Could not open the URL. " + ex.Message);
        }
    }

    internal Task ShowHistoryErrorsAsync(HistoryItem item) => item.HasErrors
        ? _coreDialogService.ShowErrorAsync("Errors", item.Errors!) : Task.CompletedTask;
}
