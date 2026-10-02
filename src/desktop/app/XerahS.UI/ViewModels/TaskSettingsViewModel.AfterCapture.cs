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
using CommunityToolkit.Mvvm.Input;

namespace XerahS.UI.ViewModels
{
    public partial class TaskSettingsViewModel
    {
        #region After Capture Tasks

        public bool SaveImageToFile
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveImageToFile);
            set
            {
                if (SaveImageToFile != value)
                {
                    UpdateAfterCaptureTask(AfterCaptureTasks.SaveImageToFile, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool CopyImageToClipboard
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyImageToClipboard);
            set
            {
                if (CopyImageToClipboard != value)
                {
                    UpdateAfterCaptureTask(AfterCaptureTasks.CopyImageToClipboard, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool UploadImageToHost
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.UploadImageToHost);
            set
            {
                if (UploadImageToHost != value)
                {
                    UpdateAfterCaptureTask(AfterCaptureTasks.UploadImageToHost, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool AnnotateMedia
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.AnnotateMedia);
            set
            {
                if (AnnotateMedia != value)
                {
                    UpdateAfterCaptureTask(AfterCaptureTasks.AnnotateMedia, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool ApplyImageEffects
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.AddImageEffects);
            set
            {
                if (ApplyImageEffects != value)
                {
                    UpdateAfterCaptureTask(AfterCaptureTasks.AddImageEffects, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowAfterCaptureWindow
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowAfterCaptureWindow);
            set
            {
                if (ShowAfterCaptureWindow != value)
                {
                    UpdateAfterCaptureTask(AfterCaptureTasks.ShowAfterCaptureWindow, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool DoOCR
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.DoOCR);
            set
            {
                if (DoOCR != value)
                {
                    UpdateAfterCaptureTask(AfterCaptureTasks.DoOCR, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool CopyOcrTextToClipboard
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyOcrTextToClipboard);
            set
            {
                if (CopyOcrTextToClipboard != value)
                {
                    UpdateAfterCaptureTask(AfterCaptureTasks.CopyOcrTextToClipboard, value);
                    OnPropertyChanged();
                }
            }
        }


        public bool ShowQuickTaskMenu
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowQuickTaskMenu);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.ShowQuickTaskMenu, value); OnPropertyChanged(); }
        }

        public bool BeautifyImage
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.BeautifyImage);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.BeautifyImage, value); OnPropertyChanged(); }
        }

        public bool SaveImageToFileWithDialog
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveImageToFileWithDialog);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.SaveImageToFileWithDialog, value); OnPropertyChanged(); }
        }

        public bool SaveThumbnailImageToFile
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveThumbnailImageToFile);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.SaveThumbnailImageToFile, value); OnPropertyChanged(); }
        }

        public bool PerformActions
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.PerformActions);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.PerformActions, value); OnPropertyChanged(); }
        }

        public bool CopyFolderPathToClipboard
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFolderPathToClipboard);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.CopyFolderPathToClipboard, value); OnPropertyChanged(); }
        }

        public bool ShowBeforeUploadWindow
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowBeforeUploadWindow);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.ShowBeforeUploadWindow, value); OnPropertyChanged(); }
        }

        public bool DeleteFile
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.DeleteFile);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.DeleteFile, value); OnPropertyChanged(); }
        }

        public bool CopyFileToClipboard
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFileToClipboard);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.CopyFileToClipboard, value); OnPropertyChanged(); }
        }

        public bool CopyFilePathToClipboard
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFilePathToClipboard);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.CopyFilePathToClipboard, value); OnPropertyChanged(); }
        }

        public bool ShowInExplorer
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowInExplorer);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.ShowInExplorer, value); OnPropertyChanged(); }
        }

        public bool PinToScreen
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.PinToScreen);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.PinToScreen, value); OnPropertyChanged(); }
        }

        public bool ScanQRCode
        {
            get => AfterCaptureSource.AfterCaptureJob.HasFlag(AfterCaptureTasks.ScanQRCode);
            set { UpdateAfterCaptureTask(AfterCaptureTasks.ScanQRCode, value); OnPropertyChanged(); }
        }

        [RelayCommand]
        private void EditQuickTaskMenu() => new Views.QuickTaskMenuEditorWindow().Show();

        private void UpdateAfterCaptureTask(AfterCaptureTasks task, bool enabled)
        {
            if (enabled)
                AfterCaptureSource.AfterCaptureJob |= task;
            else
                AfterCaptureSource.AfterCaptureJob &= ~task;
        }

        #endregion
    }
}
