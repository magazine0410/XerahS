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

using System.Collections.Specialized;
using Newtonsoft.Json;
using XerahS.Common;

namespace XerahS.Uploaders
{
    /// <summary>
    /// An OAuth 2.0 authorization code client for services where the user registers their own app (client ID and
    /// secret) with a local redirect URI: the authorization URL, the browser login, the code exchange, and token refresh,
    /// as ShareX's GoogleOAuth2, OneDrive, and Box classes do.
    /// </summary>
    public sealed class OAuth2Client
    {
        public required string ServiceName { get; init; }
        public required string AuthorizationEndpoint { get; init; }
        public required string TokenEndpoint { get; init; }
        public string Scope { get; init; } = string.Empty;
        public bool UsePkce { get; init; }

        /// <summary>Additional authorization arguments, such as Google's access_type=offline.</summary>
        public Dictionary<string, string> AuthorizationArguments { get; init; } = new();

        public string GetAuthorizationURL(string clientId, string redirectUri, string state, OAuth2ProofKey? proofKey)
        {
            var args = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["state"] = state
            };

            if (!string.IsNullOrEmpty(Scope)) args["scope"] = Scope;
            if (proofKey != null)
            {
                args["code_challenge"] = proofKey.CodeChallenge;
                args["code_challenge_method"] = proofKey.ChallengeMethod;
            }

            foreach (var pair in AuthorizationArguments) args[pair.Key] = pair.Value;
            return URLHelpers.CreateQueryString(AuthorizationEndpoint, args);
        }

        /// <summary>
        /// Opens the authorization page, waits for the code on the redirect URI (5 minutes at most), and exchanges it.
        /// Returns the token, or throws with the reason.
        /// </summary>
        public async Task<OAuth2Token> LoginWithBrowserAsync(Uploader uploader, OAuth2Info authInfo, Uri redirectUri,
            Action<string> openUrl, CancellationToken cancellation)
        {
            string state = Guid.NewGuid().ToString("N");
            OAuth2ProofKey? proofKey = UsePkce ? new OAuth2ProofKey(OAuth2ChallengeMethod.SHA256) : null;

            OAuthCallbackResult callback;
            using (var listener = new OAuthLoopbackListener(redirectUri, ServiceName))
            {
                listener.Start();
                openUrl(GetAuthorizationURL(authInfo.Client_ID, redirectUri.AbsoluteUri, state, proofKey));
                callback = await listener.WaitForCallbackAsync(state, cancellation).ConfigureAwait(false);
            }

            if (!callback.IsSuccess)
            {
                throw new InvalidOperationException($"{ServiceName} authorization failed: {callback.ErrorDescription ?? callback.Error}");
            }

            OAuth2Token? token = await Task.Run(() => ExchangeCode(uploader, authInfo, callback.Code!, redirectUri.AbsoluteUri, proofKey?.CodeVerifier),
                cancellation).ConfigureAwait(false);
            return token ?? throw new InvalidOperationException(
                $"{ServiceName} did not return an access token. Check the client ID, client secret, and that the redirect URI is registered in your app."
                + FormatErrors(uploader));
        }

        public OAuth2Token? ExchangeCode(Uploader uploader, OAuth2Info authInfo, string code, string redirectUri, string? codeVerifier)
        {
            var args = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = authInfo.Client_ID,
                ["redirect_uri"] = redirectUri
            };

            if (!string.IsNullOrEmpty(authInfo.Client_Secret)) args["client_secret"] = authInfo.Client_Secret;
            if (!string.IsNullOrEmpty(codeVerifier)) args["code_verifier"] = codeVerifier;
            return ParseToken(uploader.SendRequestURLEncoded(HttpMethod.POST, TokenEndpoint, args), null);
        }

        /// <summary>Refreshes the access token. As in ShareX, the old refresh token is kept when the service sends none.</summary>
        public OAuth2Token? Refresh(Uploader uploader, OAuth2Info authInfo)
        {
            if (authInfo.Token == null || string.IsNullOrEmpty(authInfo.Token.refresh_token)) return null;

            var args = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = authInfo.Token.refresh_token,
                ["client_id"] = authInfo.Client_ID
            };

            if (!string.IsNullOrEmpty(authInfo.Client_Secret)) args["client_secret"] = authInfo.Client_Secret;
            return ParseToken(uploader.SendRequestURLEncoded(HttpMethod.POST, TokenEndpoint, args), authInfo.Token.refresh_token);
        }

        /// <summary>
        /// ShareX's CheckAuthorization: a login is required, and an expired token is refreshed and passed to
        /// <paramref name="tokenRefreshed"/> so that it can be saved. Adds the error and returns false otherwise.
        /// </summary>
        public bool CheckAuthorization(Uploader uploader, OAuth2Info authInfo, Action<OAuth2Token>? tokenRefreshed)
        {
            if (authInfo.Token == null || string.IsNullOrEmpty(authInfo.Token.access_token))
            {
                uploader.Errors.Add($"{ServiceName} login is required. Authorize your account in the destination settings.");
                return false;
            }

            if (!authInfo.Token.IsExpired) return true;

            OAuth2Token? token = Refresh(uploader, authInfo);
            if (token == null)
            {
                uploader.Errors.Add($"Refreshing the {ServiceName} access token failed. Authorize your account again in the destination settings.");
                return false;
            }

            authInfo.Token = token;
            tokenRefreshed?.Invoke(token);
            return true;
        }

        public static NameValueCollection GetAuthHeaders(OAuth2Info authInfo) => new() { ["Authorization"] = "Bearer " + authInfo.Token.access_token };

        internal static OAuth2Token? ParseToken(string? response, string? previousRefreshToken)
        {
            if (string.IsNullOrEmpty(response)) return null;

            OAuth2Token? token;
            try
            {
                token = JsonConvert.DeserializeObject<OAuth2Token>(response);
            }
            catch (JsonException)
            {
                return null;
            }

            if (token == null || string.IsNullOrEmpty(token.access_token)) return null;
            if (string.IsNullOrEmpty(token.refresh_token) && !string.IsNullOrEmpty(previousRefreshToken)) token.refresh_token = previousRefreshToken;
            token.UpdateExpireDate();
            return token;
        }

        private static string FormatErrors(Uploader uploader) =>
            uploader.Errors.Count == 0 ? string.Empty : Environment.NewLine + uploader.Errors.Errors[^1].Text;
    }
}
