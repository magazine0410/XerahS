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

using System.Drawing;
using Point = System.Drawing.Point;
using Avalonia;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.Services;

[TestFixture]
[NonParallelizable]
public class ScrollingCaptureLinuxTests
{
    [Test]
    public void LinuxService_UsesXTestOnX11_AndTheRemoteDesktopPortalOnWayland()
    {
        var x11 = new LinuxScrollingCaptureService(isWayland: () => false, hasRemoteDesktopPortal: () => false, hasXTest: () => true);
        var wayland = new LinuxScrollingCaptureService(isWayland: () => true, hasRemoteDesktopPortal: () => true, hasXTest: () => false);
        var noPortal = new LinuxScrollingCaptureService(isWayland: () => true, hasRemoteDesktopPortal: () => false, hasXTest: () => true);

        Assert.Multiple(() =>
        {
            Assert.That(x11.IsSupported, Is.True);
            Assert.That(wayland.IsSupported, Is.True);
            Assert.That(noPortal.IsSupported, Is.False, "On Wayland, Xwayland's XTEST does not reach other windows.");
            Assert.That(x11.SupportedScrollMethods, Is.EqualTo(new[] { ScrollMethod.MouseWheel, ScrollMethod.DownArrow, ScrollMethod.PageDown }),
                "Linux has no Windows scroll messages.");
        });
    }

    [Test]
    public async Task LinuxService_DoesNotBegin_WhenUnsupported()
    {
        var service = new LinuxScrollingCaptureService(isWayland: () => true, hasRemoteDesktopPortal: () => false, hasXTest: () => false);
        Assert.That(await service.BeginAsync(), Is.False);
        Assert.DoesNotThrowAsync(() => service.ScrollWindowAsync(IntPtr.Zero, ScrollMethod.MouseWheel, 2, new Point(5, 5)));
        Assert.That(service.GetScrollBarInfo(IntPtr.Zero), Is.Null);
    }

    [Test]
    public void PointerParking_PicksAPointOutsideTheAreaOnAScreen()
    {
        Rectangle[] screens = [new(0, 0, 1920, 1080), new(1920, 0, 1920, 1080)];

        Assert.Multiple(() =>
        {
            Assert.That(LinuxScrollingCaptureService.GetPointOutside(new Rectangle(100, 100, 400, 600), screens), Is.EqualTo(new Point(532, 400)),
                "To the right of the area first.");
            Assert.That(LinuxScrollingCaptureService.GetPointOutside(new Rectangle(3500, 100, 340, 600), screens), Is.EqualTo(new Point(3467, 400)),
                "To the left when the right is off the screens.");
            Assert.That(LinuxScrollingCaptureService.GetPointOutside(new Rectangle(1920, 0, 1920, 1000), screens), Is.EqualTo(new Point(1887, 500)),
                "The other monitor counts.");
            Assert.That(LinuxScrollingCaptureService.GetPointOutside(new Rectangle(0, 0, 3840, 1080), screens), Is.Null,
                "Nowhere outside a full-screen area.");
        });
    }

    [Test]
    public void NotificationPopups_CoverTheAreaOnlyWhereTheyOverlapIt()
    {
        // KDE's "Remote control session started" popup at the bottom right of a 1920x1080 screen.
        Rectangle[] popups = [new(1552, 885, 330, 110)];

        Assert.Multiple(() =>
        {
            Assert.That(LinuxScrollingCaptureService.CoversArea(popups, new Rectangle(0, 0, 1920, 1036), 1), Is.True);
            Assert.That(LinuxScrollingCaptureService.CoversArea(popups, new Rectangle(190, 280, 1200, 700), 1), Is.False);
            Assert.That(LinuxScrollingCaptureService.CoversArea(popups, new Rectangle(3200, 1800, 400, 300), 2), Is.True,
                "Popup geometry is converted to XerahS's coordinates first.");
            Assert.That(LinuxScrollingCaptureService.CoversArea([], new Rectangle(0, 0, 100, 100), 1), Is.False);
        });
    }

