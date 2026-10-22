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
using NUnit.Framework;
using XerahS.RegionCapture.Services;
using CaptureRect = XerahS.RegionCapture.Models.PixelRect;

namespace XerahS.Tests.RegionCapture;

[TestFixture]
[NonParallelizable]
public class CursorConfinementServiceTests
{
    private static readonly CaptureRect Bounds = new(0, 0, 640, 480);

    [TearDown]
    public void Reset()
    {
        CursorConfinementService.ReleaseCurrent();
        CursorConfinementService.ConfineOverride = null;
    }

    [AvaloniaTest]
    public void TryConfine_ReleasesTheConfinementHeldBefore_SoOnlyOneExists()
    {
        var released = new List<int>();
        int count = 0;
        CursorConfinementService.ConfineOverride = (_, _) =>
        {
            int id = ++count;
            return new CursorConfinement(release: () => released.Add(id));
        };
        var overlay = new Window();

        var first = CursorConfinementService.TryConfine(overlay, Bounds);
        var second = CursorConfinementService.TryConfine(overlay, Bounds);

        Assert.That(first!.IsReleased, Is.True);
        Assert.That(released, Is.EqualTo(new[] { 1 }));
        Assert.That(CursorConfinementService.Current, Is.SameAs(second));

        CursorConfinementService.ReleaseCurrent();
        Assert.That(released, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(CursorConfinementService.Current, Is.Null);
        Assert.That(CursorConfinementService.IsSupported, Is.True);
    }

    [AvaloniaTest]
    public void TryConfine_ReturnsNull_WhenThePlatformRefusesOrFails()
    {
        CursorConfinementService.ConfineOverride = (_, _) => null;
        Assert.That(CursorConfinementService.TryConfine(new Window(), Bounds), Is.Null);
        Assert.That(CursorConfinementService.Current, Is.Null);

        CursorConfinementService.ConfineOverride = (_, _) => throw new InvalidOperationException("no display");
        Assert.That(CursorConfinementService.TryConfine(new Window(), Bounds), Is.Null);
        Assert.That(CursorConfinementService.Current, Is.Null);
    }

    [Test]
    public void Confinement_ReleasesOnce_AndStopsReassertingAfterRelease()
    {
        int released = 0, reasserted = 0;
        var confinement = new CursorConfinement(() => released++, () => reasserted++);

        confinement.Reassert();
        Assert.That(reasserted, Is.EqualTo(1));

        confinement.Release();
        confinement.Release();
        confinement.Reassert();
        Assert.That(released, Is.EqualTo(1));
        Assert.That(reasserted, Is.EqualTo(1));
        Assert.That(confinement.IsReleased, Is.True);
    }

    [Test]
    public void ConfinesReliably_OnlyOnWindows()
    {
        Assert.That(CursorConfinementService.ConfinesReliably, Is.EqualTo(OperatingSystem.IsWindows()));
    }
}
