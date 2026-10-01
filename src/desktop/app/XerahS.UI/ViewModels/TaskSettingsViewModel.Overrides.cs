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

namespace XerahS.UI.ViewModels
{
    /// <summary>
    /// ShareX's per-section "Override ... settings" checkboxes. A section a workflow does not override
    /// runs with the default task settings, and its controls show those defaults, disabled.
    /// </summary>
    public partial class TaskSettingsViewModel
    {
        private TaskSettings DefaultSettings => SettingsManager.DefaultTaskSettings ?? _settings;

        private TaskSettings SourceFor(bool useDefault) => IsDefaultTaskSettings || !useDefault ? _settings : DefaultSettings;

        private TaskSettings AfterCaptureSource => SourceFor(_settings.UseDefaultAfterCaptureJob);
        private TaskSettings AfterUploadSource => SourceFor(_settings.UseDefaultAfterUploadJob);
        private TaskSettings GeneralSource => SourceFor(_settings.UseDefaultGeneralSettings);
        private TaskSettings ImageSource => SourceFor(_settings.UseDefaultImageSettings);
        private TaskSettings CaptureSource => SourceFor(_settings.UseDefaultCaptureSettings);
        private TaskSettings UploadSource => SourceFor(_settings.UseDefaultUploadSettings);
        private TaskSettings AdvancedSource => SourceFor(_settings.UseDefaultAdvancedSettings);

        /// <summary>The override checkboxes are hidden on the default task settings page.</summary>
        public bool ShowOverrideSettings => !IsDefaultTaskSettings;

        public bool OverrideAfterCaptureTasks
        {
            get => !_settings.UseDefaultAfterCaptureJob;
            set => SetOverride(_settings.UseDefaultAfterCaptureJob, value, useDefault => _settings.UseDefaultAfterCaptureJob = useDefault);
        }

        public bool OverrideAfterUploadTasks
        {
            get => !_settings.UseDefaultAfterUploadJob;
            set => SetOverride(_settings.UseDefaultAfterUploadJob, value, useDefault => _settings.UseDefaultAfterUploadJob = useDefault);
        }

        public bool OverrideDestinations
        {
            get => !_settings.UseDefaultDestinations;
            set => SetOverride(_settings.UseDefaultDestinations, value, useDefault => _settings.UseDefaultDestinations = useDefault);
        }

        public bool OverrideGeneralSettings
        {
            get => !_settings.UseDefaultGeneralSettings;
            set => SetOverride(_settings.UseDefaultGeneralSettings, value, useDefault => _settings.UseDefaultGeneralSettings = useDefault);
        }

        public bool OverrideImageSettings
        {
            get => !_settings.UseDefaultImageSettings;
            set
            {
                if (SetOverride(_settings.UseDefaultImageSettings, value, useDefault => _settings.UseDefaultImageSettings = useDefault))
                {
                    // The effects list edits the image settings object, which is now the other one.
                    ImageEffects = new ImageEffectsViewModel(ImageSource.ImageSettings, _effectsEditorCore, _dialogService);
                    ImageEffects.UpdatePreview();
                    OnPropertyChanged(nameof(ImageEffects));
                }
            }
        }

        public bool OverrideCaptureSettings
        {
            get => !_settings.UseDefaultCaptureSettings;
            set => SetOverride(_settings.UseDefaultCaptureSettings, value, useDefault => _settings.UseDefaultCaptureSettings = useDefault);
        }

        public bool OverrideUploadSettings
        {
            get => !_settings.UseDefaultUploadSettings;
            set => SetOverride(_settings.UseDefaultUploadSettings, value, useDefault => _settings.UseDefaultUploadSettings = useDefault);
        }

        public bool OverrideAdvancedSettings
        {
            get => !_settings.UseDefaultAdvancedSettings;
            set => SetOverride(_settings.UseDefaultAdvancedSettings, value, useDefault => _settings.UseDefaultAdvancedSettings = useDefault);
        }

        // Each section can be edited on the default page, or in a workflow that overrides it.
        public bool AfterCaptureTasksEnabled => IsDefaultTaskSettings || OverrideAfterCaptureTasks;
        public bool AfterUploadTasksEnabled => IsDefaultTaskSettings || OverrideAfterUploadTasks;
        public bool DestinationsEnabled => IsDefaultTaskSettings || OverrideDestinations;
        public bool GeneralSettingsEnabled => IsDefaultTaskSettings || OverrideGeneralSettings;
        public bool ImageSettingsEnabled => IsDefaultTaskSettings || OverrideImageSettings;
        public bool CaptureSettingsEnabled => IsDefaultTaskSettings || OverrideCaptureSettings;
        public bool UploadSettingsEnabled => IsDefaultTaskSettings || OverrideUploadSettings;
        public bool AdvancedSettingsEnabled => IsDefaultTaskSettings || OverrideAdvancedSettings;

        private bool SetOverride(bool useDefault, bool overrideValue, Action<bool> setUseDefault)
        {
            if (useDefault != overrideValue)
            {
                return false;
            }

            setUseDefault(!overrideValue);
            // The section's controls now show the other settings object.
            OnPropertyChanged(string.Empty);
            return true;
        }
    }
}
