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

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.RegionCapture;
using XerahS.RegionCapture.Models;
using XerahS.RegionCapture.UI;
using XerahS.UI.Services;
using CaptureMonitor = XerahS.RegionCapture.Models.MonitorInfo;
using CaptureRect = XerahS.RegionCapture.Models.PixelRect;
using CapturePoint = XerahS.RegionCapture.Models.PixelPoint;
using DrawingColor = System.Drawing.Color;
using DrawingPoint = System.Drawing.Point;

namespace XerahS.Tests.RegionCapture;

[TestFixture]
[NonParallelizable]
public class ScreenColorPickerTests
{
    [Test]
    public void Defaults_MatchShareX()
    {
        var tools = new TaskSettingsTools();
        Assert.That(tools.ScreenColorPickerFormat, Is.EqualTo("$HEX"));
        Assert.That(tools.ScreenColorPickerFormatCtrl, Is.EqualTo("$r255, $g255, $b255"));
        Assert.That(tools.ScreenColorPickerInfoText, Is.EqualTo("#$HEX"));
        Assert.That(tools.ScreenColorPickerShowMagnifier, Is.True);
    }

    [Test]
    public void ClipboardText_UsesTheCtrlFormatForACtrlClick_AndNothingForAnEmptyFormat()
    {
        var tools = new TaskSettingsTools();
        var color = DrawingColor.FromArgb(255, 255, 136, 0);
        var position = new DrawingPoint(12, 34);

        Assert.That(ColorPickerService.GetClipboardText(tools, color, position, useCtrlFormat: false), Is.EqualTo("FF8800"));
        Assert.That(ColorPickerService.GetClipboardText(tools, color, position, useCtrlFormat: true), Is.EqualTo("255, 136, 0"));

        tools.ScreenColorPickerFormat = "";
        Assert.That(ColorPickerService.GetClipboardText(tools, color, position, useCtrlFormat: false), Is.Empty);
    }

    [AvaloniaTest]
    public void PickerOptions_UseThePickersInfoTextAndMagnifierSwitch()
    {
        var tools = new TaskSettingsTools { ScreenColorPickerInfoText = "#$HEX at $x, $y", ScreenColorPickerShowMagnifier = false };
        var options = ColorPickerToolService.CreatePickerOptions(tools, new XerahS.Core.RegionCaptureOptions { ShowMagnifier = true }, null);
        Assert.That(options.Mode, Is.EqualTo(RegionCaptureMode.ScreenColorPicker));
        Assert.That(options.EnableMagnifier, Is.False, "The picker's own switch decides, not the region capture one.");
        Assert.That(options.ShowInfo, Is.True);
        Assert.That(options.CustomInfoFormat, Is.EqualTo("#$HEX at $x, $y"));

        tools.ScreenColorPickerInfoText = "";
        Assert.That(ColorPickerToolService.CreatePickerOptions(tools, null, null).ShowInfo, Is.False);
    }

    [AvaloniaTest]
    public void InfoText_NextToTheCursor_UsesThePickerFormat()
    {
        using var background = new SKBitmap(64, 64);
        background.Erase(new SKColor(255, 136, 0));
        var monitor = new CaptureMonitor("Display", new CaptureRect(0, 0, 64, 64), new CaptureRect(0, 0, 64, 64), 1, true);
        var tools = new TaskSettingsTools { ScreenColorPickerShowMagnifier = false };
        var control = new RegionCaptureControl(monitor, ColorPickerToolService.CreatePickerOptions(tools, null, background));

        control.MagnifierForTests.UpdateFromBackground(new CapturePoint(10, 10), background, new CaptureRect(0, 0, 64, 64));

        Assert.That(control.MagnifierForTests.InfoTextForTests, Is.EqualTo("#FF8800"));
        Assert.That(control.MagnifierForTests.MagnifierViewVisibleForTests, Is.False);
    }

    [AvaloniaTest]
    public void Click_ReportsWhetherCtrlWasHeld()
    {
        Assert.That(Pick(RawInputModifiers.Control)?.ControlPressed, Is.True);
        Assert.That(Pick(RawInputModifiers.None)?.ControlPressed, Is.False);
    }

    private static RegionSelectionResult? Pick(RawInputModifiers modifiers)
    {
        var bounds = new CaptureRect(0, 0, 640, 480);
        var completion = new TaskCompletionSource<RegionSelectionResult?>();
        var window = new OverlayWindow(new CaptureMonitor("Display", bounds, bounds, 1, true), completion, options: new XerahS.RegionCapture.RegionCaptureOptions
        {
            Mode = RegionCaptureMode.ScreenColorPicker,
            CaptureBounds = bounds,
            EnableWindowSnapping = false,
            EnableMagnifier = false,
            ShowInfo = false,
            UseTransparentOverlay = true
        });
        try
        {
            window.Show();
            window.UpdateLayout();
            window.MouseDown(new Point(100, 120), MouseButton.Left, modifiers);
            window.MouseUp(new Point(100, 120), MouseButton.Left, modifiers);
            Assert.That(completion.Task.IsCompleted, Is.True);
            Assert.That(completion.Task.Result!.Value.CursorPosition, Is.EqualTo(new CapturePoint(100, 120)));
            return completion.Task.Result;
        }
        finally { window.Close(); }
    }
}
