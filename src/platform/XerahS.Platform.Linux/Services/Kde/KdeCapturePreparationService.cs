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
using System.Text.Json.Serialization;
using XerahS.Common;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>A Plasma panel as the shell scripting API reports it; lengths and the thickness are logical pixels.</summary>
internal sealed record PlasmaPanel(int Id, int Screen, string Location, string Hiding, int Thickness, string Alignment, int Offset, int MaximumLength, string LengthMode);

/// <summary>A Plasma Folder View desktop containment and its filter settings (empty when unset).</summary>
internal sealed record PlasmaDesktop(int Id, string FilterMode, string FilterPattern);

internal sealed record PlasmaPanelLayout(IReadOnlyList<Rectangle> Screens, IReadOnlyList<PlasmaPanel> Panels);

/// <summary>
/// ShareX's capture preparation on KDE Plasma. "Hide taskbar when it intersects a captured window" switches the
/// panels that the window intersects to auto-hide for the capture (ShareX hides the Windows taskbar), and
/// "Automatically hide desktop icons" filters every Folder View desktop to show nothing (ShareX hides the desktop's
/// list view). Both go through Plasma's shell scripting and are undone when the capture ends; the original modes
/// are kept in a file first, so that a crash during a capture is undone at the next start.
/// </summary>
public sealed class KdeCapturePreparationService : ICapturePreparationService
{
    internal const string AutoHideMode = "autohide";
    internal const string FilterShowMatches = "1";
    internal const string HiddenIconsPattern = "xerahs-hides-the-desktop-icons-during-a-capture";
    internal const string MarkerFileName = "kde-capture-preparation.json";

    private readonly IPlasmaShellScripting _shell;
    private readonly string _markerPath;
    private readonly Func<KWinSnapshot?> _windows;
    private readonly Func<double> _x11Scale;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, string> _hiddenPanels = new();
    private readonly Dictionary<int, PlasmaDesktop> _filteredDesktops = new();
    // Hidden panels whose capture has ended but that could not be shown again; retried after the next capture.
    private readonly HashSet<int> _unrestoredPanels = new();
    // The desktop icons' capture has ended but their filters could not be restored; retried after the next capture.
    private bool _desktopRestorePending;
    private int _iconScopes;

    public KdeCapturePreparationService()
        : this(new PlasmaShellScripting(), Path.Combine(PathsManager.TroubleshootingFolder, MarkerFileName),
            () => KWinWindowManager.Shared?.GetSnapshot(), () => KWinWindowManager.X11Scale)
    {
    }

    internal KdeCapturePreparationService(IPlasmaShellScripting shell, string markerPath, Func<KWinSnapshot?> windows, Func<double> x11Scale)
    {
        _shell = shell;
        _markerPath = markerPath;
        _windows = windows;
        _x11Scale = x11Scale;
    }

    /// <summary>How long to wait for KWin to report the panels gone before capturing anyway.</summary>
    internal TimeSpan PanelHideTimeout { get; set; } = TimeSpan.FromSeconds(1);

    internal TimeSpan PanelPollInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>The wait when KWin cannot be asked whether the panels are gone (ShareX waits 250 ms for its changes).</summary>
    internal TimeSpan PanelSettleDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>The wait for Folder View to remove its icons, as ShareX's 250 ms after hiding the desktop's list view.</summary>
    internal TimeSpan IconSettleDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How often, and how far apart, the start tries to undo an earlier run's changes: at login XerahS can start before
    /// Plasma's shell answers.
    /// </summary>
    internal int RecoveryAttempts { get; set; } = 3;

    internal TimeSpan RecoveryRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    public bool CanHidePanels => _shell.IsAvailable;

    public bool CanHideDesktopIcons => _shell.IsAvailable;

