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
using NUnit.Framework;
using XerahS.Core;
using XerahS.Core.Tasks;
using XerahS.Platform.Abstractions;

namespace XerahS.Tests.Tasks;

[TestFixture]
[NonParallelizable]
public class TopmostJobTests
{
    [TestCase(WorkflowType.ActiveWindowTopMost, true, true)]
    [TestCase(WorkflowType.ActiveWindowBorderless, true, true)]
    [TestCase(WorkflowType.ActiveWindowBorderless, true, false)]
    [TestCase(WorkflowType.ActiveWindowBorderless, false, false)]
    [TestCase(WorkflowType.ActiveWindowTopMost, true, false)]
    [TestCase(WorkflowType.ActiveWindowTopMost, false, false)]
    public async Task ActiveWindowJob_UsesPlatformCapabilityWithoutOpeningCapture(WorkflowType job, bool supported, bool succeeds)
    {
        var service = DispatchProxy.Create<IWindowService, WindowProxy>();
        var proxy = (WindowProxy)service;
        proxy.Supported = supported;
        proxy.Succeeds = succeeds;
        PlatformServices.Window = service;
        try
        {
            // "Keep taskbar visible" from the Borderless window settings reaches the direct job, as in ShareX.
            var settings = new TaskSettings { Job = job, AfterCaptureJob = AfterCaptureTasks.None, UseDefaultToolsSettings = false };
            settings.ToolsSettings.BorderlessWindowSettings.ExcludeTaskbarArea = true;
            using var worker = WorkerTask.Create(settings);
            await worker.StartAsync();
            Assert.That(proxy.ToggleCalls, Is.EqualTo(supported ? 1 : 0));
            Assert.That(worker.Status, Is.EqualTo(supported && succeeds ? XerahS.Core.TaskStatus.Completed : XerahS.Core.TaskStatus.Failed));
            Assert.That(worker.Info.FilePath, Is.Empty);
            if (supported && succeeds) Assert.That(worker.Info.SuppressCompletionNotification, Is.True);
            else Assert.That(worker.Error, Is.InstanceOf(supported ? typeof(InvalidOperationException) : typeof(PlatformNotSupportedException)));
        }
        finally { PlatformServices.Reset(); }
    }

    public class WindowProxy : DispatchProxy
    {
        public bool Supported { get; set; }
        public bool Succeeds { get; set; }
        public int ToggleCalls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_SupportsTopmost":
                case "get_SupportsBorderless": return Supported;
                case nameof(IWindowService.GetForegroundWindow): return new IntPtr(42);
                case nameof(IWindowService.ToggleBorderlessWindow):
                    Assert.That(args![0], Is.EqualTo(new IntPtr(42)));
                    Assert.That(args[1], Is.EqualTo(true));
                    ToggleCalls++; return Succeeds;
                case nameof(IWindowService.ToggleActiveWindowTopmost): ToggleCalls++; return Succeeds;
                default: throw new AssertionException("Unexpected window operation: " + targetMethod.Name);
            }
        }
    }
}
