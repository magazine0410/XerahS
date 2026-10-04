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

namespace ShareX.ImageShack.Plugin;

/// <summary>
/// ImageShack settings, as ShareX's ImageShackOptions. The API key, password, and auth token are kept in the secret store.
/// </summary>
public sealed class ImageShackConfigModel
{
    public string SecretKey { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public bool IsPublic { get; set; }

    public int ThumbnailWidth { get; set; } = 256;

    public int ThumbnailHeight { get; set; }

    /// <summary>A plaintext password from a ShareX configuration import; moved into the secret store.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Password { get; set; }

    /// <summary>A plaintext auth token from a ShareX configuration import; moved into the secret store.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? AuthToken { get; set; }
}
