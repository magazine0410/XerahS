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

using System.Net;
using System.Text;

namespace XerahS.Uploaders
{
    public sealed class OAuthCallbackResult
    {
        public bool IsSuccess { get; init; }
        public string? Code { get; init; }
        public string? Error { get; init; }
        public string? ErrorDescription { get; init; }
    }

    /// <summary>
    /// Receives an OAuth 2.0 authorization code on a local redirect URI (http://127.0.0.1 or http://localhost),
    /// as Dropbox's listener does, for the service named in <c>serviceName</c>.
    /// </summary>
    public sealed class OAuthLoopbackListener : IDisposable
    {
        private readonly Uri _redirectUri;
        private readonly string _serviceName;
        private readonly HttpListener _listener;

        public OAuthLoopbackListener(Uri redirectUri, string serviceName)
        {
            _redirectUri = redirectUri ?? throw new ArgumentNullException(nameof(redirectUri));
            _serviceName = serviceName;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"{redirectUri.Scheme}://{redirectUri.Host}:{redirectUri.Port}/");
        }

        /// <summary>Returns an error message when the URI cannot be used as a local redirect URI.</summary>
        public static string? ValidateRedirectUri(string? value, out Uri? redirectUri)
        {
            redirectUri = null;
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttp)
            {
                return "The redirect URI must be an http:// address, such as http://127.0.0.1:52476/oauth2/callback.";
            }

            if (!uri.IsLoopback)
            {
                return "The redirect URI must point to this computer (127.0.0.1 or localhost).";
            }

            if (uri.IsDefaultPort)
            {
                return "The redirect URI needs a port, such as http://127.0.0.1:52476/oauth2/callback.";
            }

            redirectUri = uri;
            return null;
        }

        public void Start() => _listener.Start();

        public async Task<OAuthCallbackResult> WaitForCallbackAsync(string expectedState, CancellationToken cancellation)
        {
            using var registration = cancellation.Register(static state =>
            {
                try { ((HttpListener)state!).Stop(); }
                catch { }
            }, _listener);

            while (!cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException && cancellation.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellation);
                }

                if (!IsExpectedPath(context.Request.Url))
                {
                    await WriteResponseAsync(context.Response, false, $"{_serviceName} login is in progress. You can close this tab.", cancellation).ConfigureAwait(false);
                    continue;
                }

                OAuthCallbackResult result = ReadCallback(context.Request.QueryString["code"], context.Request.QueryString["state"],
                    context.Request.QueryString["error"], context.Request.QueryString["error_description"], expectedState);
                string message = result.IsSuccess
                    ? $"{_serviceName} login completed. You can close this tab and return to XerahS."
                    : $"{_serviceName} authorization failed: {result.ErrorDescription ?? result.Error}";
                await WriteResponseAsync(context.Response, !result.IsSuccess, message, cancellation).ConfigureAwait(false);
                return result;
            }

            throw new OperationCanceledException(cancellation);
        }

        internal static OAuthCallbackResult ReadCallback(string? code, string? state, string? error, string? errorDescription, string expectedState)
        {
            if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                return new OAuthCallbackResult { Error = "state_mismatch", ErrorDescription = "The login state did not match. Try again." };
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                return new OAuthCallbackResult { Error = error, ErrorDescription = string.IsNullOrWhiteSpace(errorDescription) ? error : errorDescription };
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return new OAuthCallbackResult { Error = "missing_code", ErrorDescription = "The authorization code is missing." };
            }

            return new OAuthCallbackResult { IsSuccess = true, Code = code };
        }

        public void Dispose()
        {
            if (_listener.IsListening)
            {
                try { _listener.Stop(); }
                catch { }
            }

            _listener.Close();
        }

        private bool IsExpectedPath(Uri? requestUri) =>
            requestUri != null && string.Equals(NormalizePath(_redirectUri.AbsolutePath), NormalizePath(requestUri.AbsolutePath), StringComparison.OrdinalIgnoreCase);

        private static string NormalizePath(string value) => "/" + (value ?? string.Empty).Trim('/');

        private async Task WriteResponseAsync(HttpListenerResponse response, bool isError, string message, CancellationToken cancellation)
        {
            string title = WebUtility.HtmlEncode(isError ? $"{_serviceName} Login Failed" : $"{_serviceName} Login Complete");
            string color = isError ? "#9f1239" : "#166534";
            string html = $@"<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""utf-8""/><title>{title}</title><meta name=""viewport"" content=""width=device-width, initial-scale=1""/>
<style>body {{ font-family: sans-serif; background: #f8fafc; color: #0f172a; margin: 0; }}
main {{ max-width: 560px; margin: 10vh auto; background: #fff; padding: 24px; border-radius: 12px; border: 1px solid #e2e8f0; }}
h1 {{ margin: 0 0 12px; font-size: 22px; color: {color}; }} p {{ margin: 0; line-height: 1.5; }}</style></head>
<body><main><h1>{title}</h1><p>{WebUtility.HtmlEncode(message)}</p></main></body>
</html>";

            byte[] bytes = Encoding.UTF8.GetBytes(html);
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            response.KeepAlive = false;
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = bytes.LongLength;
            await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
            response.Close();
        }
    }
}
