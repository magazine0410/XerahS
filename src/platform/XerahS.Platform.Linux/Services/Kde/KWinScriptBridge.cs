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
using System.Text.Json;
using Tmds.DBus;
using XerahS.Common;
using XerahS.Platform.Linux.Capture.Detection;

namespace XerahS.Platform.Linux.Services.Kde;

/// <summary>
/// Runs short KWin scripts and returns what they report. KWin scripts cannot answer D-Bus calls, so
/// each request loads a one-off script through org.kde.KWin /Scripting; the script sends its result
/// back with callDBus to an object this class exports on the session bus, and is then unloaded.
/// A request takes a few milliseconds. This is how kdotool works, without the extra process.
/// </summary>
internal sealed class KWinScriptBridge
{
    private const string KWinBusName = "org.kde.KWin";
    private const string ReceiverInterface = "io.github.xerahs.KWinBridge";
    private static readonly ObjectPath ScriptingPath = new("/Scripting");
    private static readonly ObjectPath ReceiverPath = new("/io/github/xerahs/KWinBridge");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly Lazy<KWinScriptBridge?> SharedBridge = new(() => Task.Run(CreateAsync).GetAwaiter().GetResult());

    private readonly Connection _connection;
    private readonly IKWinScripting _scripting;
    private readonly string _localName;
    private readonly string _scriptDirectory;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new(StringComparer.Ordinal);
    private long _requestCounter;

    private KWinScriptBridge(Connection connection, IKWinScripting scripting, string localName, string scriptDirectory)
    {
        _connection = connection;
        _scripting = scripting;
        _localName = localName;
        _scriptDirectory = scriptDirectory;
    }

    /// <summary>The bridge for a KDE Plasma Wayland session, or null when KWin scripting is unavailable.</summary>
    public static KWinScriptBridge? Shared => SharedBridge.Value;

    internal static bool IsKdeWaylandSession(Func<string, string?> getEnvironmentVariable) =>
        string.Equals(getEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(DesktopEnvironmentDetector.Detect(getEnvironmentVariable), "KDE", StringComparison.Ordinal);

    private static async Task<KWinScriptBridge?> CreateAsync()
    {
        // Inside Flatpak or Snap the sandbox normally blocks org.kde.KWin, and KWin cannot read
        // script files from the sandbox's temporary directory.
        if (!OperatingSystem.IsLinux() || !IsKdeWaylandSession(Environment.GetEnvironmentVariable) || LinuxRuntimeEnvironment.Detect().IsSandboxed)
            return null;

        Connection? connection = null;
        try
        {
            connection = new Connection(Address.Session);
            ConnectionInfo info = await connection.ConnectAsync().ConfigureAwait(false);
            var scripting = connection.CreateProxy<IKWinScripting>(KWinBusName, ScriptingPath);
            await scripting.isScriptLoadedAsync("xerahs-bridge-probe").ConfigureAwait(false);

            var bridge = new KWinScriptBridge(connection, scripting, info.LocalName, CreateScriptDirectory());
            await connection.RegisterObjectAsync(new Receiver(bridge)).ConfigureAwait(false);
            DebugHelper.WriteLine("KWinScriptBridge: KWin scripting is available.");
            return bridge;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"KWinScriptBridge: KWin scripting is unavailable: {ex.Message}");
            connection?.Dispose();
            return null;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static string CreateScriptDirectory()
    {
        string? runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        string root = string.IsNullOrEmpty(runtimeDirectory) || !Directory.Exists(runtimeDirectory) ? Path.GetTempPath() : runtimeDirectory;
        string directory = Path.Combine(root, $"xerahs-kwin-{Environment.ProcessId}");
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }

    /// <summary>
    /// Runs <paramref name="body"/> as a KWin script. The body calls <c>report(value)</c> once; the value
    /// is returned as JSON. Returns null when KWin does not answer in time or the script fails.
    /// </summary>
    public async Task<JsonDocument?> RunAsync(string body)
    {
        long request = Interlocked.Increment(ref _requestCounter);
        string token = Guid.NewGuid().ToString("N");
        string pluginName = $"xerahs-{Environment.ProcessId}-{request}";
        string path = Path.Combine(_scriptDirectory, $"{pluginName}.js");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[token] = completion;
        bool loaded = false;
        try
        {
            await File.WriteAllTextAsync(path, BuildScript(_localName, token, body)).ConfigureAwait(false);
            int scriptId = await _scripting.loadScriptAsync(path, pluginName).ConfigureAwait(false);
            if (scriptId < 0)
            {
                DebugHelper.WriteLine($"KWinScriptBridge: KWin did not load script {pluginName}.");
                return null;
            }

            loaded = true;
            await _connection.CreateProxy<IKWinScript>(KWinBusName, new ObjectPath($"/Scripting/Script{scriptId}")).runAsync().ConfigureAwait(false);
            Task finished = await Task.WhenAny(completion.Task, Task.Delay(RequestTimeout)).ConfigureAwait(false);
            if (finished != completion.Task)
            {
                DebugHelper.WriteLine($"KWinScriptBridge: KWin script {pluginName} did not report within {RequestTimeout.TotalMilliseconds} ms.");
                return null;
            }

            return JsonDocument.Parse(completion.Task.Result);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"KWinScriptBridge: KWin script {pluginName} failed: {ex.Message}");
            return null;
        }
        finally
        {
            _pending.TryRemove(token, out _);
            if (loaded)
            {
                try { await _scripting.unloadScriptAsync(pluginName).ConfigureAwait(false); }
                catch (Exception ex) { DebugHelper.WriteLine($"KWinScriptBridge: Could not unload {pluginName}: {ex.Message}"); }
            }

            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>Runs a script from synchronous code without blocking on the caller's synchronization context.</summary>
    public JsonDocument? Run(string body) => Task.Run(() => RunAsync(body)).GetAwaiter().GetResult();

    internal static string BuildScript(string serviceName, string token, string body) =>
        $$"""
        (function () {
            var reported = false;
            function report(value) {
                if (reported) return;
                reported = true;
                callDBus({{JsonSerializer.Serialize(serviceName)}}, {{JsonSerializer.Serialize(ReceiverPath.ToString())}}, {{JsonSerializer.Serialize(ReceiverInterface)}}, "Report", {{JsonSerializer.Serialize(token)}}, JSON.stringify(value === undefined ? null : value));
            }
            try {
        {{body}}
            } catch (e) {
                report({ error: String(e) });
            }
        })();
        """;

    private void Complete(string token, string payload)
    {
        if (_pending.TryGetValue(token, out var completion))
            completion.TrySetResult(payload);
    }

    private sealed class Receiver(KWinScriptBridge bridge) : IKWinBridgeReceiver
    {
        public ObjectPath ObjectPath => ReceiverPath;

        public Task ReportAsync(string token, string payload)
        {
            bridge.Complete(token, payload);
            return Task.CompletedTask;
        }
    }
}

[DBusInterface("io.github.xerahs.KWinBridge")]
public interface IKWinBridgeReceiver : IDBusObject
{
    Task ReportAsync(string token, string payload);
}

[DBusInterface("org.kde.kwin.Scripting")]
public interface IKWinScripting : IDBusObject
{
    Task<int> loadScriptAsync(string filePath, string pluginName);
    Task<bool> unloadScriptAsync(string pluginName);
    Task<bool> isScriptLoadedAsync(string pluginName);
}

[DBusInterface("org.kde.kwin.Script")]
public interface IKWinScript : IDBusObject
{
    Task runAsync();
}
