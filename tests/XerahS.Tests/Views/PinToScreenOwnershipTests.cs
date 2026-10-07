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

using System.Reflection;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Core;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.Views;

[TestFixture, NonParallelizable]
public class PinToScreenOwnershipTests
{
    [AvaloniaTest]
    public void CopyImage_WorksAfterTheCallerDisposesThePinnedBitmap()
    {
        var clipboardField = typeof(PlatformServices).GetField("_clipboardService", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousClipboard = clipboardField.GetValue(null);
        var clipboard = DispatchProxy.Create<IClipboardService, RecordingClipboard>();
        PlatformServices.Clipboard = clipboard;
        try
        {
            // Pin to Screen > From File disposes its bitmap as soon as PinImage returns.
            using (var bitmap = new SKBitmap(30, 20))
            {
                bitmap.Erase(SKColors.Orange);
                PinToScreenManager.PinImage(bitmap, null, new PinToScreenOptions());
            }
            Dispatcher.UIThread.RunJobs();

            var windows = (List<PinnedImageWindow>)typeof(PinToScreenManager)
                .GetField("_windows", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var viewModel = (PinnedImageViewModel)windows.Single().DataContext!;
            viewModel.CopyImageCommand.Execute(null);

            var recording = (RecordingClipboard)(object)clipboard;
            Assert.That(recording.CopiedPixel, Is.EqualTo(SKColors.Orange));
        }
        finally
        {
            PinToScreenManager.CloseAll();
            Dispatcher.UIThread.RunJobs();
            clipboardField.SetValue(null, previousClipboard);
        }
    }

    public class RecordingClipboard : DispatchProxy
    {
        public SKColor? CopiedPixel { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IClipboardService.SetImage) && args?[0] is SKBitmap image)
            {
                // A disposed bitmap has no native handle; reading its pixels would crash the process.
                Assert.That(image.Handle, Is.Not.EqualTo(IntPtr.Zero), "The pinned window copied a disposed bitmap.");
                CopiedPixel = image.GetPixel(0, 0);
            }

            var returnType = targetMethod?.ReturnType;
            if (returnType == null || returnType == typeof(void)) return null;
            if (returnType == typeof(Task)) return Task.CompletedTask;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
