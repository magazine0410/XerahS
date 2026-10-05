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

namespace ShareX.GoogleCloudStorage.Plugin;

/// <summary>ShareX's Google Cloud Storage settings: the bucket, domain, object prefix, extension removal, and public ACL. The client ID, client secret, and OAuth token are kept in the secret store.</summary>
public sealed class GoogleCloudStorageConfigModel
{
    public const string DefaultRedirectUri = "http://127.0.0.1:52476/oauth2/callback";

    public string SecretKey { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = DefaultRedirectUri;
    public string Bucket { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string ObjectPrefix { get; set; } = "ShareX/%y/%mo";
    public bool RemoveExtensionImage { get; set; }
    public bool RemoveExtensionVideo { get; set; }
    public bool RemoveExtensionText { get; set; }
    public bool SetPublicACL { get; set; } = true;
}
