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
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux.Capture;

namespace XerahS.Platform.Linux.Services;

/// <summary>
/// Printing on Linux. With the print dialog, the desktop portal's Print interface shows the desktop's dialog
/// (KDE's on Plasma) and prints the PDF. Without it, the PDF goes straight to a CUPS printer with lp, which is
/// how ShareX's "Don't show print dialog" and "Default printer override" work.
/// </summary>
public sealed class LinuxPrintService : IPrintService
{
    private const string PortalBusName = "org.freedesktop.portal.Desktop";
    private const string PortalObjectPath = "/org/freedesktop/portal/desktop";
    private const string PortalInterface = "org.freedesktop.portal.Print";

    private readonly Func<string, string[], CancellationToken, Task<CommandResult>> _run;
    private readonly Func<bool> _hasPortal;
    private readonly Func<string, bool> _hasCommand;

    public LinuxPrintService() : this(RunCommandAsync, () => PortalInterfaceChecker.HasInterface(PortalInterface), CommandExists) { }

    internal LinuxPrintService(Func<string, string[], CancellationToken, Task<CommandResult>> run, Func<bool> hasPortal, Func<string, bool> hasCommand)
    {
        _run = run;
        _hasPortal = hasPortal;
        _hasCommand = hasCommand;
    }

    internal sealed record CommandResult(int ExitCode, string Output, string Error);

    public bool IsSupported => _hasPortal() || _hasCommand("lp");

    public string? UnavailableMessage => IsSupported ? null :
        "Printing needs the desktop portal's Print interface (xdg-desktop-portal) or CUPS (the lp command).";

    public async Task<PrintPageSize> GetDefaultPageSizeAsync(string? printerName, CancellationToken cancellationToken = default)
    {
        if (_hasCommand("lpoptions"))
        {
            string? printer = await ResolvePrinterAsync(printerName, cancellationToken);
            if (printer != null)
            {
                var options = await _run("lpoptions", ["-p", printer], cancellationToken);
                if (options.ExitCode == 0 && ParseMediaSize(options.Output) is { } size) return size;
            }
        }

        return GetLocaleDefaultPageSize();
    }

    public async Task<PrintResult> PrintAsync(string title, Func<PrintPageSize, byte[]> createPdf, bool showDialog, string? printerName,
        CancellationToken cancellationToken = default)
    {
        bool hasLp = _hasCommand("lp");
        bool hasPortal = _hasPortal();
        if (!hasLp && !hasPortal) throw new InvalidOperationException(UnavailableMessage);

        // Inside Flatpak there is no lp; the portal is the only way to print.
        if (showDialog || !hasLp)
        {
            if (!hasPortal) throw new InvalidOperationException("The print dialog needs the desktop portal's Print interface (xdg-desktop-portal).");
            return await PrintWithPortalAsync(title, createPdf, cancellationToken);
        }

        return await PrintWithLpAsync(title, createPdf, printerName, cancellationToken);
    }

