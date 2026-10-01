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

using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;
using System.Text.Json;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>One KWin window as reported by the snapshot script. Geometry is in KWin's logical coordinates.</summary>
internal sealed record KWinWindow(
    string Id,
    string Caption,
    string ResourceClass,
    uint ProcessId,
    Rectangle FrameGeometry,
    Rectangle ClientGeometry,
    bool Minimized,
    bool Maximized,
    bool KeepAbove,
    bool NoBorder,
    bool Listed,
    bool IsNotification = false);

/// <summary>The window stack (bottom to top), the active window, and the pointer position.</summary>
internal sealed record KWinSnapshot(IReadOnlyList<KWinWindow> Windows, string? ActiveWindowId, Point CursorPosition);

/// <summary>
/// Window queries and actions on KDE Plasma Wayland through <see cref="KWinScriptBridge"/>. KWin sees
/// native Wayland and XWayland windows alike, so these replace the X11 calls, which only reach
/// XWayland windows there. Window IDs are KWin's internal UUIDs, mapped to stable handles.
/// </summary>
internal sealed class KWinWindowManager
{
    private static readonly TimeSpan SnapshotMaxAge = TimeSpan.FromMilliseconds(50);
    private static readonly Lazy<KWinWindowManager?> SharedManager = new(() =>
        KWinScriptBridge.Shared is { } bridge ? new KWinWindowManager(bridge) : null);

    // Handles start above the 32-bit X11 window ID range, so they never match an X11 window.
    private const long HandleBase = 0x4B57_0000_0000;
    private static readonly ConcurrentDictionary<string, nint> HandlesById = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<nint, string> IdsByHandle = new();
    private static long _lastHandle;

    private readonly KWinScriptBridge _bridge;
    private readonly object _snapshotLock = new();
    private KWinSnapshot? _snapshot;
    private DateTime _snapshotTime;

    private KWinWindowManager(KWinScriptBridge bridge)
    {
        _bridge = bridge;
    }

    /// <summary>The manager for a KDE Plasma Wayland session, or null when KWin scripting is unavailable.</summary>
    public static KWinWindowManager? Shared => SharedManager.Value;

    public static nint GetHandle(string id) => HandlesById.GetOrAdd(id, key =>
    {
        nint handle = (nint)(HandleBase + Interlocked.Increment(ref _lastHandle));
        IdsByHandle[handle] = key;
        return handle;
    });

    public static bool TryGetId(nint handle, out string id) => IdsByHandle.TryGetValue(handle, out id!);

    /// <summary>Returns a snapshot no older than 50 ms, or null when KWin does not answer.</summary>
    public KWinSnapshot? GetSnapshot()
    {
        lock (_snapshotLock)
        {
            if (_snapshot != null && DateTime.UtcNow - _snapshotTime < SnapshotMaxAge)
                return _snapshot;
        }

        using JsonDocument? result = _bridge.Run(SnapshotScript);
        KWinSnapshot? snapshot = result == null ? null : ParseSnapshot(result.RootElement);
        lock (_snapshotLock)
        {
            _snapshot = snapshot;
            _snapshotTime = DateTime.UtcNow;
        }

        return snapshot;
    }

    /// <summary>The pointer position now, in KWin's logical coordinates, without the snapshot cache.</summary>
    public Point? QueryCursorPosition()
    {
        using JsonDocument? result = _bridge.Run("report([workspace.cursorPos.x, workspace.cursorPos.y]);");
        if (result == null || result.RootElement.ValueKind != JsonValueKind.Array || result.RootElement.GetArrayLength() != 2 ||
            !result.RootElement[0].TryGetDouble(out double x) || !result.RootElement[1].TryGetDouble(out double y))
        {
            return null;
        }

        return new Point((int)Math.Round(x), (int)Math.Round(y));
    }

    /// <summary>Where notification popups and on-screen displays are now, in KWin's logical coordinates.</summary>
    public IReadOnlyList<Rectangle> GetNotificationBounds() =>
        GetSnapshot()?.Windows.Where(window => window.IsNotification && !window.Minimized).Select(window => window.FrameGeometry).ToArray() ?? [];

    public KWinWindow? GetWindow(nint handle) =>
        TryGetId(handle, out string id) ? GetSnapshot()?.Windows.FirstOrDefault(window => window.Id == id) : null;

    /// <summary>The topmost listed, visible window containing the point.</summary>
    public KWinWindow? GetWindowAt(Point logicalPoint) => FindWindowAt(GetSnapshot(), logicalPoint);

    internal static KWinWindow? FindWindowAt(KWinSnapshot? snapshot, Point logicalPoint) =>
        snapshot?.Windows.LastOrDefault(window => window.Listed && !window.Minimized && window.FrameGeometry.Contains(logicalPoint));

