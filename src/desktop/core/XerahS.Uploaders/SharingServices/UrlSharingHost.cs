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

using XerahS.Common;

namespace XerahS.Uploaders.SharingServices;

/// <summary>An email to send, as shown in and returned by the compose window.</summary>
public sealed record EmailMessageDraft(string ToEmail, string Subject, string Body);

/// <summary>
/// What URL sharing services need from the host application. The desktop app sets these at
/// startup; without it, URLs open through the system and the email compose window is unavailable.
/// </summary>
public static class UrlSharingHost
{
    /// <summary>Opens a URL in the default browser. Returns false when it could not be opened.</summary>
    public static Func<string, bool> OpenUrl { get; set; } = url =>
    {
        URLHelpers.OpenURL(url);
        return true;
    };

    /// <summary>
    /// ShareX's email window: shows the draft for editing and returns the email to send, or null
    /// when the window is cancelled.
    /// </summary>
    public static Func<EmailMessageDraft, CancellationToken, Task<EmailMessageDraft?>>? ComposeEmailAsync { get; set; }
}
