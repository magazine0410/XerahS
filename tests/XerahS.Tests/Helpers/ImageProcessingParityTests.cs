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

using System.Buffers.Binary;
using BitMiracle.LibTiff.Classic;
using Newtonsoft.Json;
using NUnit.Framework;
using ShareX.ImageEditor.Core.ImageEffects.Adjustments;
using SkiaSharp;
using XerahS.Common;
using XerahS.Common.Helpers;
using XerahS.Core;
using XerahS.Core.Helpers;
using XerahS.Core.Hotkeys;
using TaskHelpers = XerahS.Core.TaskHelpers;
using XerahS.Core.Tasks.Processors;
using XerahS.Services.Abstractions;

namespace XerahS.Tests.Helpers;

[TestFixture, NonParallelizable]
public class ImageProcessingParityTests
{
    private string _directory = null!;
    [SetUp] public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-image-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }
    [TearDown] public void Cleanup() => Directory.Delete(_directory, true);

    [Test]
    public void Presets_MigrateSinglePreset_AndRoundTripTheListWithoutAppendingDefaults()
    {
        var legacy = new { ImageEffectsPreset = new ImageEffectPreset
        {
            Name = "Saved effect", Effects = [new BrightnessImageEffect { Amount = 12, Enabled = false }]
        }};
        string json = JsonConvert.SerializeObject(legacy, ImageEffectPresetSerializer.CreateSerializerSettings());
        var settings = JsonConvert.DeserializeObject<TaskSettingsImage>(json, ImageEffectPresetSerializer.CreateSerializerSettings())!;
        Assert.That(settings.ImageEffectPresets, Has.Count.EqualTo(1));
        Assert.That(settings.ImageEffectsPreset.Name, Is.EqualTo("Saved effect"));
        settings.ImageEffectPresets.Add(new ImageEffectPreset { Name = "Second" });
        settings.SelectedImageEffectPreset = 1;
        settings.UseRandomImageEffect = true;
        string saved = JsonConvert.SerializeObject(settings, ImageEffectPresetSerializer.CreateSerializerSettings());
        Assert.That(saved, Does.Not.Contain("\"ImageEffectsPreset\""));
        var copy = JsonConvert.DeserializeObject<TaskSettingsImage>(saved, ImageEffectPresetSerializer.CreateSerializerSettings())!;
        Assert.Multiple(() =>
        {
            Assert.That(copy.ImageEffectPresets, Has.Count.EqualTo(2));
            Assert.That(copy.ImageEffectsPreset.Name, Is.EqualTo("Second"));
            Assert.That(copy.UseRandomImageEffect, Is.True);
            Assert.That(copy.ImageEffectPresets[0].Effects.Single().Enabled, Is.False);
        });
    }

    [TestCase("xsie")]
    [TestCase("sxie")]
    public void DisabledEffects_SurviveExportImport_AndCanBeEnabledLater(string extension)
    {
        var preset = new ImageEffectPreset { Name = "Toggle", Effects = [new BrightnessImageEffect { Amount = 20, Enabled = false }] };
        string path = Path.Combine(_directory, "Toggle." + extension);
        if (extension == "xsie") ImageEffectPresetSerializer.SaveXsieFile(path, preset);
        else Assert.That(LegacyImageEffectExporter.ExportSxieFile(path, preset.Name, preset.Effects).Success, Is.True);
        var imported = ImageEffectPresetImporter.LoadPresetFile(path, out var skipped)!;
        Assert.That(skipped, Is.Empty);
        Assert.That(imported.Effects, Has.Count.EqualTo(1));
        Assert.That(imported.Effects[0].Enabled, Is.False);
        using var source = new SKBitmap(1, 1);
        source.Erase(new SKColor(40, 40, 40));
        Assert.That(TaskHelpers.ApplyImageEffectPreset(source, imported), Is.SameAs(source));
        imported.Effects[0].Enabled = true;
        using var result = TaskHelpers.ApplyImageEffectPreset(source, imported);
        Assert.That(result.GetPixel(0, 0).Red, Is.GreaterThan(40));
        Assert.That(source.GetPixel(0, 0).Red, Is.EqualTo(40));
    }

    [Test]
    public void AutomationImportAndClear_ReplaceAllPresetsRatherThanLeavingRandomCandidates()
    {
        var workflow = new WorkflowSettings { TaskSettings = new TaskSettings { UseDefaultImageSettings = false } };
        workflow.TaskSettings.ImageSettings.ImageEffectPresets.Add(new ImageEffectPreset { Name = "Old" });
        workflow.TaskSettings.ImageSettings.SelectedImageEffectPreset = 1;
        workflow.TaskSettings.ImageSettings.UseRandomImageEffect = true;
        string path = Path.Combine(_directory, "import.xsie");
        ImageEffectPresetSerializer.SaveXsieFile(path, new ImageEffectPreset { Effects = [new BrightnessImageEffect { Amount = 10 }] });
        XerahS.Core.Automation.WorkflowAutomation.ImportImageEffects(workflow, path, true);
        Assert.That(workflow.TaskSettings.ImageSettings.ImageEffectPresets, Has.Count.EqualTo(1));
        Assert.That(workflow.TaskSettings.ImageSettings.SelectedImageEffectPreset, Is.Zero);
        workflow.TaskSettings.ImageSettings.ImageEffectPresets.Add(new ImageEffectPreset { Effects = [new BrightnessImageEffect { Amount = 20 }] });
        XerahS.Core.Automation.WorkflowAutomation.ClearImageEffects(workflow);
        Assert.That(workflow.TaskSettings.ImageSettings.ImageEffectPresets, Has.Count.EqualTo(1));
        Assert.That(workflow.TaskSettings.ImageSettings.ImageEffectsPreset.Effects, Is.Empty);
    }

    [Test]
    public void RandomPreset_UsesTheWholeList_AndDoesNotChangeSelection()
    {
        var settings = new TaskSettingsImage { SelectedImageEffectPreset = 99, UseRandomImageEffect = true,
            ImageEffectPresets = [new() { Effects = [new BrightnessImageEffect { Amount = 10 }] },
                                 new() { Effects = [new BrightnessImageEffect { Amount = 40 }] }] };
        using var source = new SKBitmap(1, 1);
        source.Erase(new SKColor(50, 50, 50));
        var allowed = settings.ImageEffectPresets.Select(preset =>
        {
            using var result = TaskHelpers.ApplyImageEffectPreset(source, preset);
            return result.GetPixel(0, 0);
        }).ToHashSet();
        var seen = new HashSet<SKColor>();
        for (int i = 0; i < 100; i++)
        {
            using var result = TaskHelpers.ApplyImageEffects(source, settings)!;
            Assert.That(allowed.Contains(result.GetPixel(0, 0)), Is.True);
            seen.Add(result.GetPixel(0, 0));
        }
        Assert.That(seen.Count, Is.EqualTo(2));
        Assert.That(settings.SelectedImageEffectPreset, Is.EqualTo(99));
        settings.UseRandomImageEffect = false;
        Assert.That(TaskHelpers.ApplyImageEffects(source, settings), Is.SameAs(source));
    }

    [TestCase(WorkflowType.RectangleRegion, true)]
    [TestCase(WorkflowType.RectangleTransparent, true)]
    [TestCase(WorkflowType.LastRegion, true)]
    [TestCase(WorkflowType.CustomRegion, false)]
    [TestCase(WorkflowType.ActiveMonitor, false)]
    [TestCase(WorkflowType.ActiveWindow, false)]
    [TestCase(WorkflowType.PrintScreen, false)]
    [TestCase(WorkflowType.ScrollingCapture, true)]
    [TestCase(WorkflowType.AutoCapture, true)]
    [TestCase(WorkflowType.ClipboardUpload, true)]
    [TestCase(WorkflowType.FileUpload, true)]
    public void RegionOnly_MatchesShareXCaptureTypes_WithoutSuppressingImageUploads(WorkflowType job, bool expected)
    {
        var settings = new TaskSettings { Job = job };
        settings.ImageSettings.ImageEffectOnlyRegionCapture = true;
        Assert.That(CaptureJobProcessor.ShouldApplyImageEffects(settings), Is.EqualTo(expected));
    }

    [Test]
    public async Task EffectsWindow_IsAwaitedBeforeApplyingTheSelectedPreset_AndSkippedForNonRegions()
    {
        var original = CaptureJobProcessor.ShowImageEffectsCallback;
        var settings = new TaskSettings { Job = WorkflowType.RectangleRegion, AfterCaptureJob = AfterCaptureTasks.AddImageEffects };
        settings.ImageSettings.ShowImageEffectsWindowAfterCapture = true;
        settings.ImageSettings.ImageEffectOnlyRegionCapture = true;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var image = new SKBitmap(1, 1);
        image.Erase(new SKColor(30, 30, 30));
        var info = new TaskInfo(settings) { Metadata = new TaskMetadata(image.Copy()) };
        try
        {
            CaptureJobProcessor.ShowImageEffectsCallback = async (task, token) =>
            {
                opened.SetResult();
                await closed.Task.WaitAsync(token);
                task.TaskSettings.ImageSettings.ImageEffectsPreset.Effects.Add(new BrightnessImageEffect { Amount = 25 });
            };
            var processing = new CaptureJobProcessor().ProcessAsync(info, CancellationToken.None);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(processing.IsCompleted, Is.False);
            closed.SetResult();
            Assert.That(await processing, Is.True);
            Assert.That(info.Metadata.Image!.GetPixel(0, 0).Red, Is.GreaterThan(30));
            settings.Job = WorkflowType.PrintScreen;
            CaptureJobProcessor.RestrictImageEffectsForCapture(settings);
            CaptureJobProcessor.ShowImageEffectsCallback = (_, _) => throw new AssertionException("The dialog must not open for fullscreen captures.");
            Assert.That(await new CaptureJobProcessor().ProcessAsync(info, CancellationToken.None), Is.True);
        }
        finally { closed.TrySetResult(); info.Metadata.Dispose(); CaptureJobProcessor.ShowImageEffectsCallback = original; }
    }

    [Test]
    public void AutomaticJpeg_UsesEncodedSizeInsteadOfPixels_AndLeavesExplicitJpegQualityAlone()
    {
        using var source = new SKBitmap(1600, 1200);
        source.Erase(SKColors.White);
        var settings = new TaskSettings();
        settings.ImageSettings.ImageAutoUseJPEGSize = 100;
        using var png = TaskHelpers.PrepareImage(source, settings);
        Assert.That(png.Format, Is.EqualTo(EImageFormat.PNG));
        settings.ImageSettings.ImageFormat = EImageFormat.JPEG;
        settings.ImageSettings.ImageJPEGQuality = 42;
        settings.ImageSettings.ImageAutoJPEGQuality = true;
        settings.ImageSettings.ImageAutoUseJPEGSize = 0;
        using var jpeg = TaskHelpers.PrepareImage(source, settings);
        using var expected = TaskHelpers.SaveImageAsStream(source, EImageFormat.JPEG, jpegQuality: 42)!;
        Assert.That(jpeg.Stream.ToArray(), Is.EqualTo(expected.ToArray()));
    }

    [Test]
    public async Task AutomaticJpeg_ChoosesShareXQualityStepsAndFloor_AndUploadsWithJpegName()
    {
        using var source = new SKBitmap(128, 128);
        var random = new Random(17);
        source.Pixels = Enumerable.Range(0, 128 * 128).Select(_ => new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))).ToArray();
        using var at90 = TaskHelpers.SaveImageAsStream(source, EImageFormat.JPEG, jpegQuality: 90)!;
        int limit = (int)(at90.Length / 1000);
        var settings = new TaskSettings();
        settings.ImageSettings.ImageAutoJPEGQuality = true;
        settings.ImageSettings.ImageAutoUseJPEGSize = limit;
        using var prepared = TaskHelpers.PrepareImage(source, settings);
        Assert.That(prepared.Format, Is.EqualTo(EImageFormat.JPEG));
        for (int quality = 100; quality >= 70; quality -= 2)
        {
            using var attempt = TaskHelpers.SaveImageAsStream(source, EImageFormat.JPEG, jpegQuality: quality)!;
            if (attempt.Length <= limit * 1000L || quality == 70)
            {
                Assert.That(prepared.Stream.ToArray(), Is.EqualTo(attempt.ToArray()));
                break;
            }
        }
        settings.ImageSettings.ImageAutoUseJPEGSize = 0;
        using var floor = TaskHelpers.PrepareImage(source, settings);
        using var at70 = TaskHelpers.SaveImageAsStream(source, EImageFormat.JPEG, jpegQuality: 70)!;
        Assert.That(floor.Stream.ToArray(), Is.EqualTo(at70.ToArray()));
        var info = new TaskInfo(settings) { Metadata = new TaskMetadata(source), DataType = EDataType.Image };
        info.SetFileName("capture.png");
        var upload = await UploadJobProcessor.OpenUploadContentAsync(info);
        using var content = upload.Content;
        Assert.That(upload.FileName, Is.EqualTo("capture.jpg"));
        Assert.That(info.FileName, Is.EqualTo("capture.jpg"));
        Assert.That(settings.ImageSettings.ImageFormat, Is.EqualTo(EImageFormat.PNG));
    }

    [Test]
    public void PngStripping_RemovesOnlyColorChunks_PreservesEverythingElseAndRejectsTruncation()
    {
        using var source = new SKBitmap(2, 3);
        source.Erase(new SKColor(10, 20, 30, 90));
        using var data = source.Encode(SKEncodedImageFormat.Png, 100);
        var png = data.ToArray();
        using var input = new MemoryStream();
        input.Write(png.AsSpan(0, 8));
        foreach (var name in new[] { "gAMA", "cHRM", "sRGB", "iCCP" })
        {
            input.Write(new byte[4]);
            input.Write(System.Text.Encoding.ASCII.GetBytes(name));
            input.Write(new byte[4]);
        }
        input.Write(png.AsSpan(8));
        using var stripped = ImageHelpers.PNGStripColorSpaceInformation(input);
        using var expected = ImageHelpers.PNGStripColorSpaceInformation(new MemoryStream(png));
        Assert.That(stripped.ToArray(), Is.EqualTo(expected.ToArray()));
        Assert.That(stripped.Position, Is.Zero);
        using var decoded = ImageHelpers.LoadBitmap(stripped);
        Assert.That(decoded!.GetPixel(0, 0).Alpha, Is.EqualTo(source.GetPixel(0, 0).Alpha));
        using var truncated = new MemoryStream(png[..^1]);
        Assert.Throws<InvalidDataException>(() => ImageHelpers.PNGStripColorSpaceInformation(truncated));
    }

    [Test]
    public void PngStripping_TaskEncoderReadsTheApplicationSetting()
    {
        bool previous = SettingsManager.Settings.PNGStripColorSpaceInformation;
        try
        {
            using var colorSpace = SKColorSpace.CreateSrgbLinear();
            using var bitmap = new SKBitmap(new SKImageInfo(2, 3, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
            bitmap.Erase(SKColors.Red);
            SettingsManager.Settings.PNGStripColorSpaceInformation = false;
            using var original = TaskHelpers.SaveImageAsStream(bitmap, EImageFormat.PNG)!;
            SettingsManager.Settings.PNGStripColorSpaceInformation = true;
            using var stripped = TaskHelpers.SaveImageAsStream(bitmap, EImageFormat.PNG)!;
            using var expected = ImageHelpers.PNGStripColorSpaceInformation(original);
            Assert.That(stripped.ToArray(), Is.EqualTo(expected.ToArray()));
            Assert.That(stripped.Length, Is.LessThan(original.Length), "The fixture must contain a color-space chunk.");
        }
        finally { SettingsManager.Settings.PNGStripColorSpaceInformation = previous; }
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void ClipboardFill_UsesWhiteAndHonorsAlternativeMode(bool fill, bool alternative, bool expectedFill)
    {
        bool oldFill = HelpersOptions.DefaultCopyImageFillBackground, oldAlternative = HelpersOptions.UseAlternativeClipboardCopyImage;
        try
        {
            HelpersOptions.DefaultCopyImageFillBackground = fill;
            HelpersOptions.UseAlternativeClipboardCopyImage = alternative;
            using var source = new SKBitmap(1, 1);
            source.Erase(new SKColor(255, 0, 0, 128));
            using var result = ImageHelpers.CreateClipboardBackground(source);
            Assert.That(result != null, Is.EqualTo(expectedFill));
            if (result != null)
            {
                Assert.That(result.GetPixel(0, 0), Is.EqualTo(new SKColor(255, 127, 127)));
                Assert.That(source.GetPixel(0, 0).Alpha, Is.EqualTo(128));
            }
        }
        finally { HelpersOptions.DefaultCopyImageFillBackground = oldFill; HelpersOptions.UseAlternativeClipboardCopyImage = oldAlternative; }
    }

    [TestCase(1, "ABCDEF")]
    [TestCase(2, "BADCFE")]
    [TestCase(3, "FEDCBA")]
    [TestCase(4, "EFCDAB")]
    [TestCase(5, "ACEBDF")]
    [TestCase(6, "ECAFDB")]
    [TestCase(7, "FDBECA")]
    [TestCase(8, "BDFACE")]
    public void Exif_AllEightOrientations_AndDisabledRotation(int orientation, string order)
    {
        using var source = new SKBitmap(2, 3);
        source.Pixels = Enumerable.Range(0, 6).Select(i => new SKColor((byte)(20 + i * 35), (byte)(180 - i * 20), (byte)(i * 10))).ToArray();
        using var data = source.Encode(SKEncodedImageFormat.Jpeg, 100);
        byte[] rawJpeg = data.ToArray();
        using var raw = SKBitmap.Decode(rawJpeg);
        var exif = new byte[] { 0x45, 0x78, 0x69, 0x66, 0, 0, 0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0,
            0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0 };
        using var jpeg = new MemoryStream();
        jpeg.Write(rawJpeg.AsSpan(0, 2));
        jpeg.Write(new byte[] { 255, 225, 0, (byte)(exif.Length + 2) });
        jpeg.Write(exif);
        jpeg.Write(rawJpeg.AsSpan(2));
        jpeg.Position = 0;
        using var oriented = ImageHelpers.LoadBitmap(jpeg, true)!;
        Assert.That(oriented.Width, Is.EqualTo(orientation >= 5 ? 3 : 2));
        Assert.That(oriented.Pixels, Is.EqualTo(order.Select(c => raw.Pixels[c - 'A']).ToArray()));
        jpeg.Position = 0;
        using var unchanged = ImageHelpers.LoadBitmap(jpeg, false)!;
        Assert.That(unchanged.Pixels, Is.EqualTo(raw.Pixels));
    }

    [TestCase(1, 300, 400, 200)]
    [TestCase(6, 150, 200, 400)]
    [TestCase(1, 1500, 1600, 800)]
    public void ReducedJpegDecode_KeepsAtLeastTheRequestedOrientedWidth(int orientation, int minimumWidth, int width, int height)
    {
        using var source = new SKBitmap(1600, 800);
        source.Erase(SKColors.Teal);
        using var data = source.Encode(SKEncodedImageFormat.Jpeg, 90);
        byte[] rawJpeg = data.ToArray();
        var exif = new byte[] { 0x45, 0x78, 0x69, 0x66, 0, 0, 0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0,
            0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0 };
        using var jpeg = new MemoryStream();
        jpeg.Write(rawJpeg.AsSpan(0, 2));
        jpeg.Write(new byte[] { 255, 225, 0, (byte)(exif.Length + 2) });
        jpeg.Write(exif);
        jpeg.Write(rawJpeg.AsSpan(2));
        jpeg.Position = 0;
        using var reduced = ImageHelpers.LoadBitmap(jpeg, true, minimumWidth)!;
        Assert.That((reduced.Width, reduced.Height), Is.EqualTo((width, height)));
        Assert.That(Math.Abs(reduced.GetPixel(width / 2, height / 2).Green - SKColors.Teal.Green), Is.LessThan(4));
    }

    [TestCase(Compression.NONE, "wl")]
    [TestCase(Compression.LZW, "wb")]
    [TestCase(Compression.ADOBE_DEFLATE, "wl")]
    [TestCase(Compression.PACKBITS, "wb")]
    public void Tiff_DecodesCompressedFilesBothByteOrdersAndOrientation(Compression compression, string mode)
    {
        string path = Path.Combine(_directory, "external.tiff");
        using (var tiff = Tiff.Open(path, mode))
        {
            tiff.SetField(TiffTag.IMAGEWIDTH, 2);
            tiff.SetField(TiffTag.IMAGELENGTH, 3);
            tiff.SetField(TiffTag.SAMPLESPERPIXEL, 3);
            tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
            tiff.SetField(TiffTag.ROWSPERSTRIP, 1);
            tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
            tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tiff.SetField(TiffTag.COMPRESSION, compression);
            tiff.SetField(TiffTag.ORIENTATION, BitMiracle.LibTiff.Classic.Orientation.RIGHTTOP);
            for (int y = 0; y < 3; y++) Assert.That(tiff.WriteScanline(new byte[] { (byte)(y * 2 + 1), 0, 0, (byte)(y * 2 + 2), 0, 0 }, y), Is.True);
        }
        using var file = File.OpenRead(path);
        using var image = ImageHelpers.LoadBitmap(file, true)!;
        Assert.That(image.Width, Is.EqualTo(3));
        Assert.That(image.Height, Is.EqualTo(2));
        Assert.That(image.Pixels.Select(p => p.Red), Is.EqualTo(new byte[] { 5, 3, 1, 6, 4, 2 }));
        file.Position = 0;
        using var raw = ImageHelpers.LoadBitmap(file, false)!;
        Assert.That(raw.Pixels.Select(p => p.Red), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
    }

    [Test]
    public void Tiff_SavedTransparentImagesRoundTrip_AndInvalidFilesReturnNull()
    {
        using var source = new SKBitmap(3, 1);
        source.Pixels = [SKColors.Transparent, new SKColor(200, 100, 50, 128), SKColors.Blue];
        string path = Path.Combine(_directory, "saved.tif");
        ImageHelpers.SaveBitmap(source, path);
        using var image = ImageHelpers.LoadBitmap(path)!;
        Assert.That(image, Is.Not.Null);
        Assert.That(image.GetPixel(0, 0).Alpha, Is.Zero);
        Assert.That(image.GetPixel(1, 0).Alpha, Is.EqualTo(128));
        Assert.That(image.GetPixel(1, 0).Red, Is.EqualTo(source.GetPixel(1, 0).Red).Within(2));
        Assert.That(image.GetPixel(2, 0), Is.EqualTo(SKColors.Blue));
        using var invalid = new MemoryStream(new byte[] { 73, 73, 42, 0, 0 });
        Assert.That(ImageHelpers.LoadBitmap(invalid), Is.Null);
    }
}
