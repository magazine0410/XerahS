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
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using NUnit.Framework;
using XerahS.Core;
using XerahS.Core.Hotkeys;
using XerahS.Core.Managers;
using XerahS.RegionCapture.ScreenRecording;
using XerahS.Tests.Xip0052;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.Core;

[TestFixture]
[NonParallelizable]
public class TaskSettingsDefaultsTests
{
    private TaskSettingsCapture _previousCapture = null!;
    private AfterCaptureTasks _previousAfterCapture;

    [SetUp]
    public void SetUp()
    {
        _previousCapture = SettingsManager.DefaultTaskSettings.CaptureSettings;
        _previousAfterCapture = SettingsManager.DefaultTaskSettings.AfterCaptureJob;
        SettingsManager.DefaultTaskSettings.CaptureSettings = new TaskSettingsCapture();
    }

    [TearDown]
    public void TearDown()
    {
        SettingsManager.DefaultTaskSettings.CaptureSettings = _previousCapture;
        SettingsManager.DefaultTaskSettings.AfterCaptureJob = _previousAfterCapture;
    }

    [Test]
    public void SafeTaskSettings_UseTheDefaultsForEverySectionTheWorkflowDoesNotOverride()
    {
        var defaults = new TaskSettings { AfterCaptureJob = AfterCaptureTasks.SaveImageToFile, DestinationInstanceId = "default-destination" };
        defaults.CaptureSettings.ScreenshotDelay = 3;
        defaults.GeneralSettings.PlaySoundAfterCapture = false;
        var workflow = new TaskSettings
        {
            Job = WorkflowType.RectangleRegion,
            WorkflowId = "region",
            AfterCaptureJob = AfterCaptureTasks.AnnotateMedia,
            UseDefaultCaptureSettings = false,
            DestinationInstanceId = "own-destination"
        };
        workflow.CaptureSettings.ScreenshotDelay = 7;

        TaskSettings safe = TaskSettings.GetSafeTaskSettings(workflow, defaults);

        Assert.Multiple(() =>
        {
            Assert.That(safe, Is.Not.SameAs(workflow));
            Assert.That(safe.AfterCaptureJob, Is.EqualTo(AfterCaptureTasks.SaveImageToFile), "After capture tasks come from the defaults.");
            Assert.That(safe.DestinationInstanceId, Is.EqualTo("default-destination"));
            Assert.That(safe.GeneralSettings.PlaySoundAfterCapture, Is.False);
            Assert.That(safe.CaptureSettings.ScreenshotDelay, Is.EqualTo(7), "The overridden section stays the workflow's own.");
            Assert.That(safe.CaptureSettings, Is.Not.SameAs(defaults.CaptureSettings), "The defaults are copied, not shared.");
            Assert.That(safe.Job, Is.EqualTo(WorkflowType.RectangleRegion));
            Assert.That(safe.WorkflowId, Is.EqualTo("region"));
            Assert.That(safe.IsSafeTaskSettings, Is.True);
            Assert.That(workflow.AfterCaptureJob, Is.EqualTo(AfterCaptureTasks.AnnotateMedia), "The saved workflow keeps its own values.");
        });

        safe.AfterCaptureJob = AfterCaptureTasks.CopyImageToClipboard;
        Assert.That(TaskSettings.GetSafeTaskSettings(safe, defaults).AfterCaptureJob, Is.EqualTo(AfterCaptureTasks.CopyImageToClipboard),
            "A running task's settings are copied as they are, not given the defaults again.");
    }

    [Test]
    public void SavedWorkflowsWithoutTheFlags_UseTheDefaults()
    {
        // A workflow saved before the per-section overrides existed.
        const string json = """{ "Job": "RectangleRegion", "AfterCaptureJob": "AnnotateImage, CopyImageToClipboard, SaveImageToFile", "UseDefaultToolsSettings": false }""";
        var settings = JsonConvert.DeserializeObject<TaskSettings>(json)!;

        Assert.Multiple(() =>
        {
            Assert.That(settings.UseDefaultAfterCaptureJob, Is.True);
            Assert.That(settings.UseDefaultCaptureSettings, Is.True);
            Assert.That(settings.UseDefaultDestinations, Is.True);
            Assert.That(settings.UseDefaultAdvancedSettings, Is.True);
            Assert.That(settings.UseDefaultToolsSettings, Is.False, "Saved overrides are kept.");
        });
    }

