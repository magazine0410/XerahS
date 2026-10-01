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

using System.Net.Http;
using XerahS.Common;
using XerahS.Core.Helpers;
using XerahS.Platform.Abstractions;
using XerahS.Uploaders;

namespace XerahS.Core.Tasks.Processors;

/// <summary>ShareX's download-and-upload job: retain the download in the screenshots folder.</summary>
internal sealed class DownloadJobProcessor(HttpClient? client = null)
{
    private readonly HttpClient _client = client ?? HttpClientFactory.Create(allowAutoRedirect: true, infiniteTimeout: true);

    internal async Task<bool> ProcessAsync(TaskInfo info, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(info.TextContent?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Enter a valid HTTP or HTTPS URL.");

        using var scope = new UploadCancellationScope(cancellationToken);
        var token = scope.Token;
        info.Status = "Downloading...";
        using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string fileName = GetFileName(uri, response, info.TaskSettings);
        string directory = TaskHelpers.GetScreenshotsFolder(info.TaskSettings, info.Metadata);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        bool overwrite = info.TaskSettings.ImageSettings.FileExistAction == FileExistAction.Overwrite;
        if (File.Exists(path) && info.TaskSettings.ImageSettings.FileExistAction == FileExistAction.Ask)
        {
            var resolution = PlatformServices.IsUIServiceInitialized
                ? await PlatformServices.UI.ResolveFileConflictAsync(path, token).ConfigureAwait(false) : null;
            token.ThrowIfCancellationRequested();
            if (resolution == null) return false;
            path = resolution.FilePath;
            overwrite = resolution.Overwrite;
        }
        else
        {
            path = TaskHelpers.HandleExistsFile(path, info.TaskSettings);
            if (string.IsNullOrEmpty(path)) return false;
        }

        // Only replace a destination after a complete download. Failure or Stop all uploads
        // must not destroy an existing file or leave a truncated file ready for upload.
        string partial = Path.Combine(Path.GetDirectoryName(path)!, $".xerahs-download-{Guid.NewGuid():N}.partial");
        try
        {
            long? expected = response.Content.Headers.ContentLength;
            long received = 0;
            var progress = new ProgressManager(expected is > 0 ? expected.Value : 1);
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    received += read;
                    if (progress.UpdateProgress(read)) info.ReportUploadProgress(progress);
                }
            }
            token.ThrowIfCancellationRequested();
            if (expected.HasValue && received != expected.Value)
                throw new IOException("The download ended before the entire file was received.");
            File.Move(partial, path, overwrite);
            info.FilePath = path;
            info.TextContent = null;
            info.DataType = FileHelpers.CheckExtension(path, info.TaskSettings.AdvancedSettings.ImageExtensions) ? EDataType.Image
                : FileHelpers.CheckExtension(path, info.TaskSettings.AdvancedSettings.TextExtensions) ? EDataType.Text : EDataType.File;
            info.Status = string.Empty;
            return true;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    internal static string GetFileName(Uri uri, HttpResponseMessage response, TaskSettings settings)
    {
        string name = Uri.UnescapeDataString(uri.AbsolutePath);
        if (!settings.UploadSettings.FileUploadUseNamePattern)
        {
            var disposition = response.Content.Headers.ContentDisposition;
            string? serverName = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
            if (!string.IsNullOrWhiteSpace(serverName)) name = serverName;
        }
        // Treat either separator as a path separator, including server filenames received on Linux.
        name = FileHelpers.SanitizeFileName(Path.GetFileName(name.Replace('\\', '/'))).TrimEnd('.', ' ');
        if (settings.UploadSettings.FileUploadUseNamePattern)
            return TaskHelpers.GetFileName(settings, FileHelpers.GetFileNameExtension(name));
        if (string.IsNullOrWhiteSpace(name)) throw new IOException("The URL and server response did not provide a file name.");
        return name;
    }
}
