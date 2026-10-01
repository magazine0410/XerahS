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
using XerahS.Services.Abstractions;

namespace XerahS.UI.ViewModels
{
    public partial class TaskSettingsViewModel
    {
        #region Image Settings

        public EImageFormat ImageFormat
        {
            get => ImageSource.ImageSettings.ImageFormat;
            set
            {
                if (ImageSource.ImageSettings.ImageFormat != value)
                {
                    ImageSource.ImageSettings.ImageFormat = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ImageJPEGQuality
        {
            get => ImageSource.ImageSettings.ImageJPEGQuality;
            set
            {
                if (ImageSource.ImageSettings.ImageJPEGQuality != value)
                {
                    ImageSource.ImageSettings.ImageJPEGQuality = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ThumbnailWidth
        {
            get => ImageSource.ImageSettings.ThumbnailWidth;
            set
            {
                if (ImageSource.ImageSettings.ThumbnailWidth != value)
                {
                    ImageSource.ImageSettings.ThumbnailWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ThumbnailHeight
        {
            get => ImageSource.ImageSettings.ThumbnailHeight;
            set
            {
                if (ImageSource.ImageSettings.ThumbnailHeight != value)
                {
                    ImageSource.ImageSettings.ThumbnailHeight = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ThumbnailName
        {
            get => ImageSource.ImageSettings.ThumbnailName;
            set
            {
                if (ImageSource.ImageSettings.ThumbnailName != value)
                {
                    ImageSource.ImageSettings.ThumbnailName = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ThumbnailCheckSize
        {
            get => ImageSource.ImageSettings.ThumbnailCheckSize;
            set
            {
                if (ImageSource.ImageSettings.ThumbnailCheckSize != value)
                {
                    ImageSource.ImageSettings.ThumbnailCheckSize = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion
    }
}
