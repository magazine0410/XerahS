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

using Avalonia.Controls;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

internal static class WindowToolsService
{
    public static Task OpenAsync(WorkflowType job, TaskSettings? taskSettings)
    {
        var service = PlatformServices.Window;
        if (job == WorkflowType.InspectWindow)
        {
            if (!service.SupportsWindowInspection) throw new PlatformNotSupportedException("Window inspection is not supported on this window system.");
            new InspectWindowWindow().Show();
        }
        else
        {
            if (!service.SupportsBorderless) throw new PlatformNotSupportedException("Borderless windows are not supported on this window system.");
            var settings = (taskSettings ?? SettingsManager.DefaultTaskSettings).ToolsSettingsReference.BorderlessWindowSettings;
            new BorderlessWindowWindow(settings, (title, workingArea) => service.ToggleBorderlessWindow(service.SearchWindow(title), workingArea),
                settings => _ = ImageEditorOptionsStore.PersistAsync()).Show();
        }
        return Task.CompletedTask;
    }
}