    public bool Activate(nint handle) => RunWindowAction(handle, "if (w.minimized) w.minimized = false; workspace.activeWindow = w;");

    public bool SetMinimized(nint handle, bool minimized) =>
        RunWindowAction(handle, minimized ? "w.minimized = true;" : "w.minimized = false;");

    public bool SetKeepAbove(nint handle, bool keepAbove) =>
        RunWindowAction(handle, keepAbove ? "w.keepAbove = true;" : "w.keepAbove = false;");

    public bool ToggleActiveWindowKeepAbove() => RunAction("""
            var w = workspace.activeWindow;
            if (!w) { report({ ok: false }); return; }
            w.keepAbove = !w.keepAbove;
            report({ ok: true });
        """);

    /// <summary>
    /// Removes the window's border and fills its monitor (or the monitor's working area), as ShareX's
    /// "Make active window borderless" does.
    /// </summary>
    public bool MakeBorderless(nint handle, bool useWorkingArea) => RunWindowAction(handle, $$"""
            var area = {{(useWorkingArea ? "workspace.clientArea(KWin.MaximizeArea, w)" : "w.output.geometry")}};
            w.setMaximize(false, false);
            w.noBorder = true;
            w.frameGeometry = { x: area.x, y: area.y, width: area.width, height: area.height };
        """);

    public bool RestoreBorder(nint handle, Rectangle frameGeometry, bool noBorder, bool maximized) => RunWindowAction(handle, $$"""
            w.noBorder = {{(noBorder ? "true" : "false")}};
            w.frameGeometry = { x: {{frameGeometry.X}}, y: {{frameGeometry.Y}}, width: {{frameGeometry.Width}}, height: {{frameGeometry.Height}} };
            if ({{(maximized ? "true" : "false")}}) w.setMaximize(true, true);
        """);

    private bool RunWindowAction(nint handle, string action)
    {
        if (!TryGetId(handle, out string id))
            return false;

        return RunAction($$"""
                var id = {{JsonSerializer.Serialize(id)}};
                var w = null;
                var stack = workspace.stackingOrder;
                for (var i = 0; i < stack.length; i++) {
                    if (stack[i].internalId.toString() === id) { w = stack[i]; break; }
                }
                if (!w) { report({ ok: false }); return; }
                {{action}}
                report({ ok: true });
            """);
    }

    private bool RunAction(string body)
    {
        using JsonDocument? result = _bridge.Run(body);
        lock (_snapshotLock)
        {
            _snapshot = null;
        }

        return result != null && result.RootElement.ValueKind == JsonValueKind.Object &&
            result.RootElement.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True;
    }

    // The body of an inner function, so "return" ends the script after report().
    private const string SnapshotScript = """
            function rect(g) { return [g.x, g.y, g.width, g.height]; }
            var desktop = workspace.currentDesktop;
            var activity = workspace.currentActivity;
            var stack = workspace.stackingOrder;
            var windows = [];
            for (var i = 0; i < stack.length; i++) {
                var w = stack[i];
                windows.push({
                    id: w.internalId.toString(),
                    caption: w.caption,
                    resourceClass: w.resourceClass,
                    pid: w.pid,
                    frame: rect(w.frameGeometry),
                    client: rect(w.clientGeometry),
                    minimized: w.minimized,
                    maximized: w.maximizeMode === 3,
                    keepAbove: w.keepAbove,
                    noBorder: w.noBorder,
                    managed: w.managed,
                    deleted: w.deleted,
                    hidden: w.hidden,
                    desktopWindow: w.desktopWindow,
                    dock: w.dock,
                    popup: w.popupWindow || w.menu || w.tooltip || w.comboBox || w.dndIcon || w.appletPopup,
                    notification: w.notification || w.criticalNotification || w.onScreenDisplay,
                    splash: w.splash,
                    skipTaskbar: w.skipTaskbar,
                    skipPager: w.skipPager,
                    onCurrentDesktop: w.onAllDesktops || w.desktops.indexOf(desktop) >= 0,
                    onCurrentActivity: w.activities.length === 0 || w.activities.indexOf(activity) >= 0
                });
            }
            var active = workspace.activeWindow;
            report({
                active: active ? active.internalId.toString() : null,
                cursor: [workspace.cursorPos.x, workspace.cursorPos.y],
                windows: windows
            });
        """;

