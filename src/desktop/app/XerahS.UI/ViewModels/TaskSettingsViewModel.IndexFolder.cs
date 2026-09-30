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
using System.Threading.Tasks;
using XerahS.UI.Services;
using CommunityToolkit.Mvvm.Input;
using XerahS.Core;
using XerahS.Indexer;

namespace XerahS.UI.ViewModels
{
    public partial class TaskSettingsViewModel
    {
        #region Index Folder Commands

        [RelayCommand]
        private async Task BrowseIndexerFolderAsync()
        {
            var folderPath = await _dialogService.ShowFolderPickerAsync("Select Folder to Index");
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                IndexerFolderPath = folderPath;
            }
        }

        [RelayCommand]
        private async Task BrowseIndexerCssFileAsync()
        {
            var filePath = await _dialogService.ShowFilePickerAsync("Select Custom CSS File", new[] { "*.css", "*.*" });
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                IndexerCustomCssFilePath = filePath;
            }
        }

        #endregion

        #region Index Folder Settings

        public string IndexerFolderPath
        {
            get => Tools.IndexerFolderPath;
            set
            {
                if (Tools.IndexerFolderPath != value)
                {
                    Tools.IndexerFolderPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public IndexerOutput IndexerOutput
        {
            get => Tools.IndexerSettings.Output;
            set
            {
                if (Tools.IndexerSettings.Output != value)
                {
                    Tools.IndexerSettings.Output = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerSkipHiddenFolders
        {
            get => Tools.IndexerSettings.SkipHiddenFolders;
            set
            {
                if (Tools.IndexerSettings.SkipHiddenFolders != value)
                {
                    Tools.IndexerSettings.SkipHiddenFolders = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerSkipHiddenFiles
        {
            get => Tools.IndexerSettings.SkipHiddenFiles;
            set
            {
                if (Tools.IndexerSettings.SkipHiddenFiles != value)
                {
                    Tools.IndexerSettings.SkipHiddenFiles = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerSkipFiles
        {
            get => Tools.IndexerSettings.SkipFiles;
            set
            {
                if (Tools.IndexerSettings.SkipFiles != value)
                {
                    Tools.IndexerSettings.SkipFiles = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerIgnoreEmptyFolders
        {
            get => Tools.IndexerSettings.IgnoreEmptyFolders;
            set
            {
                if (Tools.IndexerSettings.IgnoreEmptyFolders != value)
                {
                    Tools.IndexerSettings.IgnoreEmptyFolders = value;
                    OnPropertyChanged();
                }
            }
        }

        public int IndexerMaxDepthLevel
        {
            get => Tools.IndexerSettings.MaxDepthLevel;
            set
            {
                if (Tools.IndexerSettings.MaxDepthLevel != value)
                {
                    Tools.IndexerSettings.MaxDepthLevel = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerShowSizeInfo
        {
            get => Tools.IndexerSettings.ShowSizeInfo;
            set
            {
                if (Tools.IndexerSettings.ShowSizeInfo != value)
                {
                    Tools.IndexerSettings.ShowSizeInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerAddFooter
        {
            get => Tools.IndexerSettings.AddFooter;
            set
            {
                if (Tools.IndexerSettings.AddFooter != value)
                {
                    Tools.IndexerSettings.AddFooter = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IndexerIndentationText
        {
            get => Tools.IndexerSettings.IndentationText;
            set
            {
                if (Tools.IndexerSettings.IndentationText != value)
                {
                    Tools.IndexerSettings.IndentationText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerAddEmptyLineAfterFolders
        {
            get => Tools.IndexerSettings.AddEmptyLineAfterFolders;
            set
            {
                if (Tools.IndexerSettings.AddEmptyLineAfterFolders != value)
                {
                    Tools.IndexerSettings.AddEmptyLineAfterFolders = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerUseCustomCssFile
        {
            get => Tools.IndexerSettings.UseCustomCSSFile;
            set
            {
                if (Tools.IndexerSettings.UseCustomCSSFile != value)
                {
                    Tools.IndexerSettings.UseCustomCSSFile = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerDisplayPath
        {
            get => Tools.IndexerSettings.DisplayPath;
            set
            {
                if (Tools.IndexerSettings.DisplayPath != value)
                {
                    Tools.IndexerSettings.DisplayPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerDisplayPathLimited
        {
            get => Tools.IndexerSettings.DisplayPathLimited;
            set
            {
                if (Tools.IndexerSettings.DisplayPathLimited != value)
                {
                    Tools.IndexerSettings.DisplayPathLimited = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IndexerCustomCssFilePath
        {
            get => Tools.IndexerSettings.CustomCSSFilePath;
            set
            {
                if (Tools.IndexerSettings.CustomCSSFilePath != value)
                {
                    Tools.IndexerSettings.CustomCSSFilePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerUseAttribute
        {
            get => Tools.IndexerSettings.UseAttribute;
            set
            {
                if (Tools.IndexerSettings.UseAttribute != value)
                {
                    Tools.IndexerSettings.UseAttribute = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerCreateParseableJson
        {
            get => Tools.IndexerSettings.CreateParseableJson;
            set
            {
                if (Tools.IndexerSettings.CreateParseableJson != value)
                {
                    Tools.IndexerSettings.CreateParseableJson = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndexerBinaryUnits
        {
            get => Tools.IndexerSettings.BinaryUnits;
            set
            {
                if (Tools.IndexerSettings.BinaryUnits != value)
                {
                    Tools.IndexerSettings.BinaryUnits = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion
    }
}
