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

namespace ShareX.Chevereto.Plugin;

/// <summary>Chevereto settings, as in ShareX: the server's upload URL, the API key (secret store), and direct URLs.</summary>
public sealed class CheveretoConfigModel
{
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The server's API upload address, for example https://example.com/api/1/upload.</summary>
    public string UploadURL { get; set; } = string.Empty;

    /// <summary>As in ShareX (CheveretoDirectURL, on by default): the image's own URL instead of its viewer page.</summary>
    public bool DirectURL { get; set; } = true;

    /// <summary>A plaintext API key from a ShareX configuration import; moved into the secret store.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? APIKey { get; set; }
}