    public async Task<IAsyncDisposable?> HidePanelsIntersectingAsync(Rectangle bounds, CancellationToken token)
    {
        if (!_shell.IsAvailable || bounds.Width <= 0 || bounds.Height <= 0) return null;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        int[] hidden;
        try
        {
            string? json = await _shell.EvaluateAsync(BuildPanelQueryScript(), token).ConfigureAwait(false);
            PlasmaPanelLayout? layout = ParsePanelLayout(json);
            if (layout == null) return null;
            double scale = _x11Scale();
            var toHide = new List<PlasmaPanel>();
            foreach (PlasmaPanel panel in layout.Panels)
            {
                if (_hiddenPanels.ContainsKey(panel.Id) || string.Equals(panel.Hiding, AutoHideMode, StringComparison.OrdinalIgnoreCase)) continue;
                Rectangle? panelBounds = GetPanelBounds(panel, layout.Screens);
                if (panelBounds == null) continue;
                if (KWinWindowManager.ToX11(panelBounds.Value, scale).IntersectsWith(bounds)) toHide.Add(panel);
            }

            if (toHide.Count == 0) return null;
            foreach (PlasmaPanel panel in toHide) _hiddenPanels[panel.Id] = panel.Hiding;
            SaveMarker();
            DebugHelper.WriteLine($"KDE capture preparation: hiding panel(s) {string.Join(", ", toHide.Select(p => p.Id))} for the capture.");
            hidden = toHide.Select(p => p.Id).ToArray();
            try
            {
                await _shell.EvaluateAsync(BuildPanelHidingScript(toHide.Select(p => (p.Id, AutoHideMode))), token).ConfigureAwait(false);
            }
            catch
            {
                // The script may have run, and no scope is returned to undo it, so the modes are given back now.
                _unrestoredPanels.UnionWith(hidden);
                await TryRestorePanelsAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await WaitForPanelsHiddenAsync(bounds, token).ConfigureAwait(false);
        }
        catch
        {
            // A capture cancelled during the wait gets no scope, so the panels are shown again here.
            await RestorePanelsAsync(hidden).ConfigureAwait(false);
            throw;
        }

        return new Scope(() => RestorePanelsAsync(hidden));
    }

    private async Task WaitForPanelsHiddenAsync(Rectangle bounds, CancellationToken token)
    {
        if (_windows() == null)
        {
            await Task.Delay(PanelSettleDelay, token).ConfigureAwait(false);
            return;
        }

        // KWin reports logical coordinates; the capture bounds are X11's, which XWayland scales.
        double scale = _x11Scale();
        DateTime deadline = DateTime.UtcNow + PanelHideTimeout;
        while (DateTime.UtcNow < deadline)
        {
            KWinSnapshot? snapshot = _windows();
            if (snapshot == null || !snapshot.Windows.Any(w => w.Dock && !w.Hidden && !w.Minimized &&
                KWinWindowManager.ToX11(w.FrameGeometry, scale).IntersectsWith(bounds)))
            {
                return;
            }

            await Task.Delay(PanelPollInterval, token).ConfigureAwait(false);
        }

        DebugHelper.WriteLine("KDE capture preparation: a panel still intersects the window after the wait; capturing anyway.");
    }

    private async Task RestorePanelsAsync(int[] ids)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _unrestoredPanels.UnionWith(ids.Where(_hiddenPanels.ContainsKey));
            await TryRestorePanelsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "KDE capture preparation: the panels could not be shown again");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Gives the panels whose capture has ended their modes back. A panel stays in the recovery file until the shell
    /// confirms the change, so a shell that does not answer cannot lose the panel's mode. Called inside the gate.
    /// </summary>
    private async Task<bool> TryRestorePanelsAsync()
    {
        var modes = _unrestoredPanels.Where(_hiddenPanels.ContainsKey).Select(id => (Id: id, Mode: _hiddenPanels[id])).ToList();
        if (modes.Count == 0)
        {
            _unrestoredPanels.Clear();
            return true;
        }

        if (!await RunScriptAsync(BuildPanelHidingScript(modes)).ConfigureAwait(false))
        {
            DebugHelper.WriteLine($"KDE capture preparation: panel(s) {string.Join(", ", modes.Select(m => m.Id))} could not be shown again; " +
                "they stay in the recovery file and are retried after the next capture and at the next start.");
            return false;
        }

        foreach ((int id, _) in modes)
        {
            _hiddenPanels.Remove(id);
            _unrestoredPanels.Remove(id);
        }

        SaveMarker();
        return true;
    }

    /// <summary>Runs a script that prints "ok" when it is done; false when the shell did not answer that.</summary>
    private async Task<bool> RunScriptAsync(string script)
    {
        string? output = await _shell.EvaluateAsync(script, CancellationToken.None).ConfigureAwait(false);
        return string.Equals(output?.Trim(), "ok", StringComparison.Ordinal);
    }

    public async Task<IAsyncDisposable?> HideDesktopIconsAsync(CancellationToken token)
    {
        if (!_shell.IsAvailable) return null;
        // As in ShareX, a full-screen active window is left alone: the desktop is not in the picture.
        KWinSnapshot? snapshot = _windows();
        if (snapshot?.ActiveWindowId != null && snapshot.Windows.Any(w => w.Id == snapshot.ActiveWindowId && w.FullScreen)) return null;

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_iconScopes > 0)
            {
                _iconScopes++;
                return new Scope(RestoreDesktopIconsAsync);
            }

