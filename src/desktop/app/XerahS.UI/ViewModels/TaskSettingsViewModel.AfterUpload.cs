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
using XerahS.Uploaders.PluginSystem;

namespace XerahS.UI.ViewModels
{
    public partial class TaskSettingsViewModel
    {
        #region After Upload Tasks

        public bool ShowAfterUploadWindow
        {
            get => AfterUploadSource.AfterUploadJob.HasFlag(AfterUploadTasks.ShowAfterUploadWindow);
            set
            {
                if (ShowAfterUploadWindow != value)
                {
                    UpdateAfterUploadTask(AfterUploadTasks.ShowAfterUploadWindow, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool CopyURLToClipboard
        {
            get => AfterUploadSource.AfterUploadJob.HasFlag(AfterUploadTasks.CopyURLToClipboard);
            set
            {
                if (CopyURLToClipboard != value)
                {
                    UpdateAfterUploadTask(AfterUploadTasks.CopyURLToClipboard, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool UseURLShortener
        {
            get => AfterUploadSource.AfterUploadJob.HasFlag(AfterUploadTasks.UseURLShortener);
            set
            {
                if (UseURLShortener != value)
                {
                    UpdateAfterUploadTask(AfterUploadTasks.UseURLShortener, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool ShareURL
        {
            get => AfterUploadSource.AfterUploadJob.HasFlag(AfterUploadTasks.ShareURL);
            set
            {
                if (ShareURL != value)
                {
                    UpdateAfterUploadTask(AfterUploadTasks.ShareURL, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool OpenURL
        {
            get => AfterUploadSource.AfterUploadJob.HasFlag(AfterUploadTasks.OpenURL);
            set
            {
                if (OpenURL != value)
                {
                    UpdateAfterUploadTask(AfterUploadTasks.OpenURL, value);
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowQRCode
        {
            get => AfterUploadSource.AfterUploadJob.HasFlag(AfterUploadTasks.ShowQRCode);
            set
            {
                if (ShowQRCode != value)
                {
                    UpdateAfterUploadTask(AfterUploadTasks.ShowQRCode, value);
                    OnPropertyChanged();
                }
            }
        }

        private List<UploaderInstance>? _urlShortenerDestinations;

        public IReadOnlyList<UploaderInstance> UrlShortenerDestinations
        {
            get
            {
                _urlShortenerDestinations ??= [new UploaderInstance
                    { InstanceId = string.Empty, DisplayName = "Default URL shortener", Category = UploaderCategory.UrlShortener },
                    .. InstanceManager.Instance.GetInstancesByCategory(UploaderCategory.UrlShortener)];
                string? selectedId = SourceFor(_settings.UseDefaultDestinations).UrlShortenerDestinationInstanceId;
                if (!string.IsNullOrEmpty(selectedId) && _urlShortenerDestinations.All(item => item.InstanceId != selectedId))
                {
                    _urlShortenerDestinations.Add(new UploaderInstance
                    {
                        InstanceId = selectedId, DisplayName = "Unavailable URL shortener", Category = UploaderCategory.UrlShortener
                    });
                }
                return _urlShortenerDestinations;
            }
        }

        public UploaderInstance? SelectedUrlShortenerDestination
        {
            get => UrlShortenerDestinations.FirstOrDefault(item => item.InstanceId ==
                (SourceFor(_settings.UseDefaultDestinations).UrlShortenerDestinationInstanceId ?? string.Empty));
            set
            {
                if (value == null) return;
                SourceFor(_settings.UseDefaultDestinations).UrlShortenerDestinationInstanceId = value.InstanceId;
                OnPropertyChanged();
            }
        }

        public bool ShowAfterUploadShortenerDestination => Job != WorkflowType.ShortenURL;

        private void UpdateAfterUploadTask(AfterUploadTasks task, bool enabled)
        {
            if (enabled)
                AfterUploadSource.AfterUploadJob |= task;
            else
                AfterUploadSource.AfterUploadJob &= ~task;
        }

        #endregion
    }
}
