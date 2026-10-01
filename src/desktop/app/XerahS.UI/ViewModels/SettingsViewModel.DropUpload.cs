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

using XerahS.Core;
using XerahS.Platform.Abstractions;

namespace XerahS.UI.ViewModels;

public partial class SettingsViewModel
{
    public ContentPlacement[] DropAlignments => Enum.GetValues<ContentPlacement>();

    public int DropSize
    {
        get => SettingsManager.Settings.DropSize;
        set
        {
            var normalized = Math.Clamp(value, 10, 300);
            if (SettingsManager.Settings.DropSize == normalized) return;
            SettingsManager.Settings.DropSize = normalized;
            OnPropertyChanged();
            Services.UploadWorkflowService.RefreshDropWindowSettings();
        }
    }

    public int DropOffset
    {
        get => SettingsManager.Settings.DropOffset;
        set
        {
            var normalized = Math.Clamp(value, 0, 1000);
            if (SettingsManager.Settings.DropOffset == normalized) return;
            SettingsManager.Settings.DropOffset = normalized;
            OnPropertyChanged();
            Services.UploadWorkflowService.RefreshDropWindowSettings();
        }
    }

    public int DropOpacity
    {
        get => SettingsManager.Settings.DropOpacity;
        set
        {
            var normalized = Math.Clamp(value, 1, 255);
            if (SettingsManager.Settings.DropOpacity == normalized) return;
            SettingsManager.Settings.DropOpacity = normalized;
            OnPropertyChanged();
            Services.UploadWorkflowService.RefreshDropWindowSettings();
        }
    }

    public int DropHoverOpacity
    {
        get => SettingsManager.Settings.DropHoverOpacity;
        set
        {
            var normalized = Math.Clamp(value, 1, 255);
            if (SettingsManager.Settings.DropHoverOpacity == normalized) return;
            SettingsManager.Settings.DropHoverOpacity = normalized;
            OnPropertyChanged();
            Services.UploadWorkflowService.RefreshDropWindowSettings();
        }
    }

    public ContentPlacement DropAlignment
    {
        get => SettingsManager.Settings.DropAlignment;
        set
        {
            var normalized = value;
            if (SettingsManager.Settings.DropAlignment == normalized) return;
            SettingsManager.Settings.DropAlignment = normalized;
            OnPropertyChanged();
            Services.UploadWorkflowService.RefreshDropWindowSettings();
        }
    }
}
