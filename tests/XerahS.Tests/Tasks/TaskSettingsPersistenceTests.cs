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

using Avalonia.Headless.NUnit;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Hotkeys;
using XerahS.Uploaders;
using TaskManager = XerahS.Core.Managers.TaskManager;

namespace XerahS.Tests.Tasks;

[TestFixture, NonParallelizable]
public class TaskSettingsPersistenceTests
{
    [AvaloniaTest]
    public async Task TaskCompletion_SavesWhenEnabledAndTheQueueIsIdle()
    {
        string originalFolder = SettingsManager.PersonalFolder;
        var originalSettings = SettingsManager.Settings;
        var originalUploaders = SettingsManager.UploadersConfig;
        var originalWorkflows = SettingsManager.WorkflowsConfig;
        string originalBrowser = HelpersOptions.BrowserPath;
        string root = Path.Combine(Path.GetTempPath(), "xerahs-task-settings-" + Guid.NewGuid().ToString("N"));
        var manager = (TaskManager)Activator.CreateInstance(typeof(TaskManager), nonPublic: true)!;
        try
        {
            SettingsManager.PersonalFolder = root;
            typeof(SettingsManager).GetProperty(nameof(SettingsManager.Settings))!.SetValue(null, new ApplicationConfig
            {
                UseMachineSpecificUploadersConfig = false, UseMachineSpecificWorkflowsConfig = false,
                SaveSettingsAfterTaskCompleted = false, BrowserPath = "/test/browser"
            });
            SettingsManager.UploadersConfig = new UploadersConfig();
            SettingsManager.WorkflowsConfig = new WorkflowsConfig();
            TaskSettings settings = new() { Job = WorkflowType.PrintScreen, AfterCaptureJob = AfterCaptureTasks.None, AfterUploadJob = AfterUploadTasks.None };
            await manager.StartTask(settings, new SKBitmap(1, 1));
            Assert.That(File.Exists(SettingsManager.ApplicationConfigFilePath), Is.False);
            SettingsManager.Settings.SaveSettingsAfterTaskCompleted = true;
            using var resume = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            SKBitmap firstImage = new(1, 1);
            manager.TaskStarted += (_, task) =>
            {
                if (ReferenceEquals(task.Info.Metadata.Image, firstImage))
                {
                    entered.SetResult();
                    if (!resume.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
                }
            };
            Task first = Task.Run(() => manager.StartTask(settings, firstImage));
            await entered.Task;
            try
            {
                await manager.StartTask(settings, new SKBitmap(1, 1));
                Assert.That(File.Exists(SettingsManager.ApplicationConfigFilePath), Is.False, "A pending task defers the save, as in ShareX.");
            }
            finally { resume.Set(); await first; }
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(SettingsManager.ApplicationConfigFilePath), Does.Contain("/test/browser"));
                Assert.That(File.Exists(SettingsManager.WorkflowsConfigFilePath), Is.True);
                Assert.That(File.Exists(SettingsManager.UploadersConfigFilePath), Is.True);
            });
        }
        finally
        {
            SettingsManager.PersonalFolder = originalFolder;
            typeof(SettingsManager).GetProperty(nameof(SettingsManager.Settings))!.SetValue(null, originalSettings);
            SettingsManager.UploadersConfig = originalUploaders;
            SettingsManager.WorkflowsConfig = originalWorkflows;
            HelpersOptions.BrowserPath = originalBrowser;
            foreach (var task in manager.Tasks) task.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
