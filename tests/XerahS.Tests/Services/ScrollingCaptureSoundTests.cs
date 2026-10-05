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

using System.Drawing;
using System.Reflection;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Platform.Abstractions;
using XerahS.Core;
using XerahS.UI.ViewModels;
using XerahS.Tests.Tasks;

namespace XerahS.Tests.Services;

[TestFixture, NonParallelizable]
public class ScrollingCaptureSoundTests
{
    private static T Stub<T>() where T : class => DispatchProxy.Create<T, CaptureStageWindowTests.EmptyProxy>();

    [AvaloniaTest]
    public async Task CompletionSoundPrecedesUploadAndPlaybackFailureKeepsImage()
    {
        Initialize();
        var events = new List<string>();
        var options = new ScrollingCaptureOptions { StartDelay = 0, ScrollDelay = 0, AutoUpload = true, ShowRegion = false };
        var vm = new ScrollingCaptureViewModel(options, [ScrollMethod.MouseWheel])
        {
            SelectTargetRequested = _ => Task.FromResult<ScrollingCaptureTarget?>(new ScrollingCaptureTarget((nint)42, new Rectangle(0, 0, 8, 8))),
            PlayCompletionSound = () => { events.Add("sound"); throw new IOException("No audio device"); },
            UploadRequested = image => { events.Add("upload"); image.Dispose(); return Task.CompletedTask; }
        };
        vm.CaptureFinished += (_, _) => events.Add("finished");
        try
        {
            await vm.StartStopAsync();
            Assert.That(vm.HasResult, Is.True);
            Assert.That(events, Is.EqualTo(new[] { "sound", "upload", "finished" }));
        }
        finally { vm.Cleanup(); PlatformServices.Reset(); }
    }

    [AvaloniaTest]
    public async Task CancelledSelectionHasNoCompletionSound()
    {
        Initialize();
        int sounds = 0;
        var vm = new ScrollingCaptureViewModel { SelectTargetRequested = _ => Task.FromResult<ScrollingCaptureTarget?>(null), PlayCompletionSound = () => sounds++ };
        try { await vm.StartStopAsync(); Assert.That(sounds, Is.Zero); }
        finally { vm.Cleanup(); PlatformServices.Reset(); }
    }

    [AvaloniaTest]
    public async Task ThrownCaptureHasNoCompletionSound()
    {
        Initialize();
        ((CaptureStageWindowTests.CaptureProxy)PlatformServices.ScreenCapture).ThrowOnCapture = true;
        int sounds = 0;
        var vm = new ScrollingCaptureViewModel(new ScrollingCaptureOptions { StartDelay = 0, ScrollDelay = 0, ShowRegion = false }, [ScrollMethod.MouseWheel])
        {
            SelectTargetRequested = _ => Task.FromResult<ScrollingCaptureTarget?>(new ScrollingCaptureTarget((nint)42, new Rectangle(0, 0, 8, 8))),
            PlayCompletionSound = () => sounds++
        };
        try { await vm.StartStopAsync(); Assert.That(sounds, Is.Zero); Assert.That(vm.HasResult, Is.False); }
        finally { vm.Cleanup(); PlatformServices.Reset(); }
    }

    [AvaloniaTest]
    public async Task ReturnedFailureStillPlaysCompletionSound()
    {
        Initialize();
        ((Scrolling)PlatformServices.ScrollingCapture!).FailSecondBegin = true;
        int sounds = 0;
        var vm = new ScrollingCaptureViewModel(new ScrollingCaptureOptions { StartDelay = 0, ScrollDelay = 0, ShowRegion = false }, [ScrollMethod.MouseWheel])
        {
            SelectTargetRequested = _ => Task.FromResult<ScrollingCaptureTarget?>(new ScrollingCaptureTarget((nint)42, new Rectangle(0, 0, 8, 8))),
            PlayCompletionSound = () => sounds++
        };
        try { await vm.StartStopAsync(); Assert.That(sounds, Is.EqualTo(1)); Assert.That(vm.HasResult, Is.False); }
        finally { vm.Cleanup(); PlatformServices.Reset(); }
    }

    [AvaloniaTest]
    public async Task ErrorAfterAFrameKeepsAndUploadsItWithoutCompletionSound()
    {
        Initialize();
        ((Scrolling)PlatformServices.ScrollingCapture!).FailOnWait = 2;
        var events = new List<string>();
        var options = new ScrollingCaptureOptions { StartDelay = 0, ScrollDelay = 0, AutoUpload = true, ShowRegion = false };
        var vm = new ScrollingCaptureViewModel(options, [ScrollMethod.MouseWheel])
        {
            SelectTargetRequested = _ => Task.FromResult<ScrollingCaptureTarget?>(new ScrollingCaptureTarget((nint)42, new Rectangle(0, 0, 8, 8))),
            PlayCompletionSound = () => events.Add("sound"),
            UploadRequested = image => { events.Add("upload"); image.Dispose(); return Task.CompletedTask; }
        };
        try
        {
            await vm.StartStopAsync();
            Assert.Multiple(() =>
            {
                Assert.That(vm.Status, Is.EqualTo(ScrollingCaptureStatus.Failed));
                Assert.That(vm.StatusText, Is.EqualTo("Input lost"));
                Assert.That(vm.HasResult, Is.True, "As in ShareX, the frames captured before the error are kept.");
                Assert.That(events, Is.EqualTo(new[] { "upload" }));
            });
        }
        finally { vm.Cleanup(); PlatformServices.Reset(); }
    }

