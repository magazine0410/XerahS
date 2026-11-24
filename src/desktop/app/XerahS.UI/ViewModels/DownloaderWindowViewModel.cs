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

using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XerahS.Common;

namespace XerahS.UI.ViewModels;

public partial class DownloaderWindowViewModel : ViewModelBase, IDisposable
{
    private readonly FileDownloader _downloader;
    private readonly UpdateChecker _updateChecker;
    private bool _disposed;

    [ObservableProperty]
    private string _statusText = "Preparing download...";

    [ObservableProperty]
    private double _downloadPercentage;

    [ObservableProperty]
    private string _downloadSpeedText = string.Empty;

    [ObservableProperty]
    private string _downloadedSizeText = string.Empty;

    [ObservableProperty]
    private string _totalSizeText = string.Empty;

    [ObservableProperty]
    private bool _canCancel = true;

    [ObservableProperty]
    private DownloaderFormStatus _status = DownloaderFormStatus.Waiting;

    [ObservableProperty]
    private string _fileName = string.Empty;

    public bool? DialogResult { get; private set; }

    public Action<bool?>? RequestClose { get; set; }

    public string DownloadLocation { get; private set; } = string.Empty;

    public DownloaderWindowViewModel(UpdateChecker updateChecker)
    {
        _updateChecker = updateChecker;
        _downloader = new FileDownloader();
        _downloader.FileSizeReceived += OnFileSizeReceived;
        _downloader.ProgressChanged += OnProgressChanged;

        FileName = updateChecker.FileName;
    }

    public async Task StartDownloadAsync()
    {
        if (Status != DownloaderFormStatus.Waiting)
            return;

        if (string.IsNullOrEmpty(_updateChecker.DownloadURL))
        {
            StatusText = "Download URL not available.";
            return;
        }

        Status = DownloaderFormStatus.DownloadStarted;
        StatusText = "Getting file size...";

        // Create temp folder
        string folderPath = Path.Combine(Path.GetTempPath(), AppResources.AppName);
        Directory.CreateDirectory(folderPath);
        DownloadLocation = Path.Combine(folderPath, _updateChecker.FileName);

        _downloader.URL = _updateChecker.DownloadURL;
        _downloader.DownloadLocation = DownloadLocation;

        // Set Accept header for GitHub API downloads
        if (_updateChecker is GitHubUpdateChecker)
        {
            _downloader.AcceptHeader = "application/octet-stream";
        }

        DebugHelper.WriteLine($"Downloading: \"{_updateChecker.DownloadURL}\" -> \"{DownloadLocation}\"");

        try
        {
            bool success = await _downloader.StartDownload();

            if (success)
            {
                StatusText = "Download completed. Starting installer...";
                Status = DownloaderFormStatus.DownloadCompleted;
                CanCancel = false;

                await LaunchInstallerAsync();
            }
            else if (_downloader.IsCanceled)
            {
                StatusText = "Download canceled.";
            }
            else
            {
                StatusText = "Download failed.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
            DebugHelper.WriteException(ex, "Update download failed");
        }
    }

    private void OnFileSizeReceived()
    {
        Dispatcher.UIThread.Post(() =>
        {
            TotalSizeText = _downloader.FileSize.ToSizeString();
            StatusText = $"Downloading {FileName}...";
        });
    }

    private void OnProgressChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            DownloadPercentage = _downloader.DownloadPercentage;
            DownloadSpeedText = $"{((long)_downloader.DownloadSpeed).ToSizeString()}/s";
            DownloadedSizeText = _downloader.DownloadedSize.ToSizeString();
        });
    }

    private async Task LaunchInstallerAsync()
    {
        Status = DownloaderFormStatus.InstallStarted;

        // Small delay to allow UI to update
        await Task.Delay(500);

        if (OperatingSystem.IsLinux())
        {
            await InstallLinuxPackageAsync();
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = DownloadLocation,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE",
                UseShellExecute = true
            };

            Process.Start(startInfo);

            // Close the downloader window - the app will be shut down by UpdateService
            DialogResult = true;
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to start installer: {ex.Message}";
            DebugHelper.WriteException(ex, "Failed to launch installer");
            CanCancel = true;
        }
    }

    /// <summary>
    /// True when <paramref name="fileName"/> is a package that <see cref="InstallLinuxPackageAsync"/> can install.
    /// </summary>
    public static bool IsInstallableLinuxPackage(string fileName)
    {
        return GetLinuxPackageManager(fileName) != null;
    }

    private static string? GetLinuxPackageManager(string fileName)
    {
        if (fileName.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
        {
            return "dnf";
        }

        if (fileName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
        {
            return "apt-get";
        }

        return null;
    }

    /// <summary>
    /// Installs the downloaded .rpm or .deb through the system package manager, with polkit asking
    /// for the password. The app is closed only after the package manager reports success.
    /// </summary>
    private async Task InstallLinuxPackageAsync()
    {
        string? packageManager = GetLinuxPackageManager(DownloadLocation);
        if (packageManager == null)
        {
            StatusText = $"{FileName} cannot be installed automatically.";
            CanCancel = true;
            return;
        }

        StatusText = "Installing update. Enter your password if asked...";

        try
        {
            var startInfo = new ProcessStartInfo("pkexec")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(packageManager);
            startInfo.ArgumentList.Add("install");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(DownloadLocation);

            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                StatusText = "Failed to start the package manager.";
                CanCancel = true;
                return;
            }

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                DialogResult = true;
                RequestClose?.Invoke(true);
                return;
            }

            // pkexec exits with 126 when the password dialog is dismissed, and with 127 when
            // authorization fails or the package manager is not installed.
            StatusText = process.ExitCode switch
            {
                126 => "Installation canceled.",
                127 => $"Installation was not authorized, or {packageManager} is not installed.",
                _ => $"Installation failed: {packageManager} exited with code {process.ExitCode}."
            };
            DebugHelper.WriteLine($"Update install with pkexec {packageManager} exited with code {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to start installer: {ex.Message}";
            DebugHelper.WriteException(ex, "Failed to launch package manager");
        }

        CanCancel = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Status == DownloaderFormStatus.DownloadStarted)
        {
            _downloader.StopDownload();
        }

        DialogResult = false;
        RequestClose?.Invoke(false);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _downloader.FileSizeReceived -= OnFileSizeReceived;
            _downloader.ProgressChanged -= OnProgressChanged;

            if (_downloader.IsDownloading)
            {
                _downloader.StopDownload();
            }

            _disposed = true;
        }
    }
}
