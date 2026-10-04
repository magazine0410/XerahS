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

using NUnit.Framework;
using SkiaSharp;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux;

namespace XerahS.Tests.Platform.Linux;

[TestFixture]
public class LinuxOcrServiceTests
{
    [TestCase("en", "eng")]
    [TestCase("en-US", "eng")]
    [TestCase("fi", "fin")]
    [TestCase("de", "deu")]
    [TestCase("ja", "jpn")]
    [TestCase("zh-Hans", "chi_sim")]
    [TestCase("zh-TW", "chi_tra")]
    [TestCase("nb", "nor")]
    [TestCase("", "eng")]
    [TestCase("script/Latin", "script/Latin")]
    public void LanguageTags_MapToTesseractCodes(string tag, string expected)
    {
        Assert.That(LinuxOcrService.ToTesseractCode(tag, ["script/Latin"]), Is.EqualTo(expected));
    }

    [TestCase("eng", "en")]
    [TestCase("fin", "fi")]
    [TestCase("chi_sim", "zh-Hans")]
    [TestCase("jpn_vert", "jpn_vert")]
    public void TesseractCodes_MapBackToTheStoredTags(string code, string expected)
    {
        Assert.That(LinuxOcrService.ToLanguageTag(code), Is.EqualTo(expected));
        Assert.That(LinuxOcrService.ToTesseractCode(LinuxOcrService.ToLanguageTag(code), [code]), Is.EqualTo(code));
    }

    [Test]
    public void Languages_ComeFromTheTessdataFolders_WithoutOsdAndEquationData()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xerahs-tessdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "script"));
        try
        {
            foreach (string name in new[] { "eng", "fin", "osd", "equ", "script/Latin" })
                File.WriteAllText(Path.Combine(directory, name + ".traineddata"), "");

            var languages = new LinuxOcrService(() => [directory]).GetAvailableLanguages();

            Assert.That(languages.Select(language => language.LanguageTag), Is.EquivalentTo(new[] { "en", "fi", "script/Latin" }));
            Assert.That(languages.Single(language => language.LanguageTag == "script/Latin").DisplayName, Is.EqualTo("Latin (script)"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task MissingLanguageData_ReportsThePackageToInstall()
    {
        var service = new LinuxOcrService(() => []);
        if (!service.IsSupported) Assert.Ignore("Tesseract is not installed.");
        using var image = new SKBitmap(10, 10);
        var result = await service.RecognizeAsync(image, new OcrOptions { Language = "fi" });
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("tesseract-langpack-fin"));
    }

    [Test]
    public void PrepareImage_FlattensTransparencyOnWhite_AndScales()
    {
        using var image = new SKBitmap(new SKImageInfo(4, 3, SKColorType.Bgra8888, SKAlphaType.Premul));
        image.Erase(SKColors.Transparent);
        using var prepared = LinuxOcrService.PrepareImage(image, 2f);
        Assert.That((prepared.Width, prepared.Height), Is.EqualTo((8, 6)));
        Assert.That(prepared.GetPixel(3, 3), Is.EqualTo(SKColors.White));
    }

    [Test]
    public async Task RecognizesRenderedText_WithTheSystemTesseract()
    {
        var service = new LinuxOcrService();
        if (!service.IsSupported || !service.GetAvailableLanguages().Any(language => language.LanguageTag == "en"))
            Assert.Ignore("Tesseract or its English data is not installed.");

        using var image = new SKBitmap(520, 90);
        using (var canvas = new SKCanvas(image))
        using (var font = new SKFont(SKTypeface.Default, 48))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("Hello XerahS 2026", 16, 62, SKTextAlign.Left, font, paint);
        }

        var result = await service.RecognizeAsync(image, new OcrOptions { Language = "en", ScaleFactor = 1f, SingleLine = true });

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        Assert.That(result.Text, Does.Contain("Hello").And.Contain("2026"));
    }
}
