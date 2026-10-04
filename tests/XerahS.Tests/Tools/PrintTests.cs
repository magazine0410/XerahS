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

using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Tasks.Processors;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux.Services;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.Tools;

[TestFixture, NonParallelizable]
public class PrintTests
{
    [TearDown]
    public void TearDown() => CaptureJobProcessor.PrintImageCallback = null;

    // Page units are hundredths of an inch, as in ShareX's GDI+ printing: A4 is 827 x 1169.
    [Test]
    public void Layout_SmallImage_KeepsItsSizeAtTheMargin_UnlessEnlargingIsAllowed()
    {
        var settings = new PrintSettings();
        var layout = PrintHelper.GetLayout(400, 300, 827, 1169, settings);
        Assert.That(layout.Rotate, Is.True, "A landscape image on a portrait page is rotated.");
        Assert.That(layout.Destination, Is.EqualTo(SKRect.Create(5, 5, 300, 400)));

        settings.AllowEnlargeImage = true;
        layout = PrintHelper.GetLayout(400, 300, 827, 1169, settings);
        Assert.That(layout.Destination.Width, Is.EqualTo(817).Within(0.01));
        Assert.That(layout.Destination.Height, Is.EqualTo(817 * 400 / 300f).Within(0.01));
    }

    [Test]
    public void Layout_LargeImage_ScalesDownToFit_AndCentersWhenAsked()
    {
        var settings = new PrintSettings { AutoRotateImage = false, CenterImage = true, Margin = 10 };
        var layout = PrintHelper.GetLayout(2000, 1000, 827, 1169, settings);
        Assert.That(layout.Rotate, Is.False);
        Assert.That(layout.Destination.Width, Is.EqualTo(807).Within(0.01));
        Assert.That(layout.Destination.Height, Is.EqualTo(403.5).Within(0.01));
        Assert.That(layout.Destination.Left, Is.EqualTo(10).Within(0.01));
        Assert.That(layout.Destination.MidY, Is.EqualTo(1169 / 2f).Within(0.01));
    }

    [Test]
    public void Layout_WithoutAutoScale_DrawsAtFullSizeCutOffAtTheMargin()
    {
        var settings = new PrintSettings { AutoScaleImage = false, AutoRotateImage = false };
        var layout = PrintHelper.GetLayout(2000, 500, 827, 1169, settings);
        Assert.That(layout.Destination, Is.EqualTo(SKRect.Create(5, 5, 817, 500)));
        Assert.That(layout.Source, Is.EqualTo(SKRect.Create(0, 0, 817, 500)));
    }

