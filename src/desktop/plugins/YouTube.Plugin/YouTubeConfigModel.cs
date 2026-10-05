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

using XerahS.Uploaders;

namespace ShareX.YouTube.Plugin;

/// <summary>ShareX's YouTube settings: the visibility and the shortened link. The client ID, client secret, and OAuth token are kept in the secret store.</summary>
public sealed class YouTubeConfigModel
{
    public const string DefaultRedirectUri = "http://127.0.0.1:52476/oauth2/callback";

    public string SecretKey { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = DefaultRedirectUri;
    public YouTubeVideoPrivacy PrivacyType { get; set; } = YouTubeVideoPrivacy.Public;
    public bool UseShortenedLink { get; set; }
}
