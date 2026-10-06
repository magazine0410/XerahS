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
using System.Text;
using System.Text.Json;
using XerahS.Common;

namespace XerahS.App;

/// <summary>Runs before Avalonia/logging so stdout contains only native messaging frames.</summary>
internal static class BrowserNativeMessagingHost
{
    internal static string InboxDirectory => Path.Combine(LinuxXdgDirectories.Detect().StateDirectory, "native-messaging");

    public static void Run()
    {
        try { Run(Console.OpenStandardInput(), Console.OpenStandardOutput(), Forward); }
        catch (Exception ex) { Console.Error.WriteLine("XerahS native messaging: " + ex.Message); }
    }

    internal static void Run(Stream input, Stream output, Action<string> forward)
    {
        string? message;
        while ((message = NativeMessagingHost.Read(input)) != null)
        {
            try
            {
                using var json = JsonDocument.Parse(message);
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a JSON object.");
                forward(message);
                // ShareX echoes the request. Large screenshots must use a small acknowledgement:
                // browsers cap messages from the host at 1 MiB, even though requests may be larger.
                NativeMessagingHost.Write(output, Encoding.UTF8.GetByteCount(message) <= NativeMessagingHost.MaximumOutputBytes
                    ? message : "{\"success\":true}");
            }
            catch (Exception ex)
            {
                NativeMessagingHost.Write(output, JsonSerializer.Serialize(new { error = ex.Message }));
            }
        }
    }

    private static void Forward(string message)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Browser integration is available on Linux.");
        Directory.CreateDirectory(InboxDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string path = Path.Combine(InboxDirectory, Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var file = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            }))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(message);
            string executable = Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage && File.Exists(appImage)
                ? appImage : Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
            // Do not let the GUI process inherit the browser's protocol stream.
            var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            foreach (string argument in new[] { "-c", "exec \"$@\" >/dev/null 2>&1", "xerahs", executable, "-NativeMessagingInput", path })
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new IOException("Could not start XerahS.");
        }
        catch { File.Delete(path); throw; }
    }

    internal static void DeleteConsumedInput(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) == InboxDirectory &&
            Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)) File.Delete(path);
    }
}
