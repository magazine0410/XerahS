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

namespace XerahS.Platform.Abstractions;

/// <summary>A page size in points (1/72 inch), with the orientation already applied.</summary>
public readonly record struct PrintPageSize(double WidthPoints, double HeightPoints)
{
    public static PrintPageSize FromMillimeters(double width, double height) => new(width / 25.4 * 72, height / 25.4 * 72);
    public static readonly PrintPageSize A4 = FromMillimeters(210, 297);
    public static readonly PrintPageSize Letter = new(612, 792);
}

/// <param name="Printed">False when the user cancelled the print dialog.</param>
/// <param name="Warning">For example that the printer override does not exist and the default printer was used.</param>
public sealed record PrintResult(bool Printed, string? Warning = null);

/// <summary>Prints PDF pages. The caller renders the page once the page size is known.</summary>
public interface IPrintService
{
    bool IsSupported { get; }

    string? UnavailableMessage => null;

    /// <summary>The default paper size of the printer used without the print dialog, for the preview.</summary>
    Task<PrintPageSize> GetDefaultPageSizeAsync(string? printerName, CancellationToken cancellationToken = default);

    /// <param name="createPdf">Renders a one-page PDF for the page size the user or the printer chose.</param>
    /// <param name="showDialog">Show the system print dialog; otherwise print to <paramref name="printerName"/> or the default printer.</param>
    Task<PrintResult> PrintAsync(string title, Func<PrintPageSize, byte[]> createPdf, bool showDialog, string? printerName,
        CancellationToken cancellationToken = default);
}
