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

using Newtonsoft.Json;
using SkiaSharp;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using XerahS.Services.Abstractions;

namespace XerahS.Core.Services;

public enum BrowserExtensionAction { None, UploadImage, UploadVideo, UploadAudio, UploadText, ShortenURL }
public sealed class BrowserExtensionInput
{
    public BrowserExtensionAction Action { get; set; }
    public string? URL { get; set; }
    public string? Text { get; set; }
}

public static class BrowserExtensionService
{
    public static async Task ProcessAsync(string json, ITaskManager manager, TaskSettings settings)
    {
        var input = JsonConvert.DeserializeObject<BrowserExtensionInput>(json) ?? throw new InvalidDataException("Invalid browser request.");
        NotificationSoundService.PlayActionCompleted(settings);
        if (!Enum.IsDefined(input.Action)) throw new InvalidDataException("Unknown browser action.");
        if (input.Action == BrowserExtensionAction.UploadText || (input.Action == BrowserExtensionAction.None && string.IsNullOrEmpty(input.URL)))
        {
            if (string.IsNullOrEmpty(input.Text)) throw new InvalidDataException("Browser request contains no text.");
            settings.Job = WorkflowType.UploadText;
            await manager.StartTextTask(settings, input.Text);
            return;
        }
        string url = input.URL ?? throw new InvalidDataException("Browser request contains no URL.");
        if (input.Action == BrowserExtensionAction.UploadImage && url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            int comma = url.IndexOf(',');
            if (comma < 0 || !url[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid image data URL.");
            var image = DecodeImage(Convert.FromBase64String(url[(comma + 1)..]));
            settings.Job = WorkflowType.PrintScreen;
            await manager.StartTask(settings, image);
            return;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new InvalidDataException("Browser URLs must use HTTP or HTTPS.");
        if (input.Action == BrowserExtensionAction.UploadImage && settings.AdvancedSettings.ProcessImagesDuringExtensionUpload)
        {
            SKBitmap? image = null;
            try
            {
                using var response = await HttpClientFactory.Create().GetAsync(uri, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync();
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[81920];
                int count;
                while ((count = await stream.ReadAsync(chunk)) > 0)
                {
                    if (buffer.Length + count > NativeMessagingHost.MaximumInputBytes) throw new InvalidDataException("Browser image is too large.");
                    buffer.Write(chunk, 0, count);
                }
                image = DecodeImage(buffer.ToArray());
            }
            catch (Exception ex) { DebugHelper.WriteException(ex, "Browser image processing; using download workflow"); }
            if (image != null)
            {
                settings.Job = WorkflowType.PrintScreen;
                await manager.StartTask(settings, image);
                return;
            }
        }
        settings.Job = input.Action == BrowserExtensionAction.ShortenURL ? WorkflowType.ShortenURL : WorkflowType.UploadURL;
        await manager.StartTextTask(settings, url);
    }

    private static SKBitmap DecodeImage(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        if (codec == null || (long)codec.Info.Width * codec.Info.Height > 100_000_000)
            throw new InvalidDataException("Invalid or oversized browser image.");
        return SKBitmap.Decode(bytes) ?? throw new InvalidDataException("Could not decode the browser image.");
    }
}
