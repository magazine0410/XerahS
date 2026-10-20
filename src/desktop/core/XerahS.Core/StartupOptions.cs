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

namespace XerahS.Core;

/// <summary>
/// ShareX's StartupOptions: command line arguments that apply to the instance they start. A running instance that
/// receives them from a second start ignores them, as in ShareX. Portable mode is <see cref="XerahS.Common.PathsManager.IsPortable"/>,
/// and sandbox mode <see cref="XerahS.Common.PathsManager.IsSandbox"/>.
/// </summary>
public static class StartupOptions
{
    /// <summary><c>-multi</c> or <c>-m</c>: the instance runs beside a running one instead of passing its arguments to it.</summary>
    public static bool MultiInstance { get; set; }

    /// <summary><c>-NoHotkeys</c>: hotkeys that cannot be registered at startup are not reported.</summary>
    public static bool IgnoreHotkeyWarning { get; set; }

    /// <summary><c>-AutoClose</c>: XerahS exits once its tasks are finished.</summary>
    public static bool AutoClose { get; set; }
}