    [Test]
    public void DefaultRecordingWorkflows_KeepTheirOwnCaptureSettings()
    {
        List<WorkflowSettings> workflows = WorkflowsConfig.GetDefaultWorkflowList();
        WorkflowSettings gdi = workflows.Single(w => w.TaskSettings.Description == "Record screen using GDI");
        WorkflowSettings game = workflows.Single(w => w.TaskSettings.Description == "Record screen for game");

        Assert.Multiple(() =>
        {
            Assert.That(gdi.TaskSettings.UseDefaultCaptureSettings, Is.False);
            Assert.That(gdi.TaskSettings.CaptureSettings.ScreenRecordingSettings.RecordingBackend, Is.EqualTo(RecordingBackend.GDI));
            Assert.That(game.TaskSettings.UseDefaultCaptureSettings, Is.False);
            Assert.That(game.TaskSettings.CaptureSettings.ScreenRecordingSettings.RecordingIntent, Is.EqualTo(RecordingIntent.Game));
            Assert.That(workflows.Where(w => w != gdi && w != game).All(w => w.TaskSettings.UseDefaultCaptureSettings), Is.True);
        });
    }

    [AvaloniaTest]
    public void CaptureTab_ShowsTheDefaultsDisabled_UntilTheWorkflowOverridesThem()
    {
        SettingsManager.DefaultTaskSettings.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode = true;
        SettingsManager.DefaultTaskSettings.AfterCaptureJob = AfterCaptureTasks.CopyImageToClipboard;
        var workflow = new TaskSettings { Job = WorkflowType.RectangleRegion, AfterCaptureJob = AfterCaptureTasks.AnnotateMedia };
        var vm = new TaskSettingsViewModel(workflow, new FakeViewDialogService());
        var panel = new TaskSettingsPanel { DataContext = vm };
        var window = new Window { Width = 800, Height = 900, Content = panel };
        try
        {
            window.Show();
            window.UpdateLayout();
            CheckBox overrideCapture = FindCheckBox(panel, "Override capture settings");
            CheckBox overrideAfterCapture = FindCheckBox(panel, "Override after capture tasks");
            CheckBox activeMonitor = FindCheckBox(panel, "Active monitor only");
            CheckBox annotate = FindCheckBox(panel, "Annotate media");
            Assert.Multiple(() =>
            {
                Assert.That(overrideCapture.IsChecked, Is.False);
                Assert.That(activeMonitor.IsChecked, Is.True, "While inheriting, the controls show the defaults.");
                Assert.That(activeMonitor.IsEffectivelyEnabled, Is.False);
                Assert.That(annotate.IsChecked, Is.False);
                Assert.That(annotate.IsEffectivelyEnabled, Is.False);
            });

            overrideCapture.IsChecked = true;
            overrideAfterCapture.IsChecked = true;
            window.UpdateLayout();
            Assert.Multiple(() =>
            {
                Assert.That(workflow.UseDefaultCaptureSettings, Is.False);
                Assert.That(activeMonitor.IsChecked, Is.False, "With the override on, the controls show the workflow's own settings.");
                Assert.That(activeMonitor.IsEffectivelyEnabled, Is.True);
                Assert.That(annotate.IsChecked, Is.True);
            });

            activeMonitor.IsChecked = true;
            Assert.That(workflow.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void DefaultTaskSettingsPage_HasNoOverrideCheckboxes()
    {
        var vm = new FakeUiViewModelFactory().CreateDefaultTaskSettingsViewModel();
        var panel = new TaskSettingsPanel { DataContext = vm };
        var window = new Window { Width = 800, Height = 900, Content = panel };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.That(FindCheckBox(panel, "Override capture settings").IsVisible, Is.False);
            Assert.That(FindCheckBox(panel, "Active monitor only").IsEffectivelyEnabled, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    private static CheckBox FindCheckBox(Control panel, string content) =>
        panel.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, content));
}
