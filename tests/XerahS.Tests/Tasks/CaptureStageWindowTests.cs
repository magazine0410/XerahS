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
using NUnit.Framework;
using SkiaSharp;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Core.Tasks.Pipeline;
using XerahS.Platform.Abstractions;

namespace XerahS.Tests.Tasks;

[TestFixture, NonParallelizable]
public class CaptureStageWindowTests
{
    private IScreenCaptureService _capture = null!;
    private IWindowService _windows = null!;
    private CaptureProxy Capture => (CaptureProxy)_capture;
    private WindowProxy Windows => (WindowProxy)_windows;
    private Func<Task<WindowInfo?>>? _selector;

    [SetUp]
    public void SetUp()
    {
        PlatformServices.Reset();
        _capture = DispatchProxy.Create<IScreenCaptureService, CaptureProxy>();
        _windows = DispatchProxy.Create<IWindowService, WindowProxy>();
        PlatformServices.Initialize(Stub<IPlatformInfo>(), Stub<IScreenService>(), Stub<IClipboardService>(),
            _windows, _capture, Stub<IHotkeyService>(), Stub<IInputService>(), Stub<IFontService>(),
            Stub<IStartupService>(), Stub<ISystemService>(), Stub<IDiagnosticService>());
        _selector = WorkerTask.ShowWindowSelectorCallback;
    }

    [TearDown]
    public void TearDown()
    {
        WorkerTask.ShowWindowSelectorCallback = _selector;
        PlatformServices.Reset();
    }

