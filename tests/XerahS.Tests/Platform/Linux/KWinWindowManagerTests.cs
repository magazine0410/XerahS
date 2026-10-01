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
using System.Text.Json;
using NUnit.Framework;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Tests.Platform.Linux;

[TestFixture]
public class KWinWindowManagerTests
{
    private const string SnapshotJson = """
        {
          "active": "{bbbb}",
          "cursor": [2022, 660],
          "windows": [
            { "id": "{desk}", "caption": "", "resourceClass": "plasmashell", "pid": 10, "frame": [0, 0, 1920, 1080], "client": [0, 0, 1920, 1080],
              "managed": true, "desktopWindow": true, "onCurrentDesktop": true, "onCurrentActivity": true },
            { "id": "{aaaa}", "caption": "Sheets — Dolphin", "resourceClass": "org.kde.dolphin", "pid": 20, "frame": [256, 64.58333, 1488, 950], "client": [256, 92.58333, 1488, 922],
              "managed": true, "onCurrentDesktop": true, "onCurrentActivity": true },
            { "id": "{bbbb}", "caption": "Brave", "resourceClass": "brave-browser", "pid": 30, "frame": [0, 0, 1920, 1036], "client": [0, 0, 1920, 1036],
              "managed": true, "maximized": true, "onCurrentDesktop": true, "onCurrentActivity": true },
            { "id": "{cccc}", "caption": "Other desktop", "resourceClass": "kate", "pid": 40, "frame": [0, 0, 800, 600], "client": [0, 0, 800, 600],
              "managed": true, "onCurrentDesktop": false, "onCurrentActivity": true },
            { "id": "{dddd}", "caption": "XerahS Region Capture Overlay", "resourceClass": "xerahs", "pid": 50, "frame": [0, 0, 1920, 1080], "client": [0, 0, 1920, 1080],
              "managed": true, "onCurrentDesktop": true, "onCurrentActivity": true },
            { "id": "{eeee}", "caption": "Minimized", "resourceClass": "konsole", "pid": 60, "frame": [100, 100, 640, 480], "client": [100, 100, 640, 480],
              "managed": true, "minimized": true, "onCurrentDesktop": true, "onCurrentActivity": true },
            { "id": "{ffff}", "caption": "Panel", "resourceClass": "plasmashell", "pid": 10, "frame": [0, 1036, 1920, 44], "client": [0, 1036, 1920, 44],
              "managed": true, "dock": true, "onCurrentDesktop": true, "onCurrentActivity": true },
            { "id": "{bad}", "caption": "Bad", "frame": [0, 0, "x", 10], "client": [0, 0, 10, 10] }
          ]
        }
        """;

    private static KWinSnapshot Parse()
    {
        using var document = JsonDocument.Parse(SnapshotJson);
        return KWinWindowManager.ParseSnapshot(document.RootElement)!;
    }

    [Test]
    public void ParseSnapshot_ReadsTheStackActiveWindowAndCursor()
    {
        KWinSnapshot snapshot = Parse();

        Assert.That(snapshot.ActiveWindowId, Is.EqualTo("{bbbb}"));
        Assert.That(snapshot.CursorPosition, Is.EqualTo(new Point(2022, 660)));
        Assert.That(snapshot.Windows.Select(w => w.Id), Is.EqualTo(new[] { "{desk}", "{aaaa}", "{bbbb}", "{cccc}", "{dddd}", "{eeee}", "{ffff}" }),
            "Malformed geometry is skipped.");

        KWinWindow dolphin = snapshot.Windows[1];
        Assert.That(dolphin.FrameGeometry, Is.EqualTo(new Rectangle(256, 65, 1488, 950)), "Fractional edges are rounded.");
        Assert.That(dolphin.ClientGeometry, Is.EqualTo(new Rectangle(256, 93, 1488, 922)));
        Assert.That(dolphin.ProcessId, Is.EqualTo(20u));
        Assert.That(snapshot.Windows[2].Maximized, Is.True);
    }

    [Test]
    public void ParseSnapshot_ListsTheWindowsTheX11EnumerationWouldList()
    {
        var listed = Parse().Windows.Where(w => w.Listed).Select(w => w.Caption);

        // The desktop, panels, other desktops' windows, and the region capture overlay are left out, as
        // on X11. Minimized windows stay listed (flagged), so custom window capture can restore them.
        Assert.That(listed, Is.EqualTo(new[] { "Sheets — Dolphin", "Brave", "Minimized" }));
    }

    [Test]
    public void ParseSnapshot_RejectsAnUnexpectedReport()
    {
        using var error = JsonDocument.Parse("""{ "error": "ReferenceError" }""");
        Assert.That(KWinWindowManager.ParseSnapshot(error.RootElement), Is.Null);
    }