    internal static KWinSnapshot? ParseSnapshot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("windows", out JsonElement windowsElement) || windowsElement.ValueKind != JsonValueKind.Array ||
            !TryGetPoint(root, "cursor", out Point cursor))
        {
            return null;
        }

        var windows = new List<KWinWindow>();
        foreach (JsonElement element in windowsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                GetString(element, "id") is not { Length: > 0 } id ||
                !TryGetRectangle(element, "frame", out Rectangle frame) ||
                !TryGetRectangle(element, "client", out Rectangle client))
            {
                continue;
            }

            string caption = GetString(element, "caption") ?? string.Empty;
            bool listed = GetBool(element, "managed") && !GetBool(element, "deleted") && !GetBool(element, "hidden") &&
                GetBool(element, "onCurrentDesktop") && GetBool(element, "onCurrentActivity") &&
                !GetBool(element, "desktopWindow") && !GetBool(element, "dock") && !GetBool(element, "popup") &&
                !GetBool(element, "notification") && !GetBool(element, "splash") &&
                !GetBool(element, "skipTaskbar") && !GetBool(element, "skipPager") &&
                !string.IsNullOrWhiteSpace(caption) &&
                !string.Equals(caption, PlatformWindowTitles.RegionCaptureOverlay, StringComparison.Ordinal) &&
                frame.Width > 1 && frame.Height > 1;

            windows.Add(new KWinWindow(
                id,
                caption,
                GetString(element, "resourceClass") ?? string.Empty,
                element.TryGetProperty("pid", out JsonElement pid) && pid.TryGetUInt32(out uint processId) ? processId : 0,
                frame,
                client,
                GetBool(element, "minimized"),
                GetBool(element, "maximized"),
                GetBool(element, "keepAbove"),
                GetBool(element, "noBorder"),
                listed,
                GetBool(element, "notification")));
        }

        return new KWinSnapshot(windows, GetString(root, "active"), cursor);
    }

    /// <summary>
    /// Converts KWin's logical coordinates to the X11 coordinates XerahS's windows (running through
    /// XWayland) and screen list use. When XWayland applications scale themselves, KWin multiplies
    /// X11 coordinates by the Xwayland scale saved in kwinrc; otherwise the scale is 1.
    /// </summary>
    public static Rectangle ToX11(Rectangle logical, double scale) => scale == 1 ? logical : Rectangle.FromLTRB(
        (int)Math.Round(logical.Left * scale), (int)Math.Round(logical.Top * scale),
        (int)Math.Round(logical.Right * scale), (int)Math.Round(logical.Bottom * scale));

    public static Point ToX11(Point logical, double scale) => scale == 1 ? logical :
        new Point((int)Math.Round(logical.X * scale), (int)Math.Round(logical.Y * scale));

    /// <summary>The scale between KWin's logical and XerahS's X11 coordinates; 1 when XerahS is not on X11.</summary>
    public static double X11Scale
    {
        get
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                return 1;

            string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
                configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            try
            {
                string path = Path.Combine(configHome, "kwinrc");
                return File.Exists(path) ? ParseXwaylandScale(File.ReadAllLines(path)) : 1;
            }
            catch (IOException) { return 1; }
            catch (UnauthorizedAccessException) { return 1; }
        }
    }

    internal static double ParseXwaylandScale(IEnumerable<string> kwinrcLines)
    {
        bool inSection = false;
        foreach (string rawLine in kwinrcLines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inSection = line == "[Xwayland]";
                continue;
            }

            if (inSection && line.StartsWith("Scale=", StringComparison.Ordinal) &&
                double.TryParse(line["Scale=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) &&
                scale > 0 && scale <= 8)
            {
                return scale;
            }
        }

        return 1;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static bool TryGetPoint(JsonElement element, string name, out Point point)
    {
        point = Point.Empty;
        if (!TryGetNumbers(element, name, 2, out double[] values))
            return false;
        point = new Point((int)Math.Round(values[0]), (int)Math.Round(values[1]));
        return true;
    }

    private static bool TryGetRectangle(JsonElement element, string name, out Rectangle rectangle)
    {
        rectangle = Rectangle.Empty;
        if (!TryGetNumbers(element, name, 4, out double[] values) || values[2] < 0 || values[3] < 0)
            return false;

        // Fractional scaling gives fractional logical geometry; round the edges, not the size.
        rectangle = Rectangle.FromLTRB(
            (int)Math.Round(values[0]), (int)Math.Round(values[1]),
            (int)Math.Round(values[0] + values[2]), (int)Math.Round(values[1] + values[3]));
        return true;
    }

    private static bool TryGetNumbers(JsonElement element, string name, int count, out double[] values)
    {
        values = new double[count];
        if (!element.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != count)
            return false;

        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double value) ||
                !double.IsFinite(value) || Math.Abs(value) > 1_000_000)
            {
                return false;
            }

            values[index++] = value;
        }

        return true;
    }
}
