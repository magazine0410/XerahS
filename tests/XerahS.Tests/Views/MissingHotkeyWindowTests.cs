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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using NUnit.Framework;
using SkiaSharp;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;
using XerahS.UI.Views.Dialogs;

namespace XerahS.Tests.Views;

[TestFixture]
[NonParallelizable]
public class MissingHotkeyWindowTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => Directory.CreateDirectory(_directory = Path.Combine(Path.GetTempPath(), "xerahs-viewer-tests-" + Guid.NewGuid().ToString("N")));

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [AvaloniaTest]
    public void ImageViewer_NavigatesFolderWithoutWrapping_ClosesOnSpace()
    {
        string first = CreateImage("first.png", SKColors.Coral);
        string second = CreateImage("second.png", SKColors.CornflowerBlue);
        File.WriteAllText(Path.Combine(_directory, "ignored.txt"), "text");
        using var model = new ImageViewerViewModel();
        Assert.That(model.LoadFile(first), Is.True);
        Assert.That(model.CanNavigate, Is.True);
        var images = Directory.GetFiles(_directory).Where(XerahS.Common.FileHelpers.IsImageFile).ToArray();
        var window = new ImageViewerWindow(images, 0);
        var vm = (ImageViewerViewModel)window.DataContext!;
        window.Show();
        try
        {
            Assert.That(vm.StatusText, Does.Contain("1 / 2").And.Contain("320 × 180"));
            Assert.That(vm.CanNavigateLeft, Is.False);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.That(vm.CurrentImageFilePath, Is.EqualTo(images[1]));
            Assert.That(vm.CanNavigateRight, Is.False);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.That(vm.CurrentImageFilePath, Is.EqualTo(images[1]));
            SavePreview(window, "image-viewer");
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.That(window.IsVisible, Is.False);
            Assert.That(vm.CurrentImage, Is.Null, "Closing releases the bitmap.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void ImageViewer_CorruptImageDoesNotRelabelPreviousBitmap_AndCanContinue()
    {
        string first = CreateImage("first.png", SKColors.Coral);
        string broken = Path.Combine(_directory, "broken.png");
        File.WriteAllText(broken, "not an image");
        string last = CreateImage("last.png", SKColors.CornflowerBlue);
        using var vm = new ImageViewerViewModel();
        Assert.That(vm.LoadFiles([first, broken, last], 0), Is.True);
        var original = vm.CurrentImage;
        vm.Navigate(1);
        Assert.That(vm.CurrentImageFilePath, Is.EqualTo(first));
        Assert.That(vm.CurrentImage, Is.SameAs(original));
        vm.Navigate(1);
        Assert.That(vm.CurrentImageFilePath, Is.EqualTo(last));
        Assert.That(vm.StatusText, Does.Contain("3 / 3"));
    }

    [AvaloniaTest]
    public async Task DownloadPrompt_UsesUrlUploadWording_RejectsFtp_AndReturnsUrl()
    {
        var prompt = new UploadInputWindow(UploadInputKind.UploadUrl, "https://example.test/file.png");
        var result = prompt.ShowAsync(null);
        try
        {
            Assert.That(prompt.Title, Is.EqualTo("URL upload"));
            Assert.That(prompt.FindControl<TextBlock>("InputLabel")!.Text, Is.EqualTo("URL to download and upload"));
            var input = prompt.FindControl<TextBox>("Input")!;
            input.Text = "ftp://example.test/file.png";
            Click(prompt.FindControl<Button>("SubmitButton")!);
            Assert.That(result.IsCompleted, Is.False);
            input.Text = "  https://example.test/file.png  ";
            SavePreview(prompt, "upload-url");
            Click(prompt.FindControl<Button>("SubmitButton")!);
            Assert.That(await result, Is.EqualTo("https://example.test/file.png"));
        }
        finally { prompt.Close(); }
    }

    [AvaloniaTest]
    public async Task FileConflict_DoesNotAcceptExistingOrInvalidNewName_ThenReturnsNewName()
    {
        string existing = CreateImage("existing.png", SKColors.Coral);
        var window = new FileConflictWindow(existing);
        var result = window.ShowAsync(CancellationToken.None);
        try
        {
            var input = window.FindControl<TextBox>("NewName")!;
            var button = window.FindControl<Button>("NewNameButton")!;
            input.Text = "../escape";
            Click(button);
            Assert.That(result.IsCompleted, Is.False);
            input.Text = "existing";
            Click(button);
            Assert.That(result.IsCompleted, Is.False);
            input.Text = "renamed";
            SavePreview(window, "download-file-conflict");
            Click(button);
            Assert.That(await result, Is.EqualTo(new XerahS.Platform.Abstractions.FileConflictResolution(Path.Combine(_directory, "renamed.png"), false)));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileConflict_CancellationClosesThePrompt()
    {
        using var cts = new CancellationTokenSource();
        var window = new FileConflictWindow(Path.Combine(_directory, "existing.png"));
        var result = window.ShowAsync(cts.Token);
        cts.Cancel();
        Dispatcher.UIThread.RunJobs();
        Assert.That(await result, Is.Null);
        Assert.That(window.IsVisible, Is.False);
    }

    [AvaloniaTest]
    public void TrayPopup_PreservesCommandsAndState_AndTogglesClosed()
    {
        int calls = 0;
        var source = new NativeMenu();
        var nested = new NativeMenu();
        nested.Items.Add(new NativeMenuItem { Header = "Nested", Command = new RelayCommand(() => calls++) });
        source.Items.Add(new NativeMenuItem { Header = "Action", Command = new RelayCommand(() => calls++) });
        source.Items.Add(new NativeMenuItem { Header = "Unavailable", IsEnabled = false });
        source.Items.Add(new NativeMenuItemSeparator());
        source.Items.Add(new NativeMenuItem { Header = "Submenu", Menu = nested });
        var menu = TrayMenuToolService.CreateMenu(source);
        Assert.That(menu.Items, Has.Count.EqualTo(4));
        var action = (MenuItem)menu.Items[0]!;
        action.Command!.Execute(action.CommandParameter);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(((MenuItem)menu.Items[1]!).IsEnabled, Is.False);
        Assert.That(menu.Items[2], Is.TypeOf<Separator>());
        Assert.That(((MenuItem)menu.Items[3]!).Items, Has.Count.EqualTo(1));
        try
        {
            TrayMenuToolService.Open(source, new PixelPoint(50, 60));
            Dispatcher.UIThread.RunJobs();
            Assert.That(TrayMenuToolService.IsOpen, Is.True);
            TrayMenuToolService.Toggle();
            Assert.That(TrayMenuToolService.IsOpen, Is.False);
            var anchor = TrayMenuToolService.Open(source, new PixelPoint(50, 60));
            // Headless windows do not activate/deactivate each other; send the platform callback.
            var callback = typeof(global::Avalonia.Platform.IWindowBaseImpl).GetProperty("Deactivated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
            ((Action)callback.GetValue(anchor.PlatformImpl)!).Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.That(TrayMenuToolService.IsOpen, Is.False, "Deactivation dismisses the transient menu.");
        }
        finally { TrayMenuToolService.Close(); }
    }

    private string CreateImage(string name, SKColor color)
    {
        using var bitmap = new SKBitmap(320, 180);
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        string path = Path.Combine(_directory, name);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void SavePreview(Window window, string name)
    {
        window.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://XerahS.UI/"))
        {
            Source = new Uri("avares://XerahS.UI/Themes/ThemeResources.axaml")
        });
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        string directory = Path.Combine(Path.GetTempPath(), "xerahs-missing-hotkey-previews");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