            if (_filteredDesktops.Count == 0)
            {
                string? json = await _shell.EvaluateAsync(BuildDesktopQueryScript(), token).ConfigureAwait(false);
                IReadOnlyList<PlasmaDesktop>? desktops = ParseDesktops(json);
                if (desktops == null || desktops.Count == 0) return null;
                foreach (PlasmaDesktop desktop in desktops)
                {
                    // A filter that is still XerahS's own (an earlier run could not restore it) is not the user's;
                    // Folder View's default, which shows every icon, is kept instead.
                    _filteredDesktops[desktop.Id] = desktop.FilterPattern == HiddenIconsPattern ? desktop with { FilterMode = "0", FilterPattern = string.Empty } : desktop;
                }

                SaveMarker();
            }

            // Otherwise an earlier restore failed and the originals kept then are used: the filters read now may be
            // XerahS's own.
            _desktopRestorePending = false;
            DebugHelper.WriteLine($"KDE capture preparation: hiding the desktop icons of {_filteredDesktops.Count} Folder View desktop(s) for the capture.");
            try
            {
                await _shell.EvaluateAsync(BuildDesktopFilterScript(_filteredDesktops.Keys.Select(id => (id, FilterShowMatches, HiddenIconsPattern))), token).ConfigureAwait(false);
            }
            catch
            {
                // The script may have run, and no scope is returned to undo it, so the filters are given back now.
                _desktopRestorePending = true;
                await TryRestoreDesktopsAsync().ConfigureAwait(false);
                throw;
            }

            _iconScopes = 1;
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await Task.Delay(IconSettleDelay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A capture cancelled during the wait gets no scope, so the icons are shown again here.
            await RestoreDesktopIconsAsync().ConfigureAwait(false);
            throw;
        }

