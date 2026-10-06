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
using XerahS.Core;

namespace XerahS.UI.ViewModels;

public partial class SettingsViewModel
{
    public bool SupportsCustomBrowser => HelpersOptions.SupportsCustomBrowser;

    public bool AutoCleanupBackupFiles
    {
        get => SettingsManager.Settings.AutoCleanupBackupFiles;
        set
        {
            SettingsManager.Settings.AutoCleanupBackupFiles = value;
            OnPropertyChanged();
        }
    }

    public bool AutoCleanupLogFiles
    {
        get => SettingsManager.Settings.AutoCleanupLogFiles;
        set
        {
            SettingsManager.Settings.AutoCleanupLogFiles = value;
            OnPropertyChanged();
        }
    }

    public int CleanupKeepFileCount
    {
        get => SettingsManager.Settings.CleanupKeepFileCount;
        set
        {
            SettingsManager.Settings.CleanupKeepFileCount = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public bool SaveSettingsAfterTaskCompleted
    {
        get => SettingsManager.Settings.SaveSettingsAfterTaskCompleted;
        set
        {
            SettingsManager.Settings.SaveSettingsAfterTaskCompleted = value;
            OnPropertyChanged();
        }
    }

    public string BrowserPath
    {
        get => SettingsManager.Settings.BrowserPath;
        set
        {
            SettingsManager.Settings.BrowserPath = value ?? string.Empty;
            HelpersOptions.BrowserPath = SettingsManager.Settings.BrowserPath;
            OnPropertyChanged();
        }
    }

    public string CustomScreenshotsPath2
    {
        get => SettingsManager.Settings.CustomScreenshotsPath2;
        set
        {
            SettingsManager.Settings.CustomScreenshotsPath2 = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

}
