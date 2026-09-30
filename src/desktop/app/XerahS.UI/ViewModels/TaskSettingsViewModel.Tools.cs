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

using ShareX.ImageEditor.Hosting;
using XerahS.Core;

namespace XerahS.UI.ViewModels
{
    public partial class TaskSettingsViewModel
    {
        /// <summary>
        /// True when this view model edits the default task settings (Settings > Task Settings)
        /// rather than one workflow's settings.
        /// </summary>
        public bool IsDefaultTaskSettings { get; init; }

        private TaskSettingsTools OwnTools => _settings.ToolsSettings ??= new TaskSettingsTools();

        /// <summary>
        /// The tool settings the controls show: the workflow's own when it overrides them, otherwise the
        /// defaults it runs with. The controls are disabled while they show the defaults.
        /// </summary>
        private TaskSettingsTools Tools => ToolsSettingsEnabled
            ? OwnTools
            : SettingsManager.DefaultTaskSettings?.ToolsSettings ?? OwnTools;

        /// <summary>
        /// ShareX's "Override tools settings": off means the workflow uses the default tool settings.
        /// </summary>
        public bool OverrideToolsSettings
        {
            get => !_settings.UseDefaultToolsSettings;
            set
            {
                if (_settings.UseDefaultToolsSettings == value)
                {
                    _settings.UseDefaultToolsSettings = !value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ToolsSettingsEnabled));
                    // The tool controls now show the other settings object.
                    OnPropertyChanged(string.Empty);
                }
            }
        }

        public bool ShowOverrideToolsSettings => !IsDefaultTaskSettings;

        /// <summary>
        /// Tool settings can be edited for the defaults, or for a workflow that overrides them.
        /// </summary>
        public bool ToolsSettingsEnabled => IsDefaultTaskSettings || OverrideToolsSettings;

        public bool ShowIndexFolderTab => IsIndexFolderJob || IsDefaultTaskSettings;

        /// <summary>
        /// The workflow editor shows image and video settings in its own tabs; the default page shows them here.
        /// </summary>
        public bool ShowMediaSettingsTabs => IsDefaultTaskSettings;

        public ImageEditorOptions EditorOptions => Tools.ImageEditorOptions ??= new ImageEditorOptions();

        public string ScreenColorPickerFormat
        {
            get => Tools.ScreenColorPickerFormat;
            set
            {
                if (Tools.ScreenColorPickerFormat != value)
                {
                    Tools.ScreenColorPickerFormat = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ScreenColorPickerFormatCtrl
        {
            get => Tools.ScreenColorPickerFormatCtrl;
            set
            {
                if (Tools.ScreenColorPickerFormatCtrl != value)
                {
                    Tools.ScreenColorPickerFormatCtrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ScreenColorPickerInfoText
        {
            get => Tools.ScreenColorPickerInfoText;
            set
            {
                if (Tools.ScreenColorPickerInfoText != value)
                {
                    Tools.ScreenColorPickerInfoText = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