    [Test]
    public void Pdf_HasOnePageOfTheRequestedSize()
    {
        using var image = new SKBitmap(200, 100);
        image.Erase(SKColors.Red);
        byte[] pdf = PrintHelper.CreatePdf(image, new PrintSettings(), PrintPageSize.A4.WidthPoints, PrintPageSize.A4.HeightPoints, "Test");
        string text = Encoding.Latin1.GetString(pdf);
        Assert.That(text, Does.StartWith("%PDF"));
        Match box = Regex.Match(text, @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]");
        Assert.That(box.Success, Is.True);
        Assert.That(double.Parse(box.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(595.28).Within(1), "Skia writes whole points.");
        Assert.That(double.Parse(box.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(841.89).Within(1));
    }

    [Test]
    public void Pdf_StoresTheImageLosslessAtFullSize()
    {
        using var image = new SKBitmap(300, 200);
        using (var canvas = new SKCanvas(image))
        using (var font = new SKFont(SKTypeface.Default, 24))
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawText("Small text", 10, 40, SKTextAlign.Left, font, paint);
        }
        string text = Encoding.Latin1.GetString(PrintHelper.CreatePdf(image, new PrintSettings { AutoRotateImage = false }, 595, 842, "Test"));
        Assert.That(text, Does.Not.Contain("/DCTDecode"), "Screenshots must not be stored as JPEG.");
        Assert.That(text, Does.Match(@"/Width 300\b").And.Match(@"/Height 200\b"));
    }

    [Test]
    public void Preview_IsAWhitePage_WithTheImageInside()
    {
        using var image = new SKBitmap(400, 300);
        image.Erase(SKColors.Blue);
        using var preview = PrintHelper.RenderPreview(image, new PrintSettings { AutoRotateImage = false, AllowEnlargeImage = true },
            PrintPageSize.A4.WidthPoints, PrintPageSize.A4.HeightPoints, maxSize: 1169);
        Assert.That((preview.Width, preview.Height), Is.EqualTo((827, 1169)));
        Assert.That(preview.GetPixel(400, 1000), Is.EqualTo(SKColors.White));
        Assert.That(preview.GetPixel(400, 100), Is.EqualTo(SKColors.Blue));
    }

    [TestCase("copies=1 media=iso_a4_210x297mm sides=one-sided", 595.28, 841.89)]
    [TestCase("media=na_letter_8.5x11in", 612, 792)]
    [TestCase("job-sheets=none,none media=Letter", 612, 792)]
    [TestCase("media='iso_a5_148x210mm'", 419.53, 595.28)]
    public void LpoptionsMedia_GivesThePaperSize(string output, double width, double height)
    {
        PrintPageSize? size = LinuxPrintService.ParseMediaSize(output);
        Assert.That(size, Is.Not.Null);
        Assert.That(size!.Value.WidthPoints, Is.EqualTo(width).Within(0.1));
        Assert.That(size.Value.HeightPoints, Is.EqualTo(height).Within(0.1));
    }

    [Test]
    public void PortalPageSetup_AppliesTheOrientation()
    {
        var landscape = LinuxPrintService.ParsePageSetup(new Dictionary<string, object> { ["Width"] = 210.0, ["Height"] = 297.0, ["Orientation"] = "landscape" });
        var portrait = LinuxPrintService.ParsePageSetup(new Dictionary<string, object> { ["Width"] = 210.0, ["Height"] = 297.0, ["Orientation"] = "portrait" });
        Assert.That(landscape!.Value.WidthPoints, Is.EqualTo(841.89).Within(0.1));
        Assert.That(portrait!.Value.WidthPoints, Is.EqualTo(595.28).Within(0.1));
        Assert.That(LinuxPrintService.ParsePageSetup(new Dictionary<string, object>()), Is.Null);
    }

    [Test]
    public async Task WithoutTheDialog_PrintsWithLp_AndFallsBackToTheDefaultForAnUnknownOverride()
    {
        var calls = new List<string>();
        string? pdfPath = null;
        var service = new LinuxPrintService((command, args, _) =>
        {
            calls.Add(command + " " + string.Join(' ', args));
            if (command == "lp") pdfPath = args[^1];
            return Task.FromResult(command switch
            {
                "lpstat" when args[0] == "-p" => new LinuxPrintService.CommandResult(1, "", "Invalid destination name"),
                "lpstat" when args[0] == "-d" => new LinuxPrintService.CommandResult(0, "system default destination: Office\n", ""),
                "lpoptions" => new LinuxPrintService.CommandResult(0, "media=na_letter_8.5x11in", ""),
                _ => new LinuxPrintService.CommandResult(0, "request id is Office-1", "")
            });
        }, hasPortal: () => true, hasCommand: _ => true);

        PrintPageSize? rendered = null;
        var result = await service.PrintAsync("XerahS image", page => { rendered = page; return [1, 2, 3]; }, showDialog: false, printerName: "Missing");

        Assert.Multiple(() =>
        {
            Assert.That(result.Printed, Is.True);
            Assert.That(result.Warning, Does.Contain("\"Missing\" does not exist"));
            Assert.That(rendered, Is.EqualTo(PrintPageSize.Letter));
            Assert.That(calls.Last(), Does.StartWith("lp -d Office -t XerahS image -- "));
            Assert.That(File.Exists(pdfPath), Is.False, "The temporary PDF is removed after lp has queued it.");
        });
    }

    [Test]
    public void WithoutAnyPrinter_ReportsIt()
    {
        var service = new LinuxPrintService((command, args, _) => Task.FromResult(command == "lpstat"
                ? new LinuxPrintService.CommandResult(command == "lpstat" && args[0] == "-d" ? 0 : 1, "no system default destination", "")
                : new LinuxPrintService.CommandResult(0, "", "")),
            hasPortal: () => false, hasCommand: _ => true);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.PrintAsync("t", _ => [], showDialog: false, printerName: null));
        Assert.That(error!.Message, Does.Contain("No printer is set up"));
    }

    [Test]
    public void WithoutPortalOrCups_PrintingIsUnavailable()
    {
        var service = new LinuxPrintService((_, _, _) => Task.FromResult(new LinuxPrintService.CommandResult(0, "", "")), () => false, _ => false);
        Assert.That(service.IsSupported, Is.False);
        Assert.That(service.UnavailableMessage, Does.Contain("xdg-desktop-portal"));
    }

    [Test]
    public void PrintOptions_WriteToTheSettings_LikeShareX()
    {
        var settings = new PrintSettings { ShowPrintDialog = true };
        var viewModel = new PrintOptionsViewModel(settings, previewOnly: true) { Margin = 2000, AutoScaleImage = false };
        Assert.That(settings.Margin, Is.EqualTo(1000));
        Assert.That(settings.AutoScaleImage, Is.False);
        Assert.That(viewModel.CanPrint, Is.False);
        Assert.That(viewModel.PrintButtonText, Is.EqualTo("Print..."));
        settings.ShowPrintDialog = false;
        Assert.That(new PrintOptionsViewModel(settings, false).PrintButtonText, Is.EqualTo("Print"));
    }

    [Test]
    public async Task AfterCapture_PrintImage_PrintsTheCapture()
    {
        SKBitmap? printed = null;
        CaptureJobProcessor.PrintImageCallback = image => { printed = image; return Task.CompletedTask; };
        using var image = new SKBitmap(10, 10);
        var info = new TaskInfo(new TaskSettings { AfterCaptureJob = AfterCaptureTasks.SendImageToPrinter }) { Metadata = new(image) };

        Assert.That(await new CaptureJobProcessor().ProcessAsync(info, default), Is.True);
        Assert.That(printed, Is.SameAs(info.Metadata.Image));
    }
}