    [TestCase(true, true), TestCase(false, true), TestCase(true, false), TestCase(false, false)]
    public async Task Workflow_ForwardsEffectiveTransparentAndClientSettings(bool transparent, bool inherit)
    {
        var defaults = new TaskSettings();
        defaults.CaptureSettings.CaptureTransparent = transparent;
        defaults.CaptureSettings.CaptureShadow = false;
        defaults.CaptureSettings.CaptureClientArea = true;
        var saved = new TaskSettings { Job = WorkflowType.ActiveWindow, UseDefaultCaptureSettings = inherit };
        saved.CaptureSettings.CaptureTransparent = inherit ? !transparent : transparent;
        saved.CaptureSettings.CaptureShadow = false;
        saved.CaptureSettings.CaptureClientArea = true;
        var settings = TaskSettings.GetSafeTaskSettings(saved, defaults);
        using var worker = WorkerTask.Create(settings);
        var result = await new CaptureStage(worker).ExecuteAsync(new PipelineContext { Info = worker.Info }, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PipelineStageResult.Continue));
            Assert.That(Capture.Options!.CaptureTransparent, Is.EqualTo(transparent));
            Assert.That(Capture.Options.CaptureShadow, Is.False);
            Assert.That(Capture.Options.CaptureClientArea, Is.True);
        });
    }

    [TestCase(false), TestCase(true)]
    public async Task CustomWindow_CapturesSelectedIdentityAfterRestoreAndActivation(bool named)
    {
        var settings = new TaskSettings { Job = WorkflowType.CustomWindow };
        if (named) settings.CaptureSettings.CaptureCustomWindow = "selected";
        Windows.Minimized = true;
        WorkerTask.ShowWindowSelectorCallback = () => Task.FromResult<WindowInfo?>(new WindowInfo { Handle = 42 });
        using var worker = WorkerTask.Create(settings);
        await new CaptureStage(worker).ExecuteAsync(new PipelineContext { Info = worker.Info }, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(Capture.Handle, Is.EqualTo((nint)42));
            Assert.That(Capture.Method, Is.EqualTo(nameof(IScreenCaptureService.CaptureWindowAsync)));
            Assert.That(Windows.Restored, Is.True);
        });
    }

    [TestCase(false, true), TestCase(true, false)]
    public async Task CustomWindow_RefusedOrUnconfirmedActivationStillCapturesSelectedWindow(bool accepted, bool focused)
    {
        // As in ShareX, which activates the window without checking the result.
        Windows.AcceptActivation = accepted;
        Windows.FocusOnActivation = focused;
        WorkerTask.ShowWindowSelectorCallback = () => Task.FromResult<WindowInfo?>(new WindowInfo { Handle = 42 });
        using var worker = WorkerTask.Create(new TaskSettings { Job = WorkflowType.CustomWindow });
        var result = await new CaptureStage(worker).ExecuteAsync(new PipelineContext { Info = worker.Info }, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PipelineStageResult.Continue));
            Assert.That(Capture.Method, Is.EqualTo(nameof(IScreenCaptureService.CaptureWindowAsync)));
            Assert.That(Capture.Handle, Is.EqualTo((nint)42));
        });
    }

    [TestCase(true, false), TestCase(false, true)]
    public async Task CustomWindowWithAlphaOrClientAreaBypassesEngineWithoutThoseOptions(bool transparent, bool client)
    {
        PlatformServices.HostedCaptureEngine = Stub<IHostedCaptureEngine>();
        var settings = new TaskSettings { Job = WorkflowType.CustomWindow };
        settings.CaptureSettings.CaptureTransparent = transparent;
        settings.CaptureSettings.CaptureClientArea = client;
        WorkerTask.ShowWindowSelectorCallback = () => Task.FromResult<WindowInfo?>(new WindowInfo { Handle = 42 });
        using var worker = WorkerTask.Create(settings);
        await new CaptureStage(worker).ExecuteAsync(new PipelineContext { Info = worker.Info }, CancellationToken.None);
        Assert.That(Capture.Method, Is.EqualTo(nameof(IScreenCaptureService.CaptureWindowAsync)));
    }

    [Test]
    public async Task CancelledPickerDoesNotCapture()
    {
        WorkerTask.ShowWindowSelectorCallback = () => Task.FromResult<WindowInfo?>(null);
        using var worker = WorkerTask.Create(new TaskSettings { Job = WorkflowType.CustomWindow });
        await new CaptureStage(worker).ExecuteAsync(new PipelineContext { Info = worker.Info }, CancellationToken.None);
        Assert.That(Capture.Method, Is.Null);
    }

    private static T Stub<T>() where T : class => DispatchProxy.Create<T, EmptyProxy>();
    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
    public class CaptureProxy : DispatchProxy
    {
        public bool ThrowOnCapture;
        public CaptureOptions? Options;
        public string? Method;
        public nint Handle;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Method = method!.Name;
            Options = args!.OfType<CaptureOptions>().Single();
            if (Method == nameof(IScreenCaptureService.CaptureWindowAsync)) Handle = (nint)args![0]!;
            return ThrowOnCapture ? Task.FromException<SKBitmap?>(new IOException("Capture failed")) : Task.FromResult<SKBitmap?>(new SKBitmap(8, 8));
        }
    }
    public class WindowProxy : DispatchProxy
    {
        public bool Minimized, Restored;
        public bool AcceptActivation = true, FocusOnActivation = true;
        private nint _foreground = 7;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(IWindowService.GetForegroundWindow): return _foreground;
                case nameof(IWindowService.IsWindowVisible): return true;
                case nameof(IWindowService.GetWindowProcessId): return 100u;
                case nameof(IWindowService.IsWindowMinimized): return Minimized;
                case nameof(IWindowService.ShowWindow): Restored = true; Minimized = false; return true;
                case nameof(IWindowService.ActivateWindow):
                    if (AcceptActivation && FocusOnActivation) _foreground = (nint)args![0]!;
                    return AcceptActivation;
                case nameof(IWindowService.GetWindowBounds): return new Rectangle(10, 10, 400, 300);
                case nameof(IWindowService.SearchWindow): return (nint)42;
                default: throw new AssertionException("Unexpected window operation: " + method.Name);
            }
        }
    }
}
