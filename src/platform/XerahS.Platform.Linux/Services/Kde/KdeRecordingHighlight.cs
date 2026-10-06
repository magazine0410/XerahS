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

using System.Text.RegularExpressions;
using Tmds.DBus;
using XerahS.Common;

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>Temporarily enables KWin's click animation without changing desktop preferences.</summary>
internal sealed class KdeRecordingHighlight : IAsyncDisposable, IDisposable
{
    internal const string UnsupportedMessage =
        "Highlighting the mouse while recording is not supported on this Wayland desktop. It works on X11 and on KDE Plasma.";

    private readonly Connection _connection;
    private readonly IKWinRecordingEffects _effects;
    private readonly IKWinRecordingShortcuts _shortcuts;
    private readonly KdeClickLabelSetting _label = KdeClickLabelSetting.Default;
    private bool _loadedHere;
    private bool _enabledHere;
    private bool _labelHidden;
    private int _disposed;

    private KdeRecordingHighlight(Connection connection)
    {
        _connection = connection;
        _effects = connection.CreateProxy<IKWinRecordingEffects>("org.kde.KWin", new ObjectPath("/Effects"));
        _shortcuts = connection.CreateProxy<IKWinRecordingShortcuts>("org.kde.kglobalaccel", new ObjectPath("/component/kwin"));
    }

    internal static bool IsKdeSession(string? currentDesktop) =>
        (currentDesktop ?? string.Empty).Split(':').Any(part => part.Equals("KDE", StringComparison.OrdinalIgnoreCase));

    /// <summary>Enables the effect over D-Bus without blocking the calling (UI) thread.</summary>
    public static async Task<IAsyncDisposable?> BeginAsync()
    {
        if (!IsKdeSession(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")))
            throw new PlatformNotSupportedException(UnsupportedMessage);

        var connection = new Connection(Address.Session);
        var scope = new KdeRecordingHighlight(connection);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            bool loaded = await scope._effects.isEffectLoadedAsync("mouseclick").ConfigureAwait(false);
            // ShareX's highlighter has no button label; KDE's "Show text" (on by default) adds one to each click.
            scope._labelHidden = await scope._label.HideAsync().ConfigureAwait(false);
            if (!loaded)
            {
                if (!await scope._effects.loadEffectAsync("mouseclick").ConfigureAwait(false))
                    throw new PlatformNotSupportedException("Recording mouse highlighting on Wayland requires KDE's Mouse Click Animation effect.");
                scope._loadedHere = true;
            }
            // KWin builds a loaded effect from its in-memory copy of kwinrc; reconfigureEffect re-reads the file.
            if (scope._labelHidden) await scope._effects.reconfigureEffectAsync("mouseclick").ConfigureAwait(false);
            bool enabled = ParseEnabled(await scope._effects.supportInformationAsync("mouseclick").ConfigureAwait(false));
            if (!enabled)
            {
                scope._enabledHere = true;
                await scope._shortcuts.invokeShortcutAsync("ToggleMouseClick").ConfigureAwait(false);
                await scope.WaitForEnabledAsync(true).ConfigureAwait(false);
            }
            return scope;
        }
        catch (DBusException ex)
        {
            await scope.RestoreAsync().ConfigureAwait(false);
            connection.Dispose();
            throw new PlatformNotSupportedException(UnsupportedMessage, ex);
        }
        catch
        {
            await scope.RestoreAsync().ConfigureAwait(false);
            connection.Dispose();
            throw;
        }
    }

    internal static bool? ParseShowText(string information)
    {
        var match = Regex.Match(information, @"(?im)^showText:\s*(true|false)\s*$", RegexOptions.CultureInvariant);
        return match.Success ? bool.Parse(match.Groups[1].Value) : null;
    }

    internal static bool ParseEnabled(string information)
    {
        var match = Regex.Match(information, @"(?im)^enabled:\s*(true|false)\s*$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new NotSupportedException("KDE did not report the mouse-click effect state; it cannot be changed safely.");
        return bool.Parse(match.Groups[1].Value);
    }

    private async Task WaitForEnabledAsync(bool enabled)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (ParseEnabled(await _effects.supportInformationAsync("mouseclick").ConfigureAwait(false)) == enabled) return;
            await Task.Delay(25).ConfigureAwait(false);
        }
        throw new InvalidOperationException("KDE did not switch the mouse-click animation state.");
    }