    private static void Initialize()
    {
        PlatformServices.Initialize(Stub<IPlatformInfo>(), Stub<IScreenService>(), Stub<IClipboardService>(),
            DispatchProxy.Create<IWindowService, CaptureStageWindowTests.WindowProxy>(),
            DispatchProxy.Create<IScreenCaptureService, CaptureStageWindowTests.CaptureProxy>(),
            Stub<IHotkeyService>(), Stub<IInputService>(), Stub<IFontService>(), Stub<IStartupService>(), Stub<ISystemService>(), Stub<IDiagnosticService>());
        PlatformServices.ScrollingCapture = new Scrolling();
    }

    [AvaloniaTest]
    public async Task ClosingDuringPermissionDisposesLateSessionWithoutSelectingOrRestoringWindow()
    {
        Initialize();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scroll = (Scrolling)PlatformServices.ScrollingCapture!;
        scroll.BeginOverride = _ => { entered.SetResult(); return permission.Task; };
        int selections = 0, sounds = 0;
        var minimized = new List<bool>();
        var vm = new ScrollingCaptureViewModel
        {
            SelectTargetRequested = _ => { selections++; return Task.FromResult<ScrollingCaptureTarget?>(null); },
            PlayCompletionSound = () => sounds++,
            SetMinimizedRequested = minimized.Add
        };
        try
        {
            Task operation = vm.StartStopAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.Cleanup();
            permission.SetResult(true); // Simulate a backend that finishes despite cancellation.
            await operation;
            await vm.StartStopAsync();
            Assert.Multiple(() =>
            {
                Assert.That(scroll.Ends, Is.EqualTo(1));
                Assert.That(selections, Is.Zero);
                Assert.That(sounds, Is.Zero);
                Assert.That(minimized, Is.EqualTo(new[] { true }));
                Assert.That(vm.HasResult, Is.False);
            });
        }
        finally { vm.Cleanup(); PlatformServices.Reset(); }
    }

    [AvaloniaTest]
    public async Task ClosingDuringSelectionCancelsOverlayAndEndsSession()
    {
        Initialize();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scroll = (Scrolling)PlatformServices.ScrollingCapture!;
        CancellationToken selectionToken = default;
        int sounds = 0;
        var vm = new ScrollingCaptureViewModel
        {
            SelectTargetRequested = async token =>
            {
                selectionToken = token;
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return null;
            },
            PlayCompletionSound = () => sounds++
        };
        try
        {
            Task operation = vm.StartStopAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.Cleanup();
            await operation;
            Assert.Multiple(() =>
            {
                Assert.That(selectionToken.IsCancellationRequested, Is.True);
                Assert.That(scroll.Ends, Is.EqualTo(1));
                Assert.That(sounds, Is.Zero);
                Assert.That(vm.IsCapturing, Is.False);
            });
        }
        finally { vm.Cleanup(); PlatformServices.Reset(); }
    }

    [AvaloniaTest]
    public async Task ThrownPermissionSetupRestoresWindowAndReportsFailure()
    {
        Initialize();
        var scroll = (Scrolling)PlatformServices.ScrollingCapture!;
        scroll.BeginOverride = _ => throw new IOException("Portal disconnected");
        var minimized = new List<bool>();
        var vm = new ScrollingCaptureViewModel { SetMinimizedRequested = minimized.Add };
        try
        {
            await vm.StartStopAsync();
            Assert.That(vm.Status, Is.EqualTo(ScrollingCaptureStatus.Failed));
            Assert.That(minimized, Is.EqualTo(new[] { true, false }));
        }
        finally { vm.Cleanup(); PlatformServices.Reset(); }
    }

    private sealed class Scrolling : IScrollingCaptureService
    {
        public bool IsSupported => true;
        public bool FailSecondBegin;
        public Func<CancellationToken, Task<bool>>? BeginOverride;
        public int Ends;
        public int FailOnWait;
        private int _begins;
        private int _waits;
        public Task WaitUntilAreaIsClearAsync(Rectangle area, CancellationToken cancellationToken = default) =>
            ++_waits == FailOnWait ? Task.FromException(new IOException("Input lost")) : Task.CompletedTask;
        public Task<bool> BeginAsync(CancellationToken token = default) => BeginOverride?.Invoke(token) ?? Task.FromResult(++_begins < 2 || !FailSecondBegin);
        public Task EndAsync() { Ends++; return Task.CompletedTask; }
        public Task ScrollWindowAsync(nint handle, ScrollMethod method, int amount, Point? targetPoint = null) => Task.CompletedTask;
        public Task ScrollToTopAsync(nint handle, Point? targetPoint = null) => Task.CompletedTask;
        public ScrollBarInfo? GetScrollBarInfo(nint handle) => null;
    }
}
