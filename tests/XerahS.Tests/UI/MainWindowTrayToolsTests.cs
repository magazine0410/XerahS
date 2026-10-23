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
using XerahS.Core;
using XerahS.Media;
using XerahS.Platform.Abstractions;
using XerahS.UI.Helpers;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.UI;

/// <summary>ShareX's main window, tray, and tools features that XerahS added: the parts that need no desktop.</summary>
[TestFixture]
[NonParallelizable]
public sealed class MainWindowTrayToolsTests
{
    [Test]
    public void ImageCombiner_WrapsHorizontalRows_AsShareX()
    {
        using var first = Solid(10, 10, SKColors.Red);
        using var second = Solid(20, 5, SKColors.Lime);
        using var third = Solid(5, 15, SKColors.Blue);

        using SKBitmap result = new ImageCombiner().Combine([first, second, third], ImageCombinerOrientation.Horizontal, spacing: 1,
            alignment: XerahS.Media.ImageCombinerAlignment.Near, wrapAfter: 2)!;

        Assert.Multiple(() =>
        {
            // Row 1: 10 + 1 + 20 wide, as high as its highest image (10); row 2 starts 10 + 1 below.
            Assert.That(result.Width, Is.EqualTo(31));
            Assert.That(result.Height, Is.EqualTo(26));
            Assert.That(result.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
            Assert.That(result.GetPixel(11, 0), Is.EqualTo(SKColors.Lime));
            Assert.That(result.GetPixel(0, 11), Is.EqualTo(SKColors.Blue));
            Assert.That(result.GetPixel(20, 20).Alpha, Is.EqualTo(0));
        });
    }

    [Test]
    public void ImageCombiner_WrapsVerticalColumns_AndCentersInEachColumn()
    {
        using var first = Solid(10, 10, SKColors.Red);
        using var second = Solid(4, 4, SKColors.Lime);
        using var third = Solid(6, 6, SKColors.Blue);

        using SKBitmap result = new ImageCombiner().Combine([first, second, third], ImageCombinerOrientation.Vertical,
            alignment: XerahS.Media.ImageCombinerAlignment.Center, wrapAfter: 2)!;

        Assert.Multiple(() =>
        {
            // Column 1 is 10 wide (the second image centered at x 3); column 2 starts at x 10.
            Assert.That(result.Width, Is.EqualTo(16));
            Assert.That(result.Height, Is.EqualTo(14));
            Assert.That(result.GetPixel(3, 10), Is.EqualTo(SKColors.Lime));
            Assert.That(result.GetPixel(2, 10).Alpha, Is.EqualTo(0));
            Assert.That(result.GetPixel(10, 0), Is.EqualTo(SKColors.Blue));
        });
    }

    [Test]
    public void ImageCombiner_AutoFillsWithTheFirstImagesBottomRightPixel()
    {
        using var first = Solid(4, 4, SKColors.Red);
        first.SetPixel(3, 3, SKColors.Yellow);
        using var second = Solid(2, 2, SKColors.Blue);

        using SKBitmap result = new ImageCombiner().Combine([first, second], ImageCombinerOrientation.Horizontal,
            alignment: XerahS.Media.ImageCombinerAlignment.Near, autoFillBackground: true)!;

        Assert.That(result.GetPixel(5, 3), Is.EqualTo(SKColors.Yellow));
    }

    [Test]
    public void ImageCombiner_WithoutWrapping_KeepsOneRow()
    {
        using var first = Solid(3, 3, SKColors.Red);
        using var second = Solid(3, 3, SKColors.Blue);
        using var third = Solid(3, 3, SKColors.Lime);

        using SKBitmap result = new ImageCombiner().Combine([first, second, third], ImageCombinerOrientation.Horizontal, spacing: 2)!;

        Assert.That((result.Width, result.Height), Is.EqualTo((13, 3)));
    }

    [Test]
    public void OcrServiceLink_EncodesTheTextIntoThePlaceholder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OcrViewModel.GenerateServiceLink("https://www.google.com/search?q={0}", "a b\nc&d"),
                Is.EqualTo("https://www.google.com/search?q=a%20b%0Ac%26d"));
            Assert.That(OcrViewModel.GenerateServiceLink("https://www.google.com/search?q={0}", "  "), Is.Null);
            Assert.That(OcrViewModel.GenerateServiceLink(" ", "text"), Is.Null);
        });
    }

    [Test]
    public void RecentTask_TrayMenuText_ShowsTheTimeAndTheEndOfTheResult()
    {
        var task = new RecentTask
        {
            FilePath = "/home/user/Pictures/a.png",
            URL = "https://example.com/" + new string('x', 60) + "/image.png",
            Time = new DateTime(2026, 10, 23, 9, 5, 7)
        };

        Assert.Multiple(() =>
        {
            Assert.That(task.ToString(), Is.EqualTo(task.URL));
            Assert.That(task.TrayMenuText, Does.StartWith("[09:05:07] ..."));
            Assert.That(task.TrayMenuText, Does.EndWith("/image.png"));
            Assert.That(task.TrayMenuText.Length, Is.EqualTo("[09:05:07] ".Length + 50));
        });

        task.ShortenedURL = "https://s.example/1";
        Assert.That(task.TrayMenuText, Is.EqualTo("[09:05:07] https://s.example/1"));
    }

    [Test]
    public void TaskSettings_IsUsingDefaultSettings_FollowsTheOverrides()
    {
        var settings = new TaskSettings();
        Assert.That(settings.IsUsingDefaultSettings, Is.True);

        settings.UseDefaultCaptureSettings = false;
        Assert.That(settings.IsUsingDefaultSettings, Is.False);
    }

    [Test]
    public void GetSafeTaskSettings_KeepsTheWindowToCapture()
    {
        var settings = new TaskSettings { Job = WorkflowType.CustomWindow, CaptureWindowHandle = new IntPtr(0x1234) };

        TaskSettings safe = TaskSettings.GetSafeTaskSettings(settings);

        Assert.That(TaskSettings.GetSafeTaskSettings(safe).CaptureWindowHandle, Is.EqualTo(new IntPtr(0x1234)));
    }

    [Test]
    public void ToolMenuCatalog_HidesEveryEntryOfAHiddenTool()
    {
        List<string> saved = SettingsManager.Settings.HiddenTools;
        try
        {
            SettingsManager.Settings.HiddenTools = ["Tools_QRCode"];

            Assert.Multiple(() =>
            {
                Assert.That(ToolMenuCatalog.IsTagHidden("Tools_QrGenerator"), Is.True);
                Assert.That(ToolMenuCatalog.IsTagHidden("Tools_QrScanRegion"), Is.True);
                Assert.That(ToolMenuCatalog.IsTagHidden("Tools_OCR"), Is.False);
                Assert.That(ToolMenuCatalog.IsTagHidden("Tools_VideoEditor"), Is.False);
                Assert.That(ToolMenuCatalog.GetVisibleCategories().SelectMany(c => c.Tools).Any(t => t.Id == "Tools_QRCode"), Is.False);
                Assert.That(ToolMenuCatalog.GetVisibleCategories(includeHidden: true).SelectMany(c => c.Tools).Any(t => t.Id == "Tools_QRCode"), Is.True);
            });
        }
        finally
        {
            SettingsManager.Settings.HiddenTools = saved;
        }
    }

    [Test]
    public void ToolMenuCatalog_HasShareXsCategoriesInAlphabeticalOrder()
    {
        Assert.That(ToolMenuCatalog.Categories.Select(c => c.Header),
            Is.EqualTo(new[] { "Screen & color", "Images", "Video", "Data & text", "System" }));
        foreach (ToolMenuCategory category in ToolMenuCatalog.Categories)
        {
            Assert.That(category.Tools.Select(t => t.Header), Is.Ordered.Using((IComparer<string>)StringComparer.OrdinalIgnoreCase), category.Header);
        }
    }

    [Test]
    public void ThumbnailClickAction_UsesShareXsNames()
    {
        Assert.That(Enum.GetValues<ThumbnailViewClickAction>().Select(action => XerahS.Common.EnumExtensions.GetDescription(action)),
            Is.EqualTo(new[] { "Default", "Select", "Open image viewer", "Open file", "Open folder", "Open URL", "Edit image" }));
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return bitmap;
    }
}
