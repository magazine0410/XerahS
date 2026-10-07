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

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using ShareX.ImageEditor.Core.Editor;
using ShareX.ImageEditor.Core.ImageEffects.Adjustments;
using ShareX.ImageEditor.Core.ImageEffects;
using ShareX.ImageEditor.Hosting;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Tests.Xip0052;
using XerahS.UI.Controls;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.Views;

[TestFixture, NonParallelizable]
public class ImageProcessingViewsTests
{
    [AvaloniaTest]
    public void PresetControls_KeepSelectionAndEffectsWhileRenamingDuplicatingReorderingAndDeleting()
    {
        var settings = new TaskSettingsImage();
        settings.ImageEffectsPreset.Name = "Original";
        settings.ImageEffectsPreset.Effects.Add(new BrightnessImageEffect { Amount = 20 });
        var vm = new ImageEffectsViewModel(settings, new EditorCore(), new FakeViewDialogService());
        var window = new ImageEffectsToolWindow(vm, null, null, null);
        try
        {
            window.Show();
            window.UpdateLayout();
            vm.DuplicatePresetCommand.Execute(null);
            vm.Name = "Copy";
            ((BrightnessImageEffect)vm.Effects[0]).Amount = 40;
            vm.NewPresetCommand.Execute(null);
            vm.Name = "Third";
            vm.MovePresetUpCommand.Execute(null);
            window.UpdateLayout();
            Assert.That(settings.ImageEffectPresets.Select(p => p.Name), Is.EqualTo(new[] { "Original", "Third", "Copy" }));
            Assert.That(vm.SelectedPresetIndex, Is.EqualTo(1));
            vm.RemovePresetCommand.Execute(null);
            Assert.That(vm.Name, Is.EqualTo("Copy"));
            vm.SelectedPresetIndex = 0;
            Assert.That(((BrightnessImageEffect)vm.Effects[0]).Amount, Is.EqualTo(20));
            window.UpdateLayout();
            var enabled = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, vm.Effects[0].Name));
            enabled.IsChecked = false;
            vm.ToggleEffectCommand.Execute(vm.Effects[0]);
            Assert.That(settings.ImageEffectsPreset.Effects[0].Enabled, Is.False);
            using var image = new SKBitmap(1, 1);
            image.Erase(new SKColor(50, 50, 50));
            using var result = vm.ApplyEffects(image);
            Assert.That(result.GetPixel(0, 0).Red, Is.EqualTo(50));
            vm.RemovePresetCommand.Execute(null);
            vm.RemovePresetCommand.Execute(null);
            Assert.That(settings.ImageEffectPresets, Has.Count.EqualTo(1));
            Assert.That(settings.ImageEffectsPreset.Effects, Is.Empty);
            SavePreview(window);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task Tiff_LoadsInViewerHistoryThumbnailEffectsPinAndHostedEditor()
    {
        string path = Path.Combine(Path.GetTempPath(), "xerahs-ui-tiff-" + Guid.NewGuid().ToString("N") + ".tif");
        using var image = new SKBitmap(40, 30);
        image.Erase(SKColors.Blue);
        ImageHelpers.SaveBitmap(image, path);
        var decoder = EditorServices.ImageDecoder;
        var thumbnail = new HistoryThumbnail { FilePath = path, DecodeWidth = 32 };
        var host = new Window { Content = thumbnail, Width = 160, Height = 120 };
        var vm = new ImageEffectsViewModel(new TaskSettingsImage(), new EditorCore(), new FakeViewDialogService());
        var effects = new ImageEffectsToolWindow(vm, null, null, null);
        try
        {
            using var viewer = new ImageViewerViewModel();
            Assert.That(viewer.LoadFile(path), Is.True);
            Assert.That(viewer.CurrentImage!.PixelSize.Width, Is.EqualTo(40));
            Assert.That(effects.LoadImageFile(path), Is.True);
            using var processed = effects.CreateResult();
            Assert.That(processed!.GetPixel(0, 0), Is.EqualTo(SKColors.Blue));
            EditorServices.ImageDecoder = stream => ImageHelpers.LoadBitmap(stream);
            using var core = new EditorCore();
            core.LoadImage(path);
            Assert.That(core.SourceImage!.Width, Is.EqualTo(40));
            host.Show();
            for (int i = 0; i < 500 && thumbnail.Source == null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Assert.That(thumbnail.Source, Is.Not.Null);
            var pinned = await PinToScreenToolService.PinFilesAsync([path], showToast: false);
            Assert.That(pinned.PinnedCount, Is.EqualTo(1));
            Assert.That(PinToScreenManager.Count, Is.EqualTo(1));
        }
        finally
        {
            PinToScreenManager.CloseAll();
            host.Close();
            effects.Close();
            vm.ReleasePreview();
            EditorServices.ImageDecoder = decoder;
            File.Delete(path);
        }
    }

    [Test]
    public void ImagePickers_ListTiffFiles()
    {
        // The Linux portal receives only the patterns, so TIFF must be listed there.
        foreach (var type in new[] { XerahS.UI.Helpers.ImageFilePickerTypes.Images,
            ShareX.ImageEditor.Presentation.Helpers.ImageFilePickerTypes.Images })
        {
            Assert.That(type.Patterns, Does.Contain("*.tif").And.Contain("*.tiff").And.Contain("*.jpg").And.Contain("*.webp"));
            Assert.That(type.MimeTypes, Does.Contain("image/tiff"));
        }
    }

    [AvaloniaTest]
    public void ImageFileLoader_CopiesDecodedPixelsForEachColorType()
    {
        var color = new SKColor(200, 100, 50, 128);
        foreach (var info in new[]
        {
            new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Premul),
            new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKImageInfo(3, 2, SKColorType.RgbaF16, SKAlphaType.Premul)
        })
        {
            Bitmap result;
            using (var source = new SKBitmap(info))
            {
                source.Erase(color);
                result = ImageFileLoader.ToAvaloniaBitmap(source);
            }

            using (result)
            {
                // The Avalonia bitmap owns a copy, so it stays valid after the source is disposed.
                using var pixels = ReadPixels(result);
                Assert.That(result.PixelSize, Is.EqualTo(new global::Avalonia.PixelSize(3, 2)), info.ColorType.ToString());
                Assert.That(pixels.GetPixel(2, 1).Red, Is.EqualTo(200).Within(2), info.ColorType.ToString());
                Assert.That(pixels.GetPixel(2, 1).Green, Is.EqualTo(100).Within(2), info.ColorType.ToString());
                Assert.That(pixels.GetPixel(2, 1).Alpha, Is.EqualTo(128).Within(1), info.ColorType.ToString());
            }
        }

        using var gray = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Gray8, SKAlphaType.Opaque));
        gray.Erase(new SKColor(90, 90, 90));
        using var grayResult = ImageFileLoader.ToAvaloniaBitmap(gray);
        using var grayPixels = ReadPixels(grayResult);
        Assert.That(grayPixels.GetPixel(1, 1), Is.EqualTo(new SKColor(90, 90, 90)));
    }

    private static SKBitmap ReadPixels(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    [AvaloniaTest]
    public void AfterCaptureMode_OnlyOffersContinueAndCannotReplaceCapturedImage()
    {
        using var source = new SKBitmap(12, 8);
        var vm = new ImageEffectsViewModel(new TaskSettingsImage(), new EditorCore(), new FakeViewDialogService());
        var window = new ImageEffectsToolWindow(vm, source, null, null);
        try
        {
            window.UseAfterCaptureMode();
            window.Show();
            window.UpdateLayout();
            foreach (string name in new[] { "OpenButton", "ClipboardButton", "SaveButton", "UploadButton" })
                Assert.That(window.FindControl<Button>(name)!.IsVisible, Is.False);
            Assert.That(window.FindControl<Button>("CloseButton")!.Content, Is.EqualTo("Continue"));
            Assert.That(vm.PreviewBitmap!.PixelSize.Width, Is.EqualTo(12));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void FailedEffectPreview_ClearsStalePixels_AndRecoversWhenTheEffectIsRemoved()
    {
        var settings = new TaskSettingsImage();
        var vm = new ImageEffectsViewModel(settings, new EditorCore(), new FakeViewDialogService());
        try
        {
            Assert.That(vm.PreviewBitmap, Is.Not.Null);
            vm.Effects.Add(new FailingEffect());
            vm.UpdatePreview();
            Assert.That(vm.PreviewBitmap, Is.Null);
            Assert.That(vm.PreviewError, Does.Contain("test failure"));
            vm.Effects.Clear();
            vm.UpdatePreview();
            Assert.That(vm.PreviewBitmap, Is.Not.Null);
            Assert.That(vm.HasPreviewError, Is.False);
        }
        finally { vm.ReleasePreview(); }
    }

    private sealed class FailingEffect : ImageEffect
    {
        public override string Name => "Failure";
        public override string IconKey => "";
        public override ImageEffectCategory Category => ImageEffectCategory.Adjustments;
        public override SKBitmap Apply(SKBitmap source) => throw new InvalidOperationException("test failure");
    }

    private static void SavePreview(Window window)
    {
        string? directory = Environment.GetEnvironmentVariable("XERAHS_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        using var stream = File.Create(Path.Combine(directory, "image-processing-presets.png"));
        frame!.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
