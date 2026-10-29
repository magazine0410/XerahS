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
using XerahS.RegionCapture.Services;

namespace XerahS.Tests.RegionCapture;

[TestFixture]
public class MonitorEnumerationServiceTests
{
    [TestCase("KDE", true)]
    [TestCase("ubuntu:KDE", true)]
    [TestCase("plasmawayland", true)]
    [TestCase("GNOME", false)]
    [TestCase("sway", false)]
    public void IsKdeDesktop_ReadsXdgCurrentDesktop(string desktop, bool expected)
    {
        var environment = new Dictionary<string, string?> { ["XDG_CURRENT_DESKTOP"] = desktop };

        Assert.That(MonitorEnumerationService.IsKdeDesktop(name => environment.GetValueOrDefault(name)), Is.EqualTo(expected));
    }

    [Test]
    public void IsKdeDesktop_FallsBackToTheSessionVariablesOnlyWhenXdgCurrentDesktopIsUnset()
    {
        var sessionOnly = new Dictionary<string, string?> { ["DESKTOP_SESSION"] = "plasma" };
        var gnomeFirst = new Dictionary<string, string?> { ["XDG_CURRENT_DESKTOP"] = "GNOME", ["DESKTOP_SESSION"] = "plasma" };

        Assert.Multiple(() =>
        {
            Assert.That(MonitorEnumerationService.IsKdeDesktop(name => sessionOnly.GetValueOrDefault(name)), Is.True);
            Assert.That(MonitorEnumerationService.IsKdeDesktop(name => gnomeFirst.GetValueOrDefault(name)), Is.False);
            Assert.That(MonitorEnumerationService.IsKdeDesktop(_ => null), Is.False);
        });
    }
}