    [Test]
    public void FindWindowAt_ReturnsTheTopmostVisibleListedWindow()
    {
        KWinSnapshot snapshot = Parse();

        Assert.That(KWinWindowManager.FindWindowAt(snapshot, new Point(300, 300))?.Caption, Is.EqualTo("Brave"),
            "The overlay above it and the minimized window are skipped.");
        Assert.That(KWinWindowManager.FindWindowAt(snapshot, new Point(300, 1050)), Is.Null, "The panel is not listed.");
        Assert.That(KWinWindowManager.FindWindowAt(null, new Point(1, 1)), Is.Null);
    }

    [Test]
    public void Handles_AreStablePerWindowAndOutsideTheX11Range()
    {
        nint first = KWinWindowManager.GetHandle("{handle-test-1}");

        Assert.That(KWinWindowManager.GetHandle("{handle-test-1}"), Is.EqualTo(first));
        Assert.That(KWinWindowManager.GetHandle("{handle-test-2}"), Is.Not.EqualTo(first));
        Assert.That((long)first, Is.GreaterThan(uint.MaxValue));
        Assert.That(KWinWindowManager.TryGetId(first, out string id) && id == "{handle-test-1}", Is.True);
        Assert.That(KWinWindowManager.TryGetId(0x1a00007, out _), Is.False, "X11 window IDs are not KWin handles.");
    }

    [Test]
    public void XwaylandScale_IsReadFromKwinrcAndAppliedToCoordinates()
    {
        Assert.That(KWinWindowManager.ParseXwaylandScale(["[Compositing]", "Scale=3", "[Xwayland]", "Scale=1.5"]), Is.EqualTo(1.5));
        Assert.That(KWinWindowManager.ParseXwaylandScale(["[Xwayland]", "Scale=nope"]), Is.EqualTo(1));
        Assert.That(KWinWindowManager.ParseXwaylandScale([]), Is.EqualTo(1));

        Assert.That(KWinWindowManager.ToX11(new Rectangle(10, 20, 100, 50), 1.5), Is.EqualTo(new Rectangle(15, 30, 150, 75)));
        Assert.That(KWinWindowManager.ToX11(new Point(3, 5), 2), Is.EqualTo(new Point(6, 10)));
    }

    [Test]
    public void BuildScript_QuotesTheCallbackValuesAsJavaScriptStrings()
    {
        string script = KWinScriptBridge.BuildScript(":1.42", "to\"ken", "report(1);");

        Assert.That(script, Does.Contain("callDBus(\":1.42\", \"/io/github/xerahs/KWinBridge\", \"io.github.xerahs.KWinBridge\", \"Report\", \"to\\u0022ken\""));
        Assert.That(script, Does.Contain("report(1);"));
    }

    [Test]
    public void SessionDetection_RequiresKdeOnWayland()
    {
        Assert.That(KWinScriptBridge.IsKdeWaylandSession(name => name switch { "XDG_SESSION_TYPE" => "wayland", "XDG_CURRENT_DESKTOP" => "KDE", _ => null }), Is.True);
        Assert.That(KWinScriptBridge.IsKdeWaylandSession(name => name switch { "XDG_SESSION_TYPE" => "x11", "XDG_CURRENT_DESKTOP" => "KDE", _ => null }), Is.False);
        Assert.That(KWinScriptBridge.IsKdeWaylandSession(name => name switch { "XDG_SESSION_TYPE" => "wayland", "XDG_CURRENT_DESKTOP" => "GNOME", _ => null }), Is.False);
    }

    /// <summary>Reads the window list from the running KWin. Read-only; run by hand on KDE Plasma Wayland.</summary>
    [Test, Explicit("Needs a KDE Plasma Wayland session")]
    public void LiveSession_ReportsWindowsAndCursor()
    {
        if (KWinWindowManager.Shared is not { } kwin)
        {
            Assert.Ignore("KWin scripting is not available.");
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        KWinSnapshot? snapshot = kwin.GetSnapshot();
        stopwatch.Stop();

        Assert.That(snapshot, Is.Not.Null);
        TestContext.Out.WriteLine($"Snapshot in {stopwatch.ElapsedMilliseconds} ms, cursor {snapshot!.CursorPosition}, active {snapshot.ActiveWindowId}");
        foreach (WindowInfo window in new LinuxWindowService().GetAllWindows())
            TestContext.Out.WriteLine($"{window.Handle:X} {window.Bounds} min={window.IsMinimized} max={window.IsMaximized} {window.ClassName}: {window.Title}");
        Assert.That(snapshot.Windows.Any(w => w.Listed), Is.True);
    }
}
