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

using NUnit.Framework;
using XerahS.Core;
using XerahS.Core.Capture;
using XerahS.Core.Hotkeys;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services.Capture;
using OverlayAction = XerahS.RegionCapture.RegionCaptureAction;

namespace XerahS.Tests.Services;

[TestFixture]
[NonParallelizable]
public class OverlayRegionCaptureOptionsTests
{
    private WorkflowsConfig _previousWorkflows = null!;
    private TaskSettingsCapture _previousCaptureSettings = null!;
    private TaskSettingsAdvanced _previousAdvancedSettings = null!;
    private System.Drawing.Rectangle _previousLastRegion;

    [SetUp]
    public void SetUp()
    {
        _previousWorkflows = SettingsManager.WorkflowsConfig;
        _previousCaptureSettings = SettingsManager.DefaultTaskSettings.CaptureSettings;
        _previousAdvancedSettings = SettingsManager.DefaultTaskSettings.AdvancedSettings;
        LastRegionStore.TryGet(out _previousLastRegion);
        SettingsManager.WorkflowsConfig = new WorkflowsConfig();
        SettingsManager.DefaultTaskSettings.CaptureSettings = new TaskSettingsCapture();
        SettingsManager.DefaultTaskSettings.AdvancedSettings = new TaskSettingsAdvanced();
        LastRegionStore.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        SettingsManager.WorkflowsConfig = _previousWorkflows;
        SettingsManager.DefaultTaskSettings.CaptureSettings = _previousCaptureSettings;
        SettingsManager.DefaultTaskSettings.AdvancedSettings = _previousAdvancedSettings;
        LastRegionStore.Set(_previousLastRegion);
    }

    [Test]
    public void WorkflowOptions_OverrideDefaults_AndCarryLastRegion()
    {
        var settings = new TaskSettings();
        var region = settings.CaptureSettings.RegionCaptureOptions;
        region.ActiveMonitorMode = true;
        region.BackgroundDimStrength = 65;
        region.QuickCrop = false;
        region.ShowCenterCrosshair = false;
        region.RegionCaptureActionRightClick = RegionCaptureAction.None;
        region.RegionCaptureActionMiddleClick = RegionCaptureAction.CaptureLastRegion;
        region.RegionCaptureActionX1Click = RegionCaptureAction.RemoveShape;
        region.RegionCaptureActionX2Click = RegionCaptureAction.CancelCapture;
        settings.AdvancedSettings.RegionCaptureDisableAnnotation = true;
        SettingsManager.WorkflowsConfig.Hotkeys.Add(new WorkflowSettings { Id = "region-options-test", TaskSettings = settings });
        LastRegionStore.Set(-500, 150, 320, 180);

        var options = OverlayRegionCaptureSession.CreateOverlayOptions(
            new CaptureOptions { WorkflowId = "region-options-test" }, null, false, null);

        Assert.Multiple(() =>
        {
            Assert.That(options.ActiveMonitorMode, Is.True);
            Assert.That(options.DimOpacity, Is.EqualTo(0.65));
            Assert.That(options.QuickCrop, Is.False);
            Assert.That(options.ShowCenterCrosshair, Is.False);
            Assert.That(options.EnableAnnotations, Is.False);
            Assert.That(options.RightClickAction, Is.EqualTo(OverlayAction.None));
            Assert.That(options.MiddleClickAction, Is.EqualTo(OverlayAction.CaptureLastRegion));
            Assert.That(options.X1ClickAction, Is.EqualTo(OverlayAction.RemoveShape));
            Assert.That(options.X2ClickAction, Is.EqualTo(OverlayAction.CancelCapture));
            Assert.That(options.LastRegion, Is.EqualTo(new XerahS.RegionCapture.Models.PixelRect(-500, 150, 320, 180)));
        });
    }

    [Test]
    public void MissingWorkflow_UsesDefaultsAndShareXMouseAssignments()
    {
        var options = OverlayRegionCaptureSession.CreateOverlayOptions(
            new CaptureOptions { WorkflowId = "missing" }, null, true, null);

        Assert.Multiple(() =>
        {
            Assert.That(options.ActiveMonitorMode, Is.False);
            Assert.That(options.DimOpacity, Is.EqualTo(0.2));
            Assert.That(options.QuickCrop, Is.True);
            Assert.That(options.ShowCenterCrosshair, Is.True);
            Assert.That(options.EnableAnnotations, Is.True);
            Assert.That(options.RightClickAction, Is.EqualTo(OverlayAction.RemoveShapeCancelCapture));
            Assert.That(options.MiddleClickAction, Is.EqualTo(OverlayAction.SwapToolType));
            Assert.That(options.X1ClickAction, Is.EqualTo(OverlayAction.CaptureFullscreen));
            Assert.That(options.X2ClickAction, Is.EqualTo(OverlayAction.CaptureActiveMonitor));
            Assert.That(options.LastRegion.IsEmpty, Is.True);
        });
    }

    [TestCase(true, -10, 0)]
    [TestCase(true, 180, 1)]
    [TestCase(false, 65, 0)]
    public void BackgroundDimming_ClampsStrengthAndHonorsDisabled(bool enabled, int strength, double expected)
    {
        var region = SettingsManager.DefaultTaskSettings.CaptureSettings.RegionCaptureOptions;
        region.UseDimming = enabled;
        region.BackgroundDimStrength = strength;

        var options = OverlayRegionCaptureSession.CreateOverlayOptions(null, null, false, null);

        Assert.That(options.DimOpacity, Is.EqualTo(expected));
    }
}