    private async Task<PrintResult> PrintWithLpAsync(string title, Func<PrintPageSize, byte[]> createPdf, string? printerName, CancellationToken token)
    {
        string? warning = null;
        string? printer = null;
        if (!string.IsNullOrWhiteSpace(printerName))
        {
            if ((await _run("lpstat", ["-p", printerName.Trim()], token)).ExitCode == 0) printer = printerName.Trim();
            else warning = $"Printer \"{printerName.Trim()}\" does not exist. Continuing with the default printer. " +
                "You can set the default printer override in the application settings.";
        }

        printer ??= await GetDefaultPrinterAsync(token)
            ?? throw new InvalidOperationException("No printer is set up. Add one in the system's printer settings.");

        PrintPageSize page = await GetDefaultPageSizeAsync(printer, token);
        string path = WriteTemporaryPdf(createPdf(page));
        try
        {
            var result = await _run("lp", ["-d", printer, "-t", title, "--", path], token);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"Printing to \"{printer}\" failed: {FirstLine(result.Error, result.Output)}");
            DebugHelper.WriteLine($"Print: sent to {printer} with lp ({result.Output.Trim()}).");
            return new PrintResult(true, warning);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static async Task<PrintResult> PrintWithPortalAsync(string title, Func<PrintPageSize, byte[]> createPdf, CancellationToken token)
    {
        using var connection = new Connection(Address.Session);
        ConnectionInfo info = await connection.ConnectAsync().ConfigureAwait(false);
        await PortalHostRegistry.RegisterAsync(connection).ConfigureAwait(false);
        PortalRequestExtensions.CacheLocalConnectionName(connection, info);
        var portal = connection.CreateProxy<IPrintPortal>(PortalBusName, PortalObjectPath);

        var prepareOptions = new Dictionary<string, object>
        {
            ["handle_token"] = $"xerahs_print_{Guid.NewGuid():N}",
            ["modal"] = true
        };
        var (prepareResponse, prepareResults) = await connection.SendPortalRequestAsync(PortalBusName, prepareOptions,
            () => portal.PreparePrintAsync(string.Empty, title, new Dictionary<string, object>(), new Dictionary<string, object>(), prepareOptions),
            token).ConfigureAwait(false);
        if (prepareResponse == 1) return new PrintResult(false);
        if (prepareResponse != 0) throw new InvalidOperationException($"The print dialog failed (portal response {prepareResponse}).");

        prepareResults.TryGetResult("page-setup", out IDictionary<string, object>? pageSetup);
        prepareResults.TryGetResult("token", out uint printToken);
        PrintPageSize page = ParsePageSetup(pageSetup) ?? GetLocaleDefaultPageSize();

        string path = WriteTemporaryPdf(createPdf(page));
        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
            var printOptions = new Dictionary<string, object>
            {
                ["handle_token"] = $"xerahs_print_{Guid.NewGuid():N}",
                ["modal"] = true,
                ["token"] = printToken
            };
            var (printResponse, _) = await connection.SendPortalRequestAsync(PortalBusName, printOptions,
                () => portal.PrintAsync(string.Empty, title, file, printOptions), token).ConfigureAwait(false);
            if (printResponse == 1) return new PrintResult(false);
            if (printResponse != 0) throw new InvalidOperationException($"Printing failed (portal response {printResponse}).");
            DebugHelper.WriteLine($"Print: sent through the portal ({page.WidthPoints:0}x{page.HeightPoints:0} pt).");
            return new PrintResult(true);
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>The portal's page setup gives the paper in millimetres for portrait, and the orientation separately.</summary>
    internal static PrintPageSize? ParsePageSetup(IDictionary<string, object>? pageSetup)
    {
        if (pageSetup == null || !pageSetup.TryGetResult("Width", out double width) || !pageSetup.TryGetResult("Height", out double height) ||
            width <= 0 || height <= 0)
        {
            return null;
        }

        pageSetup.TryGetResult("Orientation", out string? orientation);
        bool landscape = orientation is "landscape" or "reverse_landscape";
        return landscape ? PrintPageSize.FromMillimeters(Math.Max(width, height), Math.Min(width, height))
            : PrintPageSize.FromMillimeters(Math.Min(width, height), Math.Max(width, height));
    }

    /// <summary>Reads "media=..." from lpoptions, for example iso_a4_210x297mm, na_letter_8.5x11in or A4.</summary>
    internal static PrintPageSize? ParseMediaSize(string lpoptionsOutput)
    {
        Match media = Regex.Match(lpoptionsOutput, @"(?:^|\s)media=(?:'([^']*)'|""([^""]*)""|(\S+))");
        if (!media.Success) return null;
        string value = (media.Groups[1].Success ? media.Groups[1].Value : media.Groups[2].Success ? media.Groups[2].Value : media.Groups[3].Value)
            .Split(',')[0];

        Match size = Regex.Match(value, @"_(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)(mm|in)$", RegexOptions.IgnoreCase);
        if (size.Success)
        {
            double width = double.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
            double height = double.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
            return size.Groups[3].Value.Equals("in", StringComparison.OrdinalIgnoreCase)
                ? new PrintPageSize(width * 72, height * 72)
                : PrintPageSize.FromMillimeters(width, height);
        }

        return value.ToLowerInvariant() switch
        {
            "a3" => PrintPageSize.FromMillimeters(297, 420),
            "a4" => PrintPageSize.A4,
            "a5" => PrintPageSize.FromMillimeters(148, 210),
            "letter" => PrintPageSize.Letter,
            "legal" => new PrintPageSize(612, 1008),
            _ => null
        };
    }

    /// <summary>"system default destination: Name" from lpstat -d.</summary>
    internal static string? ParseDefaultPrinter(string lpstatOutput)
    {
        Match match = Regex.Match(lpstatOutput, @"system default destination:\s*(\S+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<string?> ResolvePrinterAsync(string? printerName, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(printerName) && (await _run("lpstat", ["-p", printerName.Trim()], token)).ExitCode == 0)
            return printerName.Trim();
        return await GetDefaultPrinterAsync(token);
    }

    private async Task<string?> GetDefaultPrinterAsync(CancellationToken token)
    {
        if (!_hasCommand("lpstat")) return null;
        var defaultResult = await _run("lpstat", ["-d"], token);
        if (ParseDefaultPrinter(defaultResult.Output) is { } printer) return printer;

        // No default set: use the only or first printer, as the print dialog would preselect it.
        var destinations = await _run("lpstat", ["-e"], token);
        return destinations.ExitCode == 0
            ? destinations.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            : null;
    }

    private static PrintPageSize GetLocaleDefaultPageSize()
    {
        try
        {
            return RegionInfo.CurrentRegion.IsMetric ? PrintPageSize.A4 : PrintPageSize.Letter;
        }
        catch (ArgumentException)
        {
            return PrintPageSize.A4;
        }
    }

    private static string WriteTemporaryPdf(byte[] pdf)
    {
        string path = Path.Combine(Path.GetTempPath(), $"xerahs-print-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Delete temporary print file"); }
    }

    private static string FirstLine(params string[] texts) =>
        texts.Select(text => text.Trim()).FirstOrDefault(text => text.Length > 0)?.Split('\n')[0] ?? "unknown error";

    private static bool CommandExists(string command) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, command)));

    private static async Task<CommandResult> RunCommandAsync(string command, string[] arguments, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        // English output, so lpstat can be parsed.
        startInfo.Environment["LC_ALL"] = "C";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {command}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return new CommandResult(process.ExitCode, await output, await error);
    }
}

[DBusInterface("org.freedesktop.portal.Print")]
public interface IPrintPortal : IDBusObject
{
    Task<ObjectPath> PreparePrintAsync(string parentWindow, string title, IDictionary<string, object> settings,
        IDictionary<string, object> pageSetup, IDictionary<string, object> options);

    Task<ObjectPath> PrintAsync(string parentWindow, string title, SafeFileHandle fd, IDictionary<string, object> options);
}
