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
using NUnit.Framework;
using XerahS.Platform.Abstractions;
using XerahS.UI;

namespace XerahS.Tests.UI;

[TestFixture]
public sealed class TrayWindowListTests
{
    [Test]
    public void ReadWindowList_ReadsIconsOnlyForNewWindows_AndListsCapturableOnes()
    {
        WindowInfo[] windows =
        [
            new() { Handle = 1, Title = "Editor", IsVisible = true, Bounds = new Rectangle(0, 0, 800, 600) },
            new() { Handle = 2, Title = "Browser", IsVisible = true, Bounds = new Rectangle(0, 0, 800, 600) },
            new() { Handle = 3, Title = "Minimized", IsVisible = true, IsMinimized = true, Bounds = new Rectangle(0, 0, 800, 600) },
            new() { Handle = 4, Title = "", IsVisible = true, Bounds = new Rectangle(0, 0, 800, 600) }
        ];
        ScreenInfo[] screens = [new() { Bounds = new Rectangle(0, 0, 1920, 1080) }];
        var iconRequests = new List<IntPtr>();

        TrayIconHelper.TrayWindowList list = TrayIconHelper.ReadWindowList(() => windows, () => screens,
            handle => { iconRequests.Add(handle); return null; }, knownIcons: [1]);

        Assert.Multiple(() =>
        {
            Assert.That(list.Windows.Select(w => w.Handle), Is.EqualTo(new IntPtr[] { 1, 2 }), "ShareX's list: shown windows with a title.");
            Assert.That(iconRequests, Is.EqualTo(new IntPtr[] { 2 }), "A window whose icon was read before is not read again.");
            Assert.That(list.NewIcons.Keys, Is.EqualTo(new IntPtr[] { 2 }));
            Assert.That(list.Screens, Has.Length.EqualTo(1));
        });

        windows[1].Title = "Browser - another page";
        string changed = TrayIconHelper.ReadWindowList(() => windows, () => screens, _ => null, knownIcons: [1, 2]).Signature;
        Assert.That(changed, Is.Not.EqualTo(list.Signature), "A changed title rebuilds the menu.");
    }

    [Test]
    public void ReadWindowList_ReturnsAnEmptyList_WhenListingFails()
    {
        TrayIconHelper.TrayWindowList list = TrayIconHelper.ReadWindowList(() => throw new InvalidOperationException("No display."),
            () => [], _ => null, knownIcons: []);
        Assert.That(list.Windows, Is.Empty);
    }
}
