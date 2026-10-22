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

namespace XerahS.App;

/// <summary>
/// Tells the user when a second launch could not pass its command line to the running instance, which
/// would otherwise drop the files silently. ShareX only logs this failure; XerahS also shows a desktop
/// notification, because on Linux the launch usually comes from a file manager or a hotkey with no visible output.
/// </summary>
internal static class HandOffFailureNotice
{
    internal const string Title = "XerahS is not responding";
    private const int MaxListedArguments = 3;

    internal static string BuildMessage(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return "XerahS is already running but did not respond. Restart XerahS and try again.";
        }

        string listed = string.Join(", ", arguments.Take(MaxListedArguments));
        if (arguments.Count > MaxListedArguments)
        {
            listed += $" and {arguments.Count - MaxListedArguments} more";
        }

        return $"XerahS is already running but did not accept this request, so it was not carried out: {listed}. Restart XerahS and try again.";
    }

    /// <summary>Writes the message to standard error and sends it to <paramref name="notify"/> as title and body.</summary>
    internal static void Report(IReadOnlyList<string> arguments, Action<string, string> notify)
    {
        string message = BuildMessage(arguments);
        Console.Error.WriteLine(message);
        notify(Title, message);
    }

    /// <summary>Best-effort desktop notification; this process exits without starting the UI.</summary>
    internal static void NotifyDesktop(string title, string message)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("notify-send")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add("--app-name=XerahS");
            startInfo.ArgumentList.Add(title);
            startInfo.ArgumentList.Add(message);
            using var process = Process.Start(startInfo);
            process?.WaitForExit(2000);
        }
        catch
        {
            // notify-send is optional; the error log and stderr already carry the message.
        }
    }
}
