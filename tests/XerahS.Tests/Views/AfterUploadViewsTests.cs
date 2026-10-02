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
using NUnit.Framework;
using SkiaSharp;
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.Platform.Abstractions;
using XerahS.Tests.Xip0052;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.Views;

[TestFixture, NonParallelizable]
public class AfterUploadViewsTests
{
    [AvaloniaTest]
    public void QrWindow_EncodesProvidedUrlWithoutInput_AndReleasesImageOnClose()
    {
        const string url = "https://short.test/upload";
        using var vm = new QrCodeGeneratorViewModel(new FakeViewDialogService());
        var window = new QrCodeGeneratorWindow(vm, url);
        try
        {
            window.Show();
            Assert.That(vm.InputText, Is.EqualTo(url));
            Assert.That(vm.HasPreviewImage, Is.True);
            using var encoded = new MemoryStream();
            vm.PreviewImage!.Save(encoded, PngBitmapEncoderOptions.Default);
            encoded.Position = 0;
            using var bitmap = SKBitmap.Decode(encoded);
            Assert.That(QrCodeService.Decode(bitmap, out var error), Is.EqualTo(new[] { url }));
            Assert.That(error, Is.Null.Or.Empty);
            SavePreview(window, "after-upload-qr");
        }
        finally { window.Close(); }
        Assert.That(vm.HasPreviewImage, Is.False);
    }

    [AvaloniaTest]
    public void AfterUploadWindow_FormatsMatchAutomaticTasks_AndEmptyShortUrlUsesOriginal()
    {
        var info = new AfterUploadWindowInfo
        {
            Url = "https://files.test/image.png", ShortenedUrl = string.Empty,
            ThumbnailUrl = "https://files.test/thumb.png", DeletionUrl = "https://files.test/delete",
            FileName = "image.png", FilePath = "/captures/image.png",
            ThumbnailFilePath = "/captures/image-thumb.png", UploadTime = 42,
            ClipboardContentFormat = "$filenamenoext $result $thumbnailurl $deletionurl",
            OpenUrlFormat = "https://viewer.test/?image=$url"
        };
        using var vm = new AfterUploadViewModel(info);
        Assert.That(vm.PrimaryUrl, Is.EqualTo(info.Url));
        Assert.That(vm.Formats.Single(item => item.Label == "Clipboard format").Value,
            Is.EqualTo("image https://files.test/image.png https://files.test/thumb.png https://files.test/delete"));
        Assert.That(vm.Formats.Single(item => item.Label == "Open URL format").Value,
            Is.EqualTo("https://viewer.test/?image=" + info.Url));
    }

    [AvaloniaTest]
    public void WorkflowOverrides_KeepAfterUploadFlagsAndShortenerSeparateFromDefaultAndUploadDestination()
    {
        var defaults = SettingsManager.DefaultTaskSettings;
        var previousTasks = defaults.AfterUploadJob;
        var previousDestination = defaults.UrlShortenerDestinationInstanceId;
        try
        {
            defaults.AfterUploadJob = AfterUploadTasks.OpenURL | AfterUploadTasks.ShowQRCode;
            defaults.UrlShortenerDestinationInstanceId = "default-shortener";
            var settings = new TaskSettings
            {
                AfterUploadJob = AfterUploadTasks.None,
                DestinationInstanceId = "image-uploader", UrlShortenerDestinationInstanceId = "workflow-shortener"
            };
            var vm = new TaskSettingsViewModel(settings, new FakeViewDialogService());
            Assert.That(vm.OpenURL && vm.ShowQRCode, Is.True);
            Assert.That(vm.SelectedUrlShortenerDestination!.InstanceId, Is.EqualTo("default-shortener"));
            vm.OverrideAfterUploadTasks = true;
            vm.OverrideDestinations = true;
            Assert.That(vm.OpenURL || vm.ShowQRCode, Is.False);
            Assert.That(vm.SelectedUrlShortenerDestination!.InstanceId, Is.EqualTo("workflow-shortener"));
            vm.OpenURL = true;
            vm.ShowQRCode = true;
            vm.SelectedUrlShortenerDestination = vm.UrlShortenerDestinations[0];
            Assert.Multiple(() =>
            {
                Assert.That(settings.AfterUploadJob, Is.EqualTo(AfterUploadTasks.OpenURL | AfterUploadTasks.ShowQRCode));
                Assert.That(settings.UrlShortenerDestinationInstanceId, Is.Empty);
                Assert.That(settings.DestinationInstanceId, Is.EqualTo("image-uploader"));
                Assert.That(defaults.UrlShortenerDestinationInstanceId, Is.EqualTo("default-shortener"));
                Assert.That(defaults.AfterUploadJob, Is.EqualTo(AfterUploadTasks.OpenURL | AfterUploadTasks.ShowQRCode));
            });
        }
        finally
        {
            defaults.AfterUploadJob = previousTasks;
            defaults.UrlShortenerDestinationInstanceId = previousDestination;
        }
    }

    [AvaloniaTest]
    public void AfterCaptureDialog_PreservesAndUpdatesOpenAndQrTasks()
    {
        using var bitmap = new SKBitmap(12, 8);
        var vm = new AfterCaptureViewModel(bitmap, AfterCaptureTasks.UploadImageToHost,
            AfterUploadTasks.OpenURL | AfterUploadTasks.ShowQRCode);
        Assert.That(vm.OpenURL && vm.ShowQRCode, Is.True);
        vm.OpenURL = false;
        Assert.That(vm.AfterUploadTasks, Is.EqualTo(AfterUploadTasks.CopyURLToClipboard | AfterUploadTasks.ShowQRCode));
        vm.ShowQRCode = false;
        Assert.That(vm.AfterUploadTasks, Is.EqualTo(AfterUploadTasks.CopyURLToClipboard));
        vm.PreviewImage.Dispose();
    }

    private static void SavePreview(Window window, string name)
    {
        window.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://XerahS.UI/"))
        {
            Source = new Uri("avares://XerahS.UI/Themes/ThemeResources.axaml")
        });
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        string? directory = Environment.GetEnvironmentVariable("XERAHS_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