    private async Task RestoreAsync()
    {
        // The label first, while the effect is still loaded: KWin re-reads kwinrc only for a loaded effect, and an
        // effect loaded later would otherwise be built from KWin's stale in-memory copy with the label still off.
        try
        {
            if (_labelHidden && await _label.RestoreAsync().ConfigureAwait(false))
                await ReloadClickLabelAsync(_effects).ConfigureAwait(false);
        }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Could not restore KDE's click label setting"); }

        try
        {
            if (_loadedHere) await _effects.unloadEffectAsync("mouseclick").ConfigureAwait(false);
            else if (_enabledHere && await _effects.isEffectLoadedAsync("mouseclick").ConfigureAwait(false) &&
                ParseEnabled(await _effects.supportInformationAsync("mouseclick").ConfigureAwait(false)))
            {
                await _shortcuts.invokeShortcutAsync("ToggleMouseClick").ConfigureAwait(false);
                await WaitForEnabledAsync(false).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Could not restore KDE recording highlight"); }
    }

    /// <summary>Makes KWin re-read the effect's settings, loading it briefly (disabled) when it is not loaded.</summary>
    private static async Task ReloadClickLabelAsync(IKWinRecordingEffects effects)
    {
        if (await effects.isEffectLoadedAsync("mouseclick").ConfigureAwait(false))
        {
            await effects.reconfigureEffectAsync("mouseclick").ConfigureAwait(false);
        }
        else if (await effects.loadEffectAsync("mouseclick").ConfigureAwait(false))
        {
            await effects.reconfigureEffectAsync("mouseclick").ConfigureAwait(false);
            await effects.unloadEffectAsync("mouseclick").ConfigureAwait(false);
        }
    }

    /// <summary>Restores the click label setting if XerahS exited during a recording without restoring it.</summary>
    public static async Task RestoreAfterUnexpectedExitAsync()
    {
        if (!IsKdeSession(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")) || !KdeClickLabelSetting.Default.IsPending) return;
        try
        {
            if (!await KdeClickLabelSetting.Default.RestoreAsync().ConfigureAwait(false)) return;
            using var connection = new Connection(Address.Session);
            await connection.ConnectAsync().ConfigureAwait(false);
            var effects = connection.CreateProxy<IKWinRecordingEffects>("org.kde.KWin", new ObjectPath("/Effects"));
            await ReloadClickLabelAsync(effects).ConfigureAwait(false);
            DebugHelper.WriteLine("KdeRecordingHighlight: restored KDE's click label setting left by an earlier recording.");
        }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Could not restore KDE's click label setting"); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await RestoreAsync().ConfigureAwait(false);
        _connection.Dispose();
    }

    /// <summary>Restores synchronously; used when the application exits during a recording.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Task.Run(RestoreAsync).GetAwaiter().GetResult();
        _connection.Dispose();
    }
}

[DBusInterface("org.kde.kwin.Effects")]
public interface IKWinRecordingEffects : IDBusObject
{
    Task<bool> isEffectLoadedAsync(string name);
    Task<bool> loadEffectAsync(string name);
    Task unloadEffectAsync(string name);
    Task reconfigureEffectAsync(string name);
    Task<string> supportInformationAsync(string name);
}

/// <summary>
/// The "Show text" option of KDE's Mouse Click Animation (kwinrc, [Effect-mouseclick] ShowText), turned off while
/// recording. The previous value is saved to a file first, so it can be restored after an unexpected exit.
/// </summary>
internal sealed class KdeClickLabelSetting
{
    internal const string Group = "Effect-mouseclick";
    internal const string Key = "ShowText";
    private const string Unset = "<unset>";

    private readonly Func<string, IReadOnlyList<string>, Task<(int ExitCode, string Output)>> _run;
    private readonly string _savedValuePath;

    internal KdeClickLabelSetting(Func<string, IReadOnlyList<string>, Task<(int ExitCode, string Output)>> run, string savedValuePath)
    {
        _run = run;
        _savedValuePath = savedValuePath;
    }

    public static KdeClickLabelSetting Default { get; } = new(RunAsync,
        Path.Combine(LinuxXdgDirectories.Detect().StateDirectory, "kde-mouseclick-showtext.saved"));

    public bool IsPending => File.Exists(_savedValuePath);

    /// <summary>Turns the label off. Returns false when it was already off, or when it could not be changed.</summary>
    public async Task<bool> HideAsync()
    {
        if (IsPending) await RestoreAsync().ConfigureAwait(false);
        var (readExit, output) = await _run("kreadconfig6", ["--file", "kwinrc", "--group", Group, "--key", Key]).ConfigureAwait(false);
        if (readExit != 0) return false;
        string value = output.Trim();
        if (IsFalse(value)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(_savedValuePath)!);
        await File.WriteAllTextAsync(_savedValuePath, value.Length == 0 ? Unset : value).ConfigureAwait(false);
        var (writeExit, _) = await _run("kwriteconfig6", ["--file", "kwinrc", "--group", Group, "--key", Key, "--type", "bool", "false"]).ConfigureAwait(false);
        if (writeExit == 0) return true;
        File.Delete(_savedValuePath);
        return false;
    }

    /// <summary>Puts back the saved value, or removes the key when it was not set. Returns false when nothing was saved.</summary>
    public async Task<bool> RestoreAsync()
    {
        if (!IsPending) return false;
        string saved = (await File.ReadAllTextAsync(_savedValuePath).ConfigureAwait(false)).Trim();
        IReadOnlyList<string> args = saved == Unset
            ? ["--file", "kwinrc", "--group", Group, "--key", Key, "--delete"]
            : ["--file", "kwinrc", "--group", Group, "--key", Key, saved];
        var (exit, _) = await _run("kwriteconfig6", args).ConfigureAwait(false);
        if (exit != 0) return false;
        File.Delete(_savedValuePath);
        return true;
    }

    internal static bool IsFalse(string value) =>
        value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0" ||
        value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase);

    private static async Task<(int ExitCode, string Output)> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            using var process = System.Diagnostics.Process.Start(info);
            if (process == null) return (-1, string.Empty);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, string.Empty); // kreadconfig6/kwriteconfig6 not installed
        }
    }
}

[DBusInterface("org.kde.kglobalaccel.Component")]
public interface IKWinRecordingShortcuts : IDBusObject
{
    Task invokeShortcutAsync(string shortcutName);
}
