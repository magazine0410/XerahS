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

using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ShareX.ImageEditor.Presentation.Theming;
using System.Security.Cryptography;
using System.Text;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks;

namespace XerahS.UI.ViewModels;

/// <summary>
/// A thumbnail of ShareX's main window task list (ThumbnailItemViewModel): a task of this session,
/// or a recent task shown at startup.
/// </summary>
public sealed partial class TaskThumbnailViewModel : ObservableObject, IDisposable
{
    private int _refreshVersion;
    private string? _thumbnailSource;

    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _details = string.Empty;
    [ObservableProperty] private string _placeholderIcon = LucideIcons.file;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isProgressVisible;
    [ObservableProperty] private bool _isFailed;

    public TaskThumbnailViewModel(WorkerTask task)
    {
        Task = task;
        Refresh();
    }

    public TaskThumbnailViewModel(RecentTask recentTask)
    {
        RecentTask = recentTask;
        Refresh();
    }

    /// <summary>The task of this session, or null for a recent task from an earlier one.</summary>
    public WorkerTask? Task { get; }

    public RecentTask? RecentTask { get; }

    public string FilePath => Task?.Info?.FilePath ?? RecentTask?.FilePath ?? string.Empty;

    /// <summary>The upload result: the shortened URL, else the URL.</summary>
    public string Url
    {
        get
        {
            if (Task?.Info?.Result is { } result) return result.ToString() ?? string.Empty;
            if (RecentTask == null) return string.Empty;
            return !string.IsNullOrEmpty(RecentTask.ShortenedURL) ? RecentTask.ShortenedURL : RecentTask.URL;
        }
    }

    public bool IsWorking => Task?.IsWorking == true;

    public int Width => Math.Max(96, SettingsManager.Settings.ThumbnailSize.Width);
    public int Height => Math.Max(72, SettingsManager.Settings.ThumbnailSize.Height);
    public bool ShowTitleAbove => SettingsManager.Settings.ShowThumbnailTitle && SettingsManager.Settings.ThumbnailTitleLocation == ThumbnailTitleLocation.Top;
    public bool ShowTitleBelow => SettingsManager.Settings.ShowThumbnailTitle && SettingsManager.Settings.ThumbnailTitleLocation == ThumbnailTitleLocation.Bottom;
    public bool HasThumbnail => Thumbnail != null;
    public bool HasNoThumbnail => Thumbnail == null;
    public bool IsVideo => FileHelpers.IsVideoFile(FilePath);
    public bool ShowPlayIcon => HasThumbnail && IsVideo;
    public bool IsImageFile => File.Exists(FilePath) && FileHelpers.IsImageFile(FilePath);

    /// <summary>ShareX's IsClickable: whether the thumbnail click action can act on this task.</summary>
    public bool IsClickable => SettingsManager.Settings.ThumbnailClickAction switch
    {
        ThumbnailViewClickAction.Default or ThumbnailViewClickAction.OpenFile => File.Exists(FilePath),
        ThumbnailViewClickAction.OpenImageViewer or ThumbnailViewClickAction.EditImage => IsImageFile,
        ThumbnailViewClickAction.OpenFolder => !string.IsNullOrEmpty(FilePath),
        ThumbnailViewClickAction.OpenURL => !string.IsNullOrWhiteSpace(Url),
        _ => false
    };

    partial void OnThumbnailChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(HasNoThumbnail));
        OnPropertyChanged(nameof(ShowPlayIcon));
        oldValue?.Dispose();
    }

    public void Refresh()
    {
        TaskInfo? info = Task?.Info;
        string filePath = FilePath;
        string fileName = !string.IsNullOrEmpty(info?.FileName) ? info.FileName : Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName) && RecentTask != null) fileName = FileHelpers.GetFileNameSafe(RecentTask.ToString());
        Title = string.IsNullOrEmpty(fileName) ? "Untitled task" : fileName;
        Details = info?.ToString() ?? RecentTask?.ToString() ?? string.Empty;
        Progress = info?.Progress?.Percentage ?? 0;
        IsProgressVisible = Task?.IsWorking == true && info?.Progress != null;
        IsFailed = Task?.Status == Core.TaskStatus.Failed;
        PlaceholderIcon = GetPlaceholderIcon(filePath, Url);
        OnPropertyChanged(nameof(IsVideo));
        OnPropertyChanged(nameof(ShowPlayIcon));
        OnPropertyChanged(nameof(IsClickable));

        if (!string.Equals(_thumbnailSource, filePath, StringComparison.Ordinal) && File.Exists(filePath))
        {
            _thumbnailSource = filePath;
            StartThumbnailRefresh(filePath);
        }
    }

    /// <summary>Applies changed thumbnail settings: size, title, and click action.</summary>
    public void RefreshSettings()
    {
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(ShowTitleAbove));
        OnPropertyChanged(nameof(ShowTitleBelow));
        OnPropertyChanged(nameof(IsClickable));
        if (File.Exists(FilePath)) StartThumbnailRefresh(FilePath);
    }

    private void StartThumbnailRefresh(string filePath)
    {
        int version = ++_refreshVersion;
        int width = Width;
        _ = RefreshThumbnailAsync(version, width, filePath);
    }

    private async Task RefreshThumbnailAsync(int version, int width, string filePath)
    {
        Bitmap? bitmap = await System.Threading.Tasks.Task.Run(() => CreateThumbnail(width, filePath));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version == _refreshVersion && bitmap != null) Thumbnail = bitmap;
            else if (version != _refreshVersion) bitmap?.Dispose();
        });
    }

    private static Bitmap? CreateThumbnail(int width, string filePath)
    {
        try
        {
            if (FileHelpers.IsImageFile(filePath))
            {
                using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            }

            // ShareX asks the Windows shell for a video thumbnail; on Linux the desktop's thumbnail cache
            // has one when the file manager has shown the video.
            if (FileHelpers.IsVideoFile(filePath) && FindCachedThumbnail(filePath) is string cached)
            {
                using FileStream stream = File.OpenRead(cached);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Unable to create a task thumbnail");
        }

        return null;
    }

    /// <summary>The freedesktop.org thumbnail of a file, when the cache has one.</summary>
    internal static string? FindCachedThumbnail(string filePath)
    {
        if (!OperatingSystem.IsLinux()) return null;

        string cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdgCache
            ? xdgCache
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        string uri = new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
        string name = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(uri))) + ".png";
        foreach (string size in new[] { "xx-large", "x-large", "large", "normal" })
        {
            string path = Path.Combine(cacheHome, "thumbnails", size, name);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static string GetPlaceholderIcon(string filePath, string url)
    {
        if (string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(url)) return LucideIcons.link;
        if (FileHelpers.IsVideoFile(filePath)) return LucideIcons.file_video;
        if (FileHelpers.IsTextFile(filePath)) return LucideIcons.file_text;
        if (FileHelpers.IsImageFile(filePath)) return LucideIcons.file_image;
        return LucideIcons.file;
    }

    public void Dispose()
    {
        _refreshVersion++;
        Thumbnail = null;
    }
}
