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

using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using NUnit.Framework;
using SkiaSharp;
using System.Reflection;
using XerahS.Bootstrap;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Platform.Abstractions;
using XerahS.Tests.Tasks;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.UI;

[TestFixture, NonParallelizable]
public class AutoCaptureViewModelTests
{
    private static T Stub<T>() where T : class => DispatchProxy.Create<T, CaptureStageWindowTests.EmptyProxy>();

    private System.Drawing.Rectangle _savedRegion;

    [SetUp]
    public void SetUp()
    {
        PlatformServices.Reset();
        _savedRegion = SettingsManager.Settings.AutoCaptureRegion;
        SettingsManager.Settings.AutoCaptureRegion = System.Drawing.Rectangle.Empty;
    }

    [TearDown]
    public void TearDown()
    {
        SettingsManager.Settings.AutoCaptureRegion = _savedRegion;
        PlatformServices.Reset();
    }

    [AvaloniaTest]
    public void Start_RunsInFullScreenMode_WhenNoRegionIsSaved()
    {
        var viewModel = new AutoCaptureViewModel(DispatchProxy.Create<IDesktopTaskManager, PendingTaskManagerProxy>());

        viewModel.StartStopCommand.Execute(null);
        bool started = viewModel.IsRunning;
        viewModel.StopCapture();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsFullScreen, Is.True);
            Assert.That(started, Is.True);
        });
    }

    [AvaloniaTest]
    public void SavedRegion_OpensInCustomRegionMode()
    {
        SettingsManager.Settings.AutoCaptureRegion = new System.Drawing.Rectangle(1, 2, 30, 40);

        var viewModel = new AutoCaptureViewModel(DispatchProxy.Create<IDesktopTaskManager, PendingTaskManagerProxy>());

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsFullScreen, Is.False);
            Assert.That(viewModel.IsSelectRegionEnabled, Is.True);
            Assert.That(viewModel.RegionText, Does.Contain("Width: 30"));
        });
    }

    [AvaloniaTest]
    public void CaptureTimer_SkipsTicksWhileACaptureRuns_ButNotWhileItsTaskRuns()
    {
        var capture = new PendingCaptureService();
        PlatformServices.Initialize(Stub<IPlatformInfo>(), Stub<IScreenService>(), Stub<IClipboardService>(),
            Stub<IWindowService>(), capture, Stub<IHotkeyService>(), Stub<IInputService>(), Stub<IFontService>(),
            Stub<IStartupService>(), Stub<ISystemService>(), Stub<IDiagnosticService>());
        IDesktopTaskManager taskManager = DispatchProxy.Create<IDesktopTaskManager, PendingTaskManagerProxy>();
        var viewModel = new AutoCaptureViewModel(taskManager);
        viewModel.IsRunning = true;
        // Set by StartCapture, which would also start the real timers.
        typeof(AutoCaptureViewModel).GetField("_delayMs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, 60000);
        MethodInfo tick = typeof(AutoCaptureViewModel).GetMethod("OnCaptureTimerElapsed", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find OnCaptureTimerElapsed.");

        tick.Invoke(viewModel, null);
        tick.Invoke(viewModel, null);

        Assert.Multiple(() =>
        {
            Assert.That(capture.Pending, Has.Count.EqualTo(1));
            Assert.That(viewModel.CaptureCount, Is.EqualTo(1));
        });

        using var bitmap = new SKBitmap(1, 1);
        capture.Pending[0].SetResult(bitmap);
        Dispatcher.UIThread.RunJobs();

        // The first capture's task never finishes here, as with a long upload. WaitUploads decides whether to wait for it.
        tick.Invoke(viewModel, null);

        Assert.Multiple(() =>
        {
            Assert.That(capture.Pending, Has.Count.EqualTo(2));
            Assert.That(viewModel.CaptureCount, Is.EqualTo(2));
        });
    }

    private sealed class PendingCaptureService : IScreenCaptureService
    {
        public List<TaskCompletionSource<SKBitmap?>> Pending { get; } = new();

        public Task<SKBitmap?> CaptureFullScreenAsync(CaptureOptions? options = null)
        {
            var capture = new TaskCompletionSource<SKBitmap?>();
            Pending.Add(capture);
            return capture.Task;
        }

        public Task<SKRectI> SelectRegionAsync(CaptureOptions? options = null) => throw new NotSupportedException();

        public Task<SKBitmap?> CaptureRegionAsync(CaptureOptions? options = null) => throw new NotSupportedException();

        public Task<SKBitmap?> CaptureRectAsync(SKRect rect, CaptureOptions? options = null) => throw new NotSupportedException();

        public Task<SKBitmap?> CaptureActiveWindowAsync(IWindowService windowService, CaptureOptions? options = null) => throw new NotSupportedException();

        public Task<SKBitmap?> CaptureWindowAsync(IntPtr windowHandle, IWindowService windowService, CaptureOptions? options = null) => throw new NotSupportedException();

        public Task<CursorInfo?> CaptureCursorAsync() => throw new NotSupportedException();
    }

    public class PendingTaskManagerProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_Tasks" => Array.Empty<WorkerTask>(),
            "StartTask" => new TaskCompletionSource().Task,
            _ => null
        };
    }
}
