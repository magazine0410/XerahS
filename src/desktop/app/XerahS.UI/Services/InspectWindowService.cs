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

using Avalonia.Media.Imaging;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Services;

internal static class InspectWindowService
{
    public static IReadOnlyList<InspectWindowListItem> GetVisibleWindows(IntPtr ignored, IWindowService? service = null)
    {
        service ??= PlatformServices.Window;
        var result = new List<InspectWindowListItem>();
        foreach (var window in service.GetAllWindows().Where(w => w.Handle != ignored).OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var details = service.GetWindowDetails(window.Handle);
                if (details != null) result.Add(new InspectWindowListItem(window.Handle, window.Title, details.ProcessName, GetWindowIcon(window.Handle, service)));
            }
            catch (Exception ex) { DebugHelper.WriteException(ex, "Unable to inspect a window"); }
        }
        return result;
    }

    public static Bitmap? GetWindowIcon(IntPtr handle, IWindowService? service = null)
    {
        try
        {
            byte[]? bytes = (service ?? PlatformServices.Window).GetWindowIcon(handle);
            if (bytes == null) return null;
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Unable to decode window icon"); return null; }
    }

    public static IntPtr GetWindowAtPoint(int x, int y, bool topLevel) => PlatformServices.Window.GetWindowAtPoint(new System.Drawing.Point(x, y), topLevel);
}
