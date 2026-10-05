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

namespace ShareX.Flickr.Plugin;

/// <summary>
/// Flickr settings: ShareX's FlickrSettings, the app's consumer key, and the account name. The consumer secret and the
/// account's OAuth token and token secret are kept in the secret store.
/// </summary>
public sealed class FlickrConfigModel
{
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The Flickr app's key. ShareX's release builds include their own; XerahS users register an app.</summary>
    public string ConsumerKey { get; set; } = string.Empty;

    /// <summary>The authorized account, shown in the settings.</summary>
    public string UserName { get; set; } = string.Empty;

    public bool DirectLink { get; set; } = true;

    /// <summary>The title of the photo.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>A description of the photo. May contain some limited HTML.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>A space-separated list of tags to apply to the photo.</summary>
    public string Tags { get; set; } = string.Empty;

    // As in ShareX, these are sent only when set: "0" for no, "1" for yes.
    public string IsPublic { get; set; } = string.Empty;
    public string IsFriend { get; set; } = string.Empty;
    public string IsFamily { get; set; } = string.Empty;

    /// <summary>1 for Safe, 2 for Moderate, or 3 for Restricted.</summary>
    public string SafetyLevel { get; set; } = string.Empty;

    /// <summary>1 for Photo, 2 for Screenshot, or 3 for Other.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>1 to keep the photo in global search results, 2 to hide it from public searches.</summary>
    public string Hidden { get; set; } = string.Empty;
}
