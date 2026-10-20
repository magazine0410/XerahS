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

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using XerahS.Bootstrap;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Automation;
using XerahS.Core.Services;

namespace XerahS.UI.Services;

/// <summary>
/// Runs ShareX's job and workflow arguments one after another, as ShareXCLIManager.UseCommandLineArgs does. "-&lt;JobName&gt;"
/// runs the job with the default workflow settings, with the file after it for the jobs that ShareX gives a file to, and
/// "-workflow" runs the workflow of that name as its hotkey would.
/// </summary>
internal sealed class CommandLineJobRunner
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IDesktopTaskManager _taskManager;
    private readonly IWorkflowOrchestrator _orchestrator;

    public CommandLineJobRunner(IClassicDesktopStyleApplicationLifetime desktop, IDesktopTaskManager taskManager, IWorkflowOrchestrator orchestrator)
    {
        _desktop = desktop;
        _taskManager = taskManager;
        _orchestrator = orchestrator;
    }

    public async Task RunAsync(IReadOnlyList<CommandLineRequest> requests)
    {
        foreach (CommandLineRequest request in requests)
        {
            try
            {
                DebugHelper.WriteLine($"Command line: {request}");

                if (request.Kind == CommandLineRequestKind.Workflow)
                {
                    await RunWorkflowAsync(request.Parameter ?? string.Empty);
                }
                else
                {
                    await RunJobAsync(request);
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, $"Command line: {request} failed");
            }
        }
    }

    private Task RunWorkflowAsync(string name)
    {
        var workflow = CommandLineArguments.FindWorkflow(SettingsManager.WorkflowsConfig?.Hotkeys, name);
        if (workflow == null)
        {
            DebugHelper.WriteLine($"Command line: no workflow with a job is named \"{name}\".");
            return Task.CompletedTask;
        }

        return _orchestrator.RunWorkflowAsync(workflow);
    }

    private async Task RunJobAsync(CommandLineRequest request)
    {
        string? filePath = null;
        if (!string.IsNullOrEmpty(request.Parameter))
        {
            // As in ShareX, a file that does not exist stops the job.
            filePath = Path.GetFullPath(request.Parameter);
            if (!File.Exists(filePath))
            {
                DebugHelper.WriteLine($"Command line: {request} was not run because the file does not exist.");
                return;
            }
        }

        // As in ShareX, the job runs with the default workflow settings.
        TaskSettings taskSettings = TaskSettings.GetSafeTaskSettings(SettingsManager.DefaultTaskSettings);
        taskSettings.Job = request.Job;

        if (filePath == null || !await TryRunJobWithFileAsync(request.Job, filePath, taskSettings))
        {
            await _orchestrator.RunJobAsync(request.Job, taskSettings);
        }
    }

    /// <summary>The jobs to which ShareX's ExecuteJob gives the file; the other jobs run without it.</summary>
    private async Task<bool> TryRunJobWithFileAsync(WorkflowType job, string filePath, TaskSettings taskSettings)
    {
        Window? owner = GetOwner();

        switch (job)
        {
            case WorkflowType.FileUpload:
                await _taskManager.StartFileTask(taskSettings, filePath);
                return true;

            case WorkflowType.PinToScreen:
            case WorkflowType.PinToScreenFromFile:
                await PinToScreenToolService.PinFileAsync(filePath, taskSettings);
                return true;

            case WorkflowType.ImageEditor:
            case WorkflowType.ImageBeautifier:
                if (!await ImageEditingToolService.OpenImageFileAsync(filePath, taskSettings, job == WorkflowType.ImageBeautifier, _taskManager))
                {
                    ReportUnreadableImage(job);
                }
                return true;

            case WorkflowType.ImageEffects:
                if (!await ImageEditingToolService.OpenImageEffectsAsync(filePath, taskSettings, _taskManager))
                {
                    ReportUnreadableImage(job);
                }
                return true;

            case WorkflowType.ImageViewer:
                Views.ImageViewerWindow.OpenFile(filePath);
                return true;

            case WorkflowType.VideoConverter:
                MediaToolsToolService.OpenVideoConverter(filePath, owner);
                return true;

            case WorkflowType.VideoTrimmer:
                MediaToolsToolService.OpenVideoTrimmer(filePath, owner, taskSettings);
                return true;

            case WorkflowType.OCR:
                await OcrToolService.OcrImageFileAsync(filePath, owner, taskSettings);
                return true;

            case WorkflowType.QRCode:
                // ShareX opens its QR code window on the image file's decoded codes.
                if (ImageHelpers.LoadBitmap(filePath) is { } image)
                {
                    await QrCodeToolService.DecodeAndShowAsync(image, owner);
                }
                else
                {
                    ReportUnreadableImage(job);
                }
                return true;

            case WorkflowType.HashCheck:
                await HashCheckToolService.OpenHashCheckAsync(owner, filePath, taskSettings);
                return true;

            case WorkflowType.Metadata:
                new Views.MetadataWindow(filePath, () => NotificationSoundService.PlayActionCompleted()).Show();
                return true;

            case WorkflowType.StripMetadata:
                await MetadataToolService.StripFileAsync(filePath, taskSettings);
                return true;

            default:
                return false;
        }
    }

    /// <summary>The main window, when it is shown; tool windows open on their own while it is hidden in the tray.</summary>
    private Window? GetOwner()
    {
        Window? mainWindow = _desktop.MainWindow;
        return mainWindow is { IsVisible: true } && mainWindow.WindowState != WindowState.Minimized ? mainWindow : null;
    }

    private static void ReportUnreadableImage(WorkflowType job)
    {
        UploadWorkflowService.ReportError(
            new IOException("The image could not be opened. Check that it uses a supported image format."),
            $"Could not open {EnumExtensions.GetDescription(job)}");
    }
}
