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

using System.Drawing;

namespace XerahS.Platform.Abstractions;

/// <summary>A snapshot of platform-neutral details; absent styles indicate an unsupported platform.</summary>
public sealed class WindowDetails
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public uint ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string ProcessFileName { get; init; } = string.Empty;
    public Rectangle Bounds { get; init; }
    public Rectangle ClientBounds { get; init; }
    public string? Styles { get; init; }
    public string? ExtendedStyles { get; init; }
    public bool IsTopmost { get; init; }
    public byte Opacity { get; init; } = 255;
}
