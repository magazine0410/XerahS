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

namespace ShareX.Box.Plugin;

/// <summary>ShareX's Box settings: the folder, the shared link, and its access level. The client ID, client secret, and OAuth token are kept in the secret store.</summary>
public sealed class BoxConfigModel
{
    public const string DefaultRedirectUri = "http://localhost:52476/oauth2/callback";

    public string SecretKey { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = DefaultRedirectUri;
    /// <summary>The folder's ID; "0" is the root folder.</summary>
    public string FolderID { get; set; } = "0";
    public string FolderName { get; set; } = string.Empty;
    public bool Share { get; set; } = true;
    public BoxShareAccessLevel ShareAccessLevel { get; set; } = BoxShareAccessLevel.Open;
}

public sealed class BoxFolder
{
    public string ID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