    [Test]
    public void FindWindowUnder_PicksTheTopmostVisibleWindowAtTheMiddleOfTheArea()
    {
        WindowInfo[] windows =
        [
            new() { Handle = 1, Bounds = new Rectangle(0, 0, 100, 100), IsVisible = true, IsMinimized = true },
            new() { Handle = 2, Bounds = new Rectangle(500, 500, 100, 100), IsVisible = true },
            new() { Handle = 3, Bounds = new Rectangle(0, 0, 400, 400), IsVisible = true },
            new() { Handle = 4, Bounds = new Rectangle(0, 0, 1920, 1080), IsVisible = true }
        ];

        Assert.Multiple(() =>
        {
            Assert.That(ScrollingCaptureToolService.FindWindowUnder(windows, new Rectangle(10, 10, 80, 80)), Is.EqualTo((IntPtr)3),
                "The minimized window is skipped; the next one at the middle is topmost.");
            Assert.That(ScrollingCaptureToolService.FindWindowUnder(windows, new Rectangle(1000, 900, 50, 50)), Is.EqualTo((IntPtr)4));
            Assert.That(ScrollingCaptureToolService.FindWindowUnder([], new Rectangle(0, 0, 10, 10)), Is.EqualTo(IntPtr.Zero));
        });
    }

    [Test]
    public void Options_AreEditedInThePanel_AndSavedOnOK()
    {
        var options = new ScrollingCaptureOptions { ScrollMethod = ScrollMethod.ScrollMessage, ScrollAmount = 3 };
        int saves = 0;
        var vm = new ScrollingCaptureViewModel(options, [ScrollMethod.MouseWheel, ScrollMethod.DownArrow, ScrollMethod.PageDown])
        {
            SaveOptionsRequested = () => saves++
        };

        vm.OpenOptionsCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsOptionsOpen, Is.True);
            Assert.That(vm.ScrollMethod, Is.EqualTo(ScrollMethod.MouseWheel), "A method this platform cannot perform shows as the mouse wheel.");
            Assert.That(vm.StartDelay, Is.EqualTo(300));
            Assert.That(vm.ShowRegion, Is.True);
        });

        vm.ScrollMethod = ScrollMethod.PageDown;
        Assert.That(vm.IsScrollAmountVisible, Is.False, "As in ShareX, Page down has no amount.");
        vm.StartDelay = 800;
        vm.AutoUpload = true;
        vm.CancelOptionsCommand.Execute(null);
        Assert.That(options.StartDelay, Is.EqualTo(300), "Cancel keeps the options.");

        vm.OpenOptionsCommand.Execute(null);
        vm.StartDelay = 800;
        vm.ScrollMethod = ScrollMethod.DownArrow;
        vm.AutoUpload = true;
        vm.SaveOptionsCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(options.StartDelay, Is.EqualTo(800));
            Assert.That(options.ScrollMethod, Is.EqualTo(ScrollMethod.DownArrow));
            Assert.That(options.AutoUpload, Is.True);
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(vm.IsOptionsOpen, Is.False);
        });
    }

    [Test]
    public async Task StartStop_ReportsAnUnsupportedPlatform()
    {
        var vm = new ScrollingCaptureViewModel();
        bool? minimized = null;
        vm.SetMinimizedRequested = value => minimized = value;

        PlatformServices.Reset();
        await vm.StartStopAsync();

        Assert.That(vm.StatusText, Is.EqualTo(ScrollingCaptureViewModel.UnsupportedText));
        Assert.That(minimized, Is.Null, "Nothing is selected, so the window stays as it is.");
    }

    [Test]
    public void CaptureSettingsReference_IsTheDefaults_UnlessTheWorkflowOverridesCapture()
    {
        var inheriting = new TaskSettings();
        var overriding = new TaskSettings { UseDefaultCaptureSettings = false };

        Assert.That(inheriting.CaptureSettingsReference, Is.SameAs(SettingsManager.DefaultTaskSettings.CaptureSettings));
        Assert.That(overriding.CaptureSettingsReference, Is.SameAs(overriding.CaptureSettings));
        Assert.That(TaskSettings.GetSafeTaskSettings(overriding).CaptureSettingsReference, Is.SameAs(overriding.CaptureSettings),
            "A running task edits its workflow's settings, not its copy.");
    }

    [AvaloniaTest]
    public void RegionBorder_SitsOnePixelOutsideTheArea()
    {
        var window = new ScrollingCaptureRegionWindow(new Rectangle(100, 200, 300, 150));
        try
        {
            window.Show();
            Assert.Multiple(() =>
            {
                Assert.That(window.Position, Is.EqualTo(new PixelPoint(99, 199)));
                Assert.That(window.Width, Is.EqualTo(302));
                Assert.That(window.Height, Is.EqualTo(152));
                Assert.That(window.ShowActivated, Is.False);
                Assert.That(window.Topmost, Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }
}
