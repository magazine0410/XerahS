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
using XerahS.UI.Helpers;

namespace XerahS.Tests.UI;

[TestFixture]
public class ToolNavigationRegistryTests
{
    [Test]
    public void ScreenColorPicker_RoutesToTheScreenColorPickerJob()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToolNavigationRegistry.TryResolve("Tools_ScreenColorPicker", out var route), Is.True);
            Assert.That(route.WorkflowType, Is.EqualTo(WorkflowType.ScreenColorPicker));
            // Dispatched to the tool service directly, which copies the color with the Task Settings formats.
            Assert.That(route.DispatchMode, Is.EqualTo(ToolNavigationDispatchMode.DirectToolService));
            Assert.That(NavigationSearchKeywords.ForTag("Tools_ScreenColorPicker"), Does.Contain("screen color picker"));
        });
    }
}
