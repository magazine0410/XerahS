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

namespace ShareX.ImageChest.Plugin;

/// <summary>Image Chest settings. The personal access token is kept in the secret store.</summary>
public sealed class ImageChestConfigModel
{
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The post's title; empty leaves it untitled.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>public, hidden, or secret; Image Chest's default is hidden.</summary>
    public string Privacy { get; set; } = "hidden";

    public bool Nsfw { get; set; }

    public bool Anonymous { get; set; }

    /// <summary>The image's own URL (on by default, as for ShareX's image hosts) instead of the post page.</summary>
    public bool DirectLink { get; set; } = true;
}
