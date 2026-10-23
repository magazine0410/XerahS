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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XerahS.Common;
using XerahS.Core;
using XerahS.Media;

namespace XerahS.UI.ViewModels;

public partial class VideoThumbnailerViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _inputFilePath;

    [ObservableProperty]
    private string _inputFileName = "";

    [ObservableProperty]
    private int _thumbnailCount = 9;

    [ObservableProperty]
    private int _columnCount = 3;

    [ObservableProperty]
    private int _maxThumbnailWidth = 512;

    [ObservableProperty]
    private bool _combineScreenshots = true;

    [ObservableProperty]
    private bool _addVideoInfo = true;

    [ObservableProperty]
    private bool _addTimestamp = true;

    [ObservableProperty]
    private string _outputFolder = "";

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusText = "Select a video file";

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private bool _hasInput;

    /// <summary>ShareX's "Upload created thumbnails": each created file is uploaded as a file upload task.</summary>
    [ObservableProperty]
    private bool _uploadThumbnails = true;

    /// <summary>ShareX's "Open output folder when finished".</summary>
    [ObservableProperty]
    private bool _openDirectory;

    public event EventHandler? FilePickerRequested;
    public event EventHandler? FolderPickerRequested;

    private readonly TaskSettings? _taskSettings;
    private readonly Core.VideoThumbnailOptions? _options;

    public VideoThumbnailerViewModel(TaskSettings? taskSettings = null)
    {
        OutputFolder = TaskHelpers.GetScreenshotsFolder();
        _taskSettings = taskSettings;
        _options = taskSettings?.ToolsSettingsReference.VideoThumbnailOptions;
        if (_options == null) return;

        _thumbnailCount = _options.ThumbnailCount;
        _columnCount = _options.ColumnCount;
        _maxThumbnailWidth = _options.MaxThumbnailWidth;
        _combineScreenshots = _options.CombineScreenshots;
        _addVideoInfo = _options.AddVideoInfo;
        _addTimestamp = _options.AddTimestamp;
        _uploadThumbnails = _options.UploadThumbnails;
        _openDirectory = _options.OpenDirectory;
    }

    partial void OnThumbnailCountChanged(int value) => SaveOptions();
    partial void OnColumnCountChanged(int value) => SaveOptions();
    partial void OnMaxThumbnailWidthChanged(int value) => SaveOptions();
    partial void OnCombineScreenshotsChanged(bool value) => SaveOptions();
    partial void OnAddVideoInfoChanged(bool value) => SaveOptions();
    partial void OnAddTimestampChanged(bool value) => SaveOptions();
    partial void OnUploadThumbnailsChanged(bool value) => SaveOptions();
    partial void OnOpenDirectoryChanged(bool value) => SaveOptions();

    /// <summary>As in ShareX, the options are kept in the workflow's tool settings.</summary>
    private void SaveOptions()
    {
        if (_options == null) return;

        _options.ThumbnailCount = ThumbnailCount;
        _options.ColumnCount = ColumnCount;
        _options.MaxThumbnailWidth = MaxThumbnailWidth;
        _options.CombineScreenshots = CombineScreenshots;
        _options.AddVideoInfo = AddVideoInfo;
        _options.AddTimestamp = AddTimestamp;
        _options.UploadThumbnails = UploadThumbnails;
        _options.OpenDirectory = OpenDirectory;
        _ = SaveWorkflowsAsync();
    }

    private static async Task SaveWorkflowsAsync()
    {
        try
        {
            await SettingsManager.SaveWorkflowsConfigAsync(raiseSettingsChanged: false);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to save the video thumbnailer options");
        }
    }

    [RelayCommand]
    private void BrowseInput()
    {
        FilePickerRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        FolderPickerRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetInputFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        InputFilePath = filePath;
        InputFileName = Path.GetFileName(filePath);
        HasInput = true;
        StatusText = InputFileName;
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (string.IsNullOrEmpty(InputFilePath) || IsProcessing) return;

        string ffmpegPath = PathsManager.GetFFmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            StatusText = "FFmpeg not found. Please download FFmpeg first.";
            return;
        }

        IsProcessing = true;
        ProgressPercent = 0;
        StatusText = "Generating thumbnails...";

        try
        {
            string inputPath = InputFilePath;
            string outputDir = OutputFolder;

            var options = new XerahS.Media.VideoThumbnailOptions
            {
                ThumbnailCount = ThumbnailCount,
                ColumnCount = ColumnCount,
                MaxThumbnailWidth = MaxThumbnailWidth,
                CombineScreenshots = CombineScreenshots,
                AddVideoInfo = AddVideoInfo,
                AddTimestamp = AddTimestamp,
                DefaultOutputDirectory = outputDir,
                OutputLocation = XerahS.Media.ThumbnailLocationType.DefaultFolder,
                OpenDirectory = OpenDirectory
            };

            var thumbnails = await Task.Run(() =>
            {
                FileHelpers.CreateDirectory(outputDir);

                var thumbnailer = new VideoThumbnailer(ffmpegPath, options);
                thumbnailer.ProgressChanged += (current, total) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ProgressPercent = (int)((double)current / total * 100);
                        StatusText = $"Generating thumbnail {current}/{total}...";
                    });
                };

                return thumbnailer.TakeThumbnails(inputPath);
            });

            if (thumbnails.Count > 0)
            {
                ProgressPercent = 100;
                StatusText = $"Generated {thumbnails.Count} thumbnail(s)";

                // ShareX's OpenVideoThumbnailer: each created file (the combined sheet, or every frame when the
                // frames are not combined) is uploaded with the tool's task settings.
                if (UploadThumbnails)
                {
                    TaskSettings settings = _taskSettings ?? TaskSettings.GetSafeTaskSettings(SettingsManager.DefaultTaskSettings);
                    foreach (var thumbnail in thumbnails)
                    {
                        TaskSettings fileSettings = TaskSettings.GetSafeTaskSettings(settings);
                        fileSettings.Job = WorkflowType.FileUpload;
                        _ = XerahS.Core.Helpers.TaskHelpers.TaskManagerService.StartFileTask(fileSettings, thumbnail.FilePath);
                    }
                }
            }
            else
            {
                StatusText = "No thumbnails generated. Check video file.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            DebugHelper.WriteException(ex, "VideoThumbnailer");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        InputFilePath = null;
        InputFileName = "";
        HasInput = false;
        ProgressPercent = 0;
        StatusText = "Select a video file";
    }
}
