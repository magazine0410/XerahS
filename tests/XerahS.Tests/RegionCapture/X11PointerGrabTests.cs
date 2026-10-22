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
using XerahS.RegionCapture.Platform.Linux;

namespace XerahS.Tests.RegionCapture;

[TestFixture]
public class X11PointerGrabTests
{
    /// <summary>
    /// The grab needs the X connection Avalonia's X11 backend keeps in the internal field X11Window._x11
    /// (X11Info.Display). This guards those names across Avalonia updates.
    /// </summary>
    [Test]
    public void AvaloniasX11Window_StillHoldsTheDisplayWhereTheGrabReadsIt()
    {
        var assembly = typeof(global::Avalonia.X11PlatformOptions).Assembly;
        var windowType = assembly.GetType("Avalonia.X11.X11Window");
        Assert.That(windowType, Is.Not.Null, "Avalonia.X11.X11Window was not found; X11PointerGrab must follow Avalonia.");

        Assert.That(X11PointerGrab.TryResolve(windowType!, out var field, out var property), Is.True,
            $"X11Window.{X11PointerGrab.X11InfoFieldName} or X11Info.{X11PointerGrab.DisplayPropertyName} changed; X11PointerGrab must follow Avalonia.");
        Assert.That(field!.FieldType.FullName, Is.EqualTo("Avalonia.X11.X11Info"));
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(IntPtr)));
    }

    [Test]
    public void TryGetDisplay_ReadsTheConnectionFromThePrivateField()
    {
        Assert.That(X11PointerGrab.TryGetDisplay(new FakeWindowImpl(new IntPtr(42)), out IntPtr display), Is.True);
        Assert.That(display, Is.EqualTo(new IntPtr(42)));

        Assert.That(X11PointerGrab.TryGetDisplay(new FakeWindowImpl(IntPtr.Zero), out _), Is.False, "A closed connection is no connection.");
        Assert.That(X11PointerGrab.TryGetDisplay(new object(), out _), Is.False, "Another backend has no X connection.");
        Assert.That(X11PointerGrab.TryGetDisplay(new FakeWindowImpl(new IntPtr(7)), out display), Is.True, "The lookup is cached per type and still works.");
        Assert.That(display, Is.EqualTo(new IntPtr(7)));
    }

    [Test]
    public void DescribeStatus_NamesTheXServersReasons()
    {
        Assert.That(X11PointerGrab.DescribeStatus(X11PointerGrab.GrabSuccess), Is.EqualTo("success"));
        Assert.That(X11PointerGrab.DescribeStatus(X11PointerGrab.AlreadyGrabbed), Is.EqualTo("another client holds the pointer"));
        Assert.That(X11PointerGrab.DescribeStatus(X11PointerGrab.GrabNotViewable), Is.EqualTo("the overlay window is not viewable yet"));
        Assert.That(X11PointerGrab.DescribeStatus(X11PointerGrab.GrabFrozen), Is.EqualTo("the pointer is frozen by another grab"));
        Assert.That(X11PointerGrab.DescribeStatus(9), Is.EqualTo("status 9"));
    }

    private sealed class FakeInfo(IntPtr display)
    {
        public IntPtr Display { get; } = display;
    }

    private sealed class FakeWindowImpl(IntPtr display)
    {
        private readonly FakeInfo _x11 = new(display);

        public override string ToString() => _x11.Display.ToString();
    }
}
