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

namespace XerahS.Uploaders.PluginSystem;

public static class UploaderUploadAdapter
{
    public static async Task<UploadOutcome> UploadAsync(object instance, UploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(request);

        using var scope = new UploadCancellationScope(cancellationToken);
        cancellationToken = scope.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (instance is Uploader legacyUploader) legacyUploader.RequestCancellationToken = cancellationToken;
        using var registration = instance is Uploader uploader
            ? cancellationToken.Register(uploader.StopUpload)
            : default;

        UploadOutcome outcome;

        if (instance is IUploadHandler handler)
        {
            outcome = await handler.UploadAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else if (instance is GenericUploader generic)
        {
            outcome = await UploadLegacyAsync(generic, request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return UploadOutcome.Failed("Uploader type not supported.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return outcome;
    }

    public static async Task<UploadResult> ShortenAsync(object? instance, string url, CancellationToken cancellationToken)
    {
        using var scope = new UploadCancellationScope(cancellationToken);
        cancellationToken = scope.Token;
        if (instance is Uploader uploader) uploader.RequestCancellationToken = cancellationToken;
        using var registration = instance is Uploader legacy ? cancellationToken.Register(legacy.StopUpload) : default;
        var result = await Task.Run(() => instance switch
        {
            UrlShortener shortener => shortener.ShortenURL(url),
            CustomUploader.CustomUploaderExecutor custom => custom.ShortenUrl(url),
            _ => new UploadResult { Response = "The selected destination does not support URL shortening." }
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (instance is Uploader source && source.Errors.Count > 0 && !ReferenceEquals(source.Errors, result.Errors))
        {
            result.Errors.Add(source.Errors);
        }
        return result;
    }

    private static async Task<UploadOutcome> UploadLegacyAsync(
        GenericUploader uploader,
        UploadRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Uploader.ProgressEventHandler? progressHandler = null;
        if (request.Progress != null)
        {
            long lastPosition = 0;
            progressHandler = progress =>
            {
                long delta = Math.Max(0, progress.Position - lastPosition);
                lastPosition = progress.Position;
                request.Progress.Report(new UploadProgressReport(delta, progress.Length));
            };
            uploader.ProgressChanged += progressHandler;
        }

        try
        {
            UploadResult result = await Task.Run(
                () => uploader.Upload(request.Content, request.FileName),
                cancellationToken).ConfigureAwait(false);
            return UploadOutcomeMapper.FromUploadResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UploadOutcome.Failed(ex.Message);
        }
        finally
        {
            if (progressHandler != null)
            {
                uploader.ProgressChanged -= progressHandler;
            }
        }
    }
}
