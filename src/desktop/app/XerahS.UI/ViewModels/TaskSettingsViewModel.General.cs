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

namespace XerahS.UI.ViewModels
{
    public partial class TaskSettingsViewModel
    {
        #region General (Forwarded from TaskSettingsGeneral)

        public bool PlaySoundAfterCapture
        {
            get => GeneralSource.GeneralSettings.PlaySoundAfterCapture;
            set
            {
                if (GeneralSource.GeneralSettings.PlaySoundAfterCapture != value)
                {
                    GeneralSource.GeneralSettings.PlaySoundAfterCapture = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowToastNotification
        {
            get => GeneralSource.GeneralSettings.ShowToastNotificationAfterTaskCompleted;
            set
            {
                if (GeneralSource.GeneralSettings.ShowToastNotificationAfterTaskCompleted != value)
                {
                    GeneralSource.GeneralSettings.ShowToastNotificationAfterTaskCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool PlaySoundAfterUpload
        {
            get => GeneralSource.GeneralSettings.PlaySoundAfterUpload;
            set
            {
                if (GeneralSource.GeneralSettings.PlaySoundAfterUpload != value)
                {
                    GeneralSource.GeneralSettings.PlaySoundAfterUpload = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool PlaySoundAfterAction
        {
            get => GeneralSource.GeneralSettings.PlaySoundAfterAction;
            set
            {
                if (GeneralSource.GeneralSettings.PlaySoundAfterAction != value)
                {
                    GeneralSource.GeneralSettings.PlaySoundAfterAction = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool UseCustomCaptureSound
        {
            get => GeneralSource.GeneralSettings.UseCustomCaptureSound;
            set
            {
                if (GeneralSource.GeneralSettings.UseCustomCaptureSound != value)
                {
                    GeneralSource.GeneralSettings.UseCustomCaptureSound = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CustomCaptureSoundPath
        {
            get => GeneralSource.GeneralSettings.CustomCaptureSoundPath;
            set
            {
                if (GeneralSource.GeneralSettings.CustomCaptureSoundPath != value)
                {
                    GeneralSource.GeneralSettings.CustomCaptureSoundPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public float ToastWindowDuration
        {
            get => GeneralSource.GeneralSettings.ToastWindowDuration;
            set
            {
                if (Math.Abs(GeneralSource.GeneralSettings.ToastWindowDuration - value) > 0.001f)
                {
                    GeneralSource.GeneralSettings.ToastWindowDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public float ToastWindowFadeDuration
        {
            get => GeneralSource.GeneralSettings.ToastWindowFadeDuration;
            set
            {
                if (Math.Abs(GeneralSource.GeneralSettings.ToastWindowFadeDuration - value) > 0.001f)
                {
                    GeneralSource.GeneralSettings.ToastWindowFadeDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public ContentPlacement ToastWindowPlacement
        {
            get => GeneralSource.GeneralSettings.ToastWindowPlacement;
            set
            {
                if (GeneralSource.GeneralSettings.ToastWindowPlacement != value)
                {
                    GeneralSource.GeneralSettings.ToastWindowPlacement = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ToastWindowWidth
        {
            get => GeneralSource.GeneralSettings.ToastWindowSize.Width;
            set
            {
                if (GeneralSource.GeneralSettings.ToastWindowSize.Width != value)
                {
                    GeneralSource.GeneralSettings.ToastWindowSize = new SizeI(value, GeneralSource.GeneralSettings.ToastWindowSize.Height);
                    OnPropertyChanged();
                }
            }
        }

        public int ToastWindowHeight
        {
            get => GeneralSource.GeneralSettings.ToastWindowSize.Height;
            set
            {
                if (GeneralSource.GeneralSettings.ToastWindowSize.Height != value)
                {
                    GeneralSource.GeneralSettings.ToastWindowSize = new SizeI(GeneralSource.GeneralSettings.ToastWindowSize.Width, value);
                    OnPropertyChanged();
                }
            }
        }

        public ToastClickAction ToastWindowLeftClickAction
        {
            get => GeneralSource.GeneralSettings.ToastWindowLeftClickAction;
            set
            {
                if (GeneralSource.GeneralSettings.ToastWindowLeftClickAction != value)
                {
                    GeneralSource.GeneralSettings.ToastWindowLeftClickAction = value;
                    OnPropertyChanged();
                }
            }
        }

        public ToastClickAction ToastWindowRightClickAction
        {
            get => GeneralSource.GeneralSettings.ToastWindowRightClickAction;
            set
            {
                if (GeneralSource.GeneralSettings.ToastWindowRightClickAction != value)
                {
                    GeneralSource.GeneralSettings.ToastWindowRightClickAction = value;
                    OnPropertyChanged();
                }
            }
        }

        public ToastClickAction ToastWindowMiddleClickAction
        {
            get => GeneralSource.GeneralSettings.ToastWindowMiddleClickAction;
            set
            {
                if (GeneralSource.GeneralSettings.ToastWindowMiddleClickAction != value)
                {
                    GeneralSource.GeneralSettings.ToastWindowMiddleClickAction = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ToastWindowAutoHide
        {
            get => GeneralSource.GeneralSettings.ToastWindowAutoHide;
            set
            {
                if (GeneralSource.GeneralSettings.ToastWindowAutoHide != value)
                {
                    GeneralSource.GeneralSettings.ToastWindowAutoHide = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion
    }
}
