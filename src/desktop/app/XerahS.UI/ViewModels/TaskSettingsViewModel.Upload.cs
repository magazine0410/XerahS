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

namespace XerahS.UI.ViewModels
{
    public partial class TaskSettingsViewModel
    {
        #region Upload / File Naming

        public string NameFormatPattern
        {
            get => UploadSource.UploadSettings.NameFormatPattern;
            set
            {
                if (UploadSource.UploadSettings.NameFormatPattern != value)
                {
                    UploadSource.UploadSettings.NameFormatPattern = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NameFormatPatternActiveWindow
        {
            get => UploadSource.UploadSettings.NameFormatPatternActiveWindow;
            set
            {
                if (UploadSource.UploadSettings.NameFormatPatternActiveWindow != value)
                {
                    UploadSource.UploadSettings.NameFormatPatternActiveWindow = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool FileUploadUseNamePattern
        {
            get => UploadSource.UploadSettings.FileUploadUseNamePattern;
            set
            {
                if (UploadSource.UploadSettings.FileUploadUseNamePattern != value)
                {
                    UploadSource.UploadSettings.FileUploadUseNamePattern = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool FileUploadReplaceProblematicCharacters
        {
            get => UploadSource.UploadSettings.FileUploadReplaceProblematicCharacters;
            set
            {
                if (UploadSource.UploadSettings.FileUploadReplaceProblematicCharacters != value)
                {
                    UploadSource.UploadSettings.FileUploadReplaceProblematicCharacters = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool URLRegexReplace
        {
            get => UploadSource.UploadSettings.URLRegexReplace;
            set
            {
                if (UploadSource.UploadSettings.URLRegexReplace != value)
                {
                    UploadSource.UploadSettings.URLRegexReplace = value;
                    OnPropertyChanged();
                }
            }
        }

        public string URLRegexReplacePattern
        {
            get => UploadSource.UploadSettings.URLRegexReplacePattern;
            set
            {
                if (UploadSource.UploadSettings.URLRegexReplacePattern != value)
                {
                    UploadSource.UploadSettings.URLRegexReplacePattern = value;
                    OnPropertyChanged();
                }
            }
        }

        public string URLRegexReplaceReplacement
        {
            get => UploadSource.UploadSettings.URLRegexReplaceReplacement;
            set
            {
                if (UploadSource.UploadSettings.URLRegexReplaceReplacement != value)
                {
                    UploadSource.UploadSettings.URLRegexReplaceReplacement = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ClipboardUploadURLContents
        {
            get => UploadSource.UploadSettings.ClipboardUploadURLContents;
            set
            {
                if (UploadSource.UploadSettings.ClipboardUploadURLContents != value)
                {
                    UploadSource.UploadSettings.ClipboardUploadURLContents = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ClipboardUploadShortenURL
        {
            get => UploadSource.UploadSettings.ClipboardUploadShortenURL;
            set
            {
                if (UploadSource.UploadSettings.ClipboardUploadShortenURL != value)
                {
                    UploadSource.UploadSettings.ClipboardUploadShortenURL = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ClipboardUploadShareURL
        {
            get => UploadSource.UploadSettings.ClipboardUploadShareURL;
            set
            {
                if (UploadSource.UploadSettings.ClipboardUploadShareURL != value)
                {
                    UploadSource.UploadSettings.ClipboardUploadShareURL = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion
    }
}
