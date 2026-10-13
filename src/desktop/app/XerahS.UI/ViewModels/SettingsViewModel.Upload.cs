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

namespace XerahS.UI.ViewModels
{
    // ShareX's Upload settings page.
    public partial class SettingsViewModel
    {
        /// <summary>"Simultaneous upload limit"; 0 disables the limit. ShareX allows 0 to 25.</summary>
        public int UploadLimit
        {
            get => SettingsManager.Settings.UploadLimit;
            set
            {
                SettingsManager.Settings.UploadLimit = Math.Clamp(value, 0, 25);
                OnPropertyChanged();
            }
        }

        /// <summary>ShareX's buffer sizes, 1 KiB to 8 MiB, named in the units chosen with "Use binary units".</summary>
        public string[] BufferSizeOptions => Enumerable.Range(0, TaskHelpers.MaxBufferSizePower + 1)
            .Select(power => ((long)TaskHelpers.GetUploadBufferSize(power)).ToSizeString(SettingsManager.Settings.BinaryUnits, 0))
            .ToArray();

        public int BufferSizePower
        {
            get => Math.Clamp(SettingsManager.Settings.BufferSizePower, 0, TaskHelpers.MaxBufferSizePower);
            set
            {
                if (value < 0) return;
                SettingsManager.Settings.BufferSizePower = Math.Clamp(value, 0, TaskHelpers.MaxBufferSizePower);
                OnPropertyChanged();
            }
        }

        /// <summary>"Number of times to retry if upload fails"; ShareX allows 0 to 5.</summary>
        public int MaxUploadFailRetry
        {
            get => SettingsManager.Settings.MaxUploadFailRetry;
            set
            {
                SettingsManager.Settings.MaxUploadFailRetry = Math.Clamp(value, 0, 5);
                OnPropertyChanged();
            }
        }
    }
}