        return new Scope(RestoreDesktopIconsAsync);
    }

    private async Task RestoreDesktopIconsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_iconScopes == 0) return;
            if (--_iconScopes > 0) return;
            _desktopRestorePending = true;
            await TryRestoreDesktopsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "KDE capture preparation: the desktop icons could not be shown again");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Gives the desktops their filters back once their capture has ended. They stay in the recovery file until the
    /// shell confirms the change. Called inside the gate.
    /// </summary>
    private async Task<bool> TryRestoreDesktopsAsync()
    {
        if (!_desktopRestorePending) return true;
        if (_filteredDesktops.Count > 0)
        {
            var originals = _filteredDesktops.Values.Select(d => (d.Id, d.FilterMode, d.FilterPattern)).ToList();
            if (!await RunScriptAsync(BuildDesktopFilterScript(originals)).ConfigureAwait(false))
            {
                DebugHelper.WriteLine("KDE capture preparation: the desktop icons could not be shown again; their filters stay in the recovery file " +
                    "and are restored after the next capture and at the next start.");
                return false;
            }

            _filteredDesktops.Clear();
        }

        _desktopRestorePending = false;
        SaveMarker();
        return true;
    }

    /// <summary>
    /// Undoes what an earlier run left behind (it ended during a capture). The changes stay in the recovery file until
    /// the shell confirms them; at login the shell may not answer yet, so this tries again a few times.
    /// </summary>
    public async Task RecoverAsync()
    {
        PreparationState? state;
        try
        {
            if (!File.Exists(_markerPath)) return;
            state = JsonSerializer.Deserialize<PreparationState>(File.ReadAllText(_markerPath));
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "KDE capture preparation: the recovery file could not be read");
            return;
        }

        if (!_shell.IsAvailable || state == null) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (PanelState panel in state.Panels)
            {
                if (_hiddenPanels.TryAdd(panel.Id, panel.Hiding)) _unrestoredPanels.Add(panel.Id);
            }

            foreach (DesktopState desktop in state.Desktops)
            {
                _filteredDesktops.TryAdd(desktop.Id, new PlasmaDesktop(desktop.Id, desktop.FilterMode, desktop.FilterPattern));
            }

            if (state.Desktops.Count > 0 && _iconScopes == 0) _desktopRestorePending = true;
            if (state.Panels.Count > 0)
            {
                DebugHelper.WriteLine($"KDE capture preparation: showing panel(s) {string.Join(", ", state.Panels.Select(p => p.Id))} left hidden by an earlier run.");
            }

            if (state.Desktops.Count > 0)
            {
                DebugHelper.WriteLine($"KDE capture preparation: showing the desktop icons of {state.Desktops.Count} desktop(s) left hidden by an earlier run.");
            }
        }
        finally
        {
            _gate.Release();
        }

        for (int attempt = 1; attempt <= RecoveryAttempts; attempt++)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                bool panelsRestored = await TryRestorePanelsAsync().ConfigureAwait(false);
                bool desktopsRestored = await TryRestoreDesktopsAsync().ConfigureAwait(false);
                if (panelsRestored && desktopsRestored)
                {
                    SaveMarker();
                    return;
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "KDE capture preparation: an earlier run's changes could not be undone");
            }
            finally
            {
                _gate.Release();
            }

            if (attempt < RecoveryAttempts) await Task.Delay(RecoveryRetryDelay).ConfigureAwait(false);
        }
    }

    // ---- Scripts (the Plasma shell scripting API: panels(), panelById(), desktops(), desktopById()) ----

    internal static string BuildPanelQueryScript() => """
        var out = { screens: [], panels: [] };
        for (var i = 0; i < screenCount; i++) { var g = screenGeometry(i); out.screens.push([g.x, g.y, g.width, g.height]); }
        var ps = panels();
        for (var i = 0; i < ps.length; i++) {
            var p = ps[i];
            out.panels.push({ id: p.id, screen: p.screen, location: p.location, hiding: p.hiding, height: p.height,
                alignment: p.alignment, offset: p.offset, maxLength: p.maximumLength, lengthMode: p.lengthMode });
        }
        print(JSON.stringify(out));
        """;

    internal static string BuildPanelHidingScript(IEnumerable<(int Id, string Mode)> panels)
    {
        var script = new System.Text.StringBuilder();
        foreach ((int id, string mode) in panels)
        {
            script.Append("var p").Append(id).Append(" = panelById(").Append(id).Append("); if (p").Append(id)
                .Append(") p").Append(id).Append(".hiding = ").Append(JsonSerializer.Serialize(mode)).Append(";\n");
        }

        script.Append("print('ok');");
        return script.ToString();
    }

    internal static string BuildDesktopQueryScript() => """
        var out = [];
        var ds = desktops();
        for (var i = 0; i < ds.length; i++) {
            var d = ds[i];
            if (d.type !== "org.kde.plasma.folder") continue;
            d.currentConfigGroup = ["General"];
            out.push({ id: d.id, filterMode: String(d.readConfig("filterMode")), filterPattern: String(d.readConfig("filterPattern")) });
        }
        print(JSON.stringify(out));
        """;

    /// <summary>Folder View's filter: mode 0 shows everything, 1 only the names matching the pattern, 2 all but them.</summary>
    internal static string BuildDesktopFilterScript(IEnumerable<(int Id, string FilterMode, string FilterPattern)> desktops)
    {
        var script = new System.Text.StringBuilder();
        foreach ((int id, string mode, string pattern) in desktops)
        {
            string filterMode = int.TryParse(mode, out int parsed) ? parsed.ToString() : "0";
            script.Append("var d").Append(id).Append(" = desktopById(").Append(id).Append("); if (d").Append(id).Append(") { d").Append(id)
                .Append(".currentConfigGroup = [\"General\"]; d").Append(id).Append(".writeConfig(\"filterMode\", ").Append(filterMode)
                .Append("); d").Append(id).Append(".writeConfig(\"filterPattern\", ").Append(JsonSerializer.Serialize(pattern ?? string.Empty))
                .Append("); d").Append(id).Append(".reloadConfig(); }\n");
        }

        script.Append("print('ok');");
        return script.ToString();
    }

    // ---- Parsing and geometry ----

    internal static PlasmaPanelLayout? ParsePanelLayout(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json.Trim());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var screens = new List<Rectangle>();
            if (root.TryGetProperty("screens", out JsonElement screensElement) && screensElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement screen in screensElement.EnumerateArray())
                {
                    if (screen.ValueKind == JsonValueKind.Array && screen.GetArrayLength() == 4)
                    {
                        int[] values = screen.EnumerateArray().Select(v => (int)Math.Round(v.GetDouble())).ToArray();
                        screens.Add(new Rectangle(values[0], values[1], values[2], values[3]));
                    }
                }
            }

            var panels = new List<PlasmaPanel>();
            if (root.TryGetProperty("panels", out JsonElement panelsElement) && panelsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement panel in panelsElement.EnumerateArray())
                {
                    if (panel.ValueKind != JsonValueKind.Object || !panel.TryGetProperty("id", out JsonElement id) || !id.TryGetInt32(out int panelId)) continue;
                    panels.Add(new PlasmaPanel(panelId, GetInt(panel, "screen"), GetString(panel, "location"), GetString(panel, "hiding"),
                        GetInt(panel, "height"), GetString(panel, "alignment"), GetInt(panel, "offset"), GetInt(panel, "maxLength"), GetString(panel, "lengthMode")));
                }
            }

            return new PlasmaPanelLayout(screens, panels);
        }
        catch (JsonException ex)
        {
            DebugHelper.WriteLine($"KDE capture preparation: the panel list could not be read: {ex.Message}");
            return null;
        }
    }

    internal static IReadOnlyList<PlasmaDesktop>? ParseDesktops(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json.Trim());
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var desktops = new List<PlasmaDesktop>();
            foreach (JsonElement desktop in document.RootElement.EnumerateArray())
            {
                if (desktop.ValueKind != JsonValueKind.Object || !desktop.TryGetProperty("id", out JsonElement id) || !id.TryGetInt32(out int desktopId)) continue;
                desktops.Add(new PlasmaDesktop(desktopId, GetString(desktop, "filterMode"), GetString(desktop, "filterPattern")));
            }

            return desktops;
        }
        catch (JsonException ex)
        {
            DebugHelper.WriteLine($"KDE capture preparation: the desktop list could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The panel's rectangle in logical coordinates: its thickness along the screen edge it sits on, and its length
    /// placed by the alignment and offset (a filling panel spans the edge). Null when its screen is unknown.
    /// </summary>
    internal static Rectangle? GetPanelBounds(PlasmaPanel panel, IReadOnlyList<Rectangle> screens)
    {
        if (panel.Screen < 0 || panel.Screen >= screens.Count || panel.Thickness <= 0) return null;
        Rectangle screen = screens[panel.Screen];
        bool vertical = panel.Location is "left" or "right";
        int edge = vertical ? screen.Height : screen.Width;
        int length = string.Equals(panel.LengthMode, "fill", StringComparison.OrdinalIgnoreCase) || panel.MaximumLength <= 0
            ? edge
            : Math.Min(panel.MaximumLength, edge);
        int start = panel.Alignment switch
        {
            "right" => edge - length - panel.Offset,
            "center" => (edge - length) / 2 + panel.Offset,
            _ => panel.Offset
        };
        start = Math.Clamp(start, 0, Math.Max(0, edge - length));
        int thickness = Math.Min(panel.Thickness, vertical ? screen.Width : screen.Height);
        return panel.Location switch
        {
            "top" => new Rectangle(screen.X + start, screen.Y, length, thickness),
            "left" => new Rectangle(screen.X, screen.Y + start, thickness, length),
            "right" => new Rectangle(screen.Right - thickness, screen.Y + start, thickness, length),
            _ => new Rectangle(screen.X + start, screen.Bottom - thickness, length, thickness)
        };
    }

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)
            ? (int)Math.Round(number)
            : value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed) ? parsed : 0;

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch { JsonValueKind.String => value.GetString() ?? string.Empty, JsonValueKind.Number => value.GetRawText(), _ => string.Empty }
            : string.Empty;

    // ---- The recovery file ----

    private void SaveMarker()
    {
        try
        {
            if (_hiddenPanels.Count == 0 && _filteredDesktops.Count == 0)
            {
                DeleteMarker();
                return;
            }

            var state = new PreparationState
            {
                Panels = _hiddenPanels.Select(pair => new PanelState { Id = pair.Key, Hiding = pair.Value }).ToList(),
                Desktops = _filteredDesktops.Values.Select(d => new DesktopState { Id = d.Id, FilterMode = d.FilterMode, FilterPattern = d.FilterPattern }).ToList()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            File.WriteAllText(_markerPath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "KDE capture preparation: the recovery file could not be written");
        }
    }

    private void DeleteMarker()
    {
        try { if (File.Exists(_markerPath)) File.Delete(_markerPath); }
        catch (Exception ex) { DebugHelper.WriteException(ex, "KDE capture preparation: the recovery file could not be removed"); }
    }

    internal sealed class PreparationState
    {
        [JsonPropertyName("panels")] public List<PanelState> Panels { get; set; } = new();
        [JsonPropertyName("desktops")] public List<DesktopState> Desktops { get; set; } = new();
    }

    internal sealed class PanelState
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("hiding")] public string Hiding { get; set; } = "none";
    }

    internal sealed class DesktopState
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("filterMode")] public string FilterMode { get; set; } = string.Empty;
        [JsonPropertyName("filterPattern")] public string FilterPattern { get; set; } = string.Empty;
    }

    private sealed class Scope(Func<Task> restore) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) await restore().ConfigureAwait(false);
        }
    }
}
