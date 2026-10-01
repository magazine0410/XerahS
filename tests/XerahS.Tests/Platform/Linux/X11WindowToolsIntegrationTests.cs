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

using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux;
using XerahS.Platform.Linux.Services;

namespace XerahS.Tests.Platform.Linux;

/// <summary>Run only in an isolated X server: these tests synthesize pointer events.</summary>
[TestFixture, NonParallelizable, Explicit("Requires XERAHS_ISOLATED_X11_TEST=1 and an isolated EWMH window manager.")]
public class X11WindowToolsIntegrationTests
{
    [Test]
    public void NativeWindowTools_RoundTripPropertiesAndGeometry_AndObserveGlobalClicks()
    {
        Assert.That(Environment.GetEnvironmentVariable("XERAHS_ISOLATED_X11_TEST"), Is.EqualTo("1"));
        Assert.That(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), Is.EqualTo("x11"));
        IntPtr display = XOpenDisplay(null);
        Assert.That(display, Is.Not.EqualTo(IntPtr.Zero));
        IntPtr root = XDefaultRootWindow(display);
        IntPtr window = XCreateSimpleWindow(display, root, 100, 100, 400, 240, 0, 0, 0x304050);
        using var service = new LinuxWindowService();
        PlatformServices.Screen = DispatchProxy.Create<IScreenService, ScreenProxy>();
        try
        {
            XStoreName(display, window, "XerahS native tool test");
            SetProperty("_NET_WM_PID", "CARDINAL", [new IntPtr(Environment.ProcessId)]);
            SetProperty("_NET_WM_ICON", "CARDINAL", [new IntPtr(1), new IntPtr(1), new IntPtr(0xffaabbccL)]);
            XMapWindow(display, window);
            XSync(display, false);
            WaitFor(() => service.GetAllWindows().Any(w => w.Handle == window), "Window should be enumerated once mapped.");
            Assert.That(service.GetWindowDetails(window)!.ProcessId, Is.EqualTo((uint)Environment.ProcessId));
            Assert.That(service.GetWindowDetails(window)!.ProcessName, Is.Not.Empty);
            Assert.That(service.GetWindowIcon(window), Is.Not.Empty);
            Rectangle original = service.GetWindowBounds(window);
            TestContext.Out.WriteLine($"Original geometry: {original}");
            Assert.That(service.GetWindowAtPoint(new Point(original.X + 100, original.Y + 100)), Is.EqualTo(window));
            Assert.That(service.SetWindowTopmost(window, true), Is.True);
            WaitFor(() => service.GetWindowDetails(window)!.IsTopmost, "Topmost state should change.");
            Assert.That(service.SetWindowOpacity(window, 128), Is.True);
            WaitFor(() => service.GetWindowDetails(window)!.Opacity == 128, "Opacity should change.");
            Assert.That(service.ToggleBorderlessWindow(window), Is.True);
            WaitFor(() => service.GetWindowBounds(window) == new Rectangle(0, 0, 1024, 768), "Borderless window should fill the monitor.");
            Assert.That(service.ToggleBorderlessWindow(window), Is.True);
            WaitFor(() => service.GetWindowBounds(window) == original, $"Toggling again should restore {original}.", () => service.GetWindowBounds(window));

            IInputService input = new LinuxInputService();
            Assert.That(input.SupportsGlobalMouseMonitoring, Is.True);
            var buffer = new MouseHighlighterInputBuffer(Point.Empty);
            using (var monitor = input.CreateGlobalMouseMonitor(buffer))
            {
                XTestFakeMotionEvent(display, 0, original.X + 100, original.Y + 100, 0);
                XTestFakeButtonEvent(display, 1, true, 0);
                XTestFakeButtonEvent(display, 1, false, 0);
                XSync(display, false);
                var events = new List<MouseHighlighterButtonEvent>();
                WaitFor(() => { while (buffer.TryRead(out var e)) events.Add(e); return events.Count >= 2; }, "Global click listener should receive press and release.");
                Assert.That(events.Select(e => e.Pressed), Is.EqualTo(new[] { true, false }));
                Assert.That(events.All(e => e.Button == MouseHighlightButton.Primary), Is.True);
                Assert.That(monitor.Failure, Is.Null);
            }
            Assert.That(service.SetWindowClickThrough(window), Is.True);
            WaitFor(() =>
            {
                IntPtr rectangles = XShapeGetRectangles(display, window, 2, out int count, out _);
                if (rectangles != IntPtr.Zero) XFree(rectangles);
                return count == 0;
            }, "Click-through must set an empty input region.");
        }
        finally { XDestroyWindow(display, window); XCloseDisplay(display); PlatformServices.Reset(); }

        void SetProperty(string property, string type, IntPtr[] values) =>
            XChangeProperty(display, window, XInternAtom(display, property, false), XInternAtom(display, type, false), 32, 0, values, values.Length);
    }

    private static void WaitFor(Func<bool> condition, string message, Func<object>? actual = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (condition()) return;
            Thread.Sleep(20);
        }
        Assert.Fail(actual == null ? message : $"{message} Actual: {actual()}");
    }

    public class ScreenProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            new ScreenInfo { Bounds = new Rectangle(0, 0, 1024, 768), WorkingArea = new Rectangle(0, 0, 1024, 728) };
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);
    [DllImport("libX11.so.6")] private static extern int XStoreName(IntPtr display, IntPtr window, string name);
    [DllImport("libX11.so.6")] private static extern int XMapWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] private static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(IntPtr display, string name, bool exists);
    [DllImport("libX11.so.6")] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int count);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
    [DllImport("libXext.so.6")] private static extern IntPtr XShapeGetRectangles(IntPtr display, IntPtr window, int kind, out int count, out int ordering);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, bool pressed, nuint delay);
}
