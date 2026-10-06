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
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using NUnit.Framework;
using XerahS.Bootstrap;
using XerahS.RegionCapture.ScreenRecording;
using XerahS.UI.Views;

namespace XerahS.Tests.Avalonia;

// The recording controls follow ShareX's ScreenRecordWindow toolbar.
[TestFixture]
public sealed class RecordingControlWindowTests
{
    [AvaloniaTest]
    public void Abort_AsksInTheToolbar_WhenConfirmationIsOn()
    {
        var coordinator = new Coordinator();
        var window = Show(coordinator, new RecordingOptions { AskConfirmationOnAbort = true });
        window.SetStatus(new RecordingStatusEventArgs(RecordingStatus.Recording, TimeSpan.Zero));

        Click(window, "✕  Abort");
        Assert.That(window.IsAbortConfirmationVisible, Is.True);
        Assert.That(coordinator.Aborts, Is.Zero);
        // A timer tick keeps the question open; only a status change closes it.
        window.SetStatus(new RecordingStatusEventArgs(RecordingStatus.Recording, TimeSpan.FromSeconds(1)));
        Assert.That(window.IsAbortConfirmationVisible, Is.True);

        Click(window, "Cancel");
        Assert.That(window.IsAbortConfirmationVisible, Is.False);
        Assert.That(coordinator.Aborts, Is.Zero);

        window.RequestAbort();
        Click(window, "Abort");
        Assert.That(coordinator.Aborts, Is.EqualTo(1));
        window.Finish();
    }

    [AvaloniaTest]
    public void Abort_DoesNotAsk_WhenConfirmationIsOff()
    {
        var coordinator = new Coordinator();
        var window = Show(coordinator, new RecordingOptions());
        window.SetStatus(new RecordingStatusEventArgs(RecordingStatus.Recording, TimeSpan.Zero));
        Assert.That(RecordingControlWindow.Current, Is.SameAs(window));
        RecordingControlWindow.RequestAbortAsync(coordinator);
        Assert.That(coordinator.Aborts, Is.EqualTo(1));
        Assert.That(window.IsAbortConfirmationVisible, Is.False);
        window.Finish();
        Assert.That(RecordingControlWindow.Current, Is.Null);
    }

    [AvaloniaTest]
    public void Buttons_FollowShareXStates_AndTheToolbarHidesWhileEncoding()
    {
        var coordinator = new Coordinator();
        var window = Show(coordinator, new RecordingOptions { AutoStart = false });
        Assert.That(window.StartStopLabel, Is.EqualTo("Start"));
        Assert.That(window.PauseLabel, Is.EqualTo("Resume"), "ShareX's Resume also starts a waiting recording");
        Click(window, "▶  Resume");
        Assert.That(coordinator.Starts, Is.EqualTo(1));

        window.SetStatus(new RecordingStatusEventArgs(RecordingStatus.Initializing, TimeSpan.Zero));
        Click(window, "■  Stop");
        Assert.That(coordinator.Aborts, Is.EqualTo(1), "stopping before the recording started aborts it, as in ShareX");

        window.SetStatus(new RecordingStatusEventArgs(RecordingStatus.Recording, TimeSpan.Zero));
        Assert.That(window.PauseLabel, Is.EqualTo("Pause"));
        Click(window, "■  Stop");
        Assert.That(coordinator.Stops, Is.EqualTo(1));

        window.SetStatus(new RecordingStatusEventArgs(RecordingStatus.Finalizing, TimeSpan.Zero) { EncodingProgress = 10 });
        Assert.That(window.IsVisible, Is.False);
        window.SetStatus(new RecordingStatusEventArgs(RecordingStatus.Idle, TimeSpan.Zero));
        Assert.That(RecordingControlWindow.Current, Is.Null);
    }

    [AvaloniaTest]
    public void ButtonLabelsAndTimer_FollowTheWorkflowOptions()
    {
        var window = Show(new Coordinator(), new RecordingOptions { ShowButtonLabels = false, ShowTimer = false });
        Assert.That(window.GetLogicalDescendants().OfType<Button>().Select(b => b.Content as string), Does.Contain("✕").And.Not.Contain("✕  Abort"));
        Assert.That(window.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Text == "00:00:00").All(t => !t.IsEffectivelyVisible), Is.True);
        window.Finish();
    }

    [Test]
    public void Timer_CountsDownTheStartDelay_ThenUpOrDownForFixedDuration()
    {
        var free = new RecordingOptions();
        var fixedDuration = new RecordingOptions { Duration = 10 };
        Assert.Multiple(() =>
        {
            Assert.That(RecordingControlWindow.GetTimerValue(RecordingStatus.Waiting, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), free), Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(RecordingControlWindow.GetTimerValue(RecordingStatus.Waiting, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), free), Is.EqualTo(TimeSpan.Zero));
            Assert.That(RecordingControlWindow.GetTimerValue(RecordingStatus.Recording, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1), free), Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(RecordingControlWindow.GetTimerValue(RecordingStatus.Recording, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1), fixedDuration), Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(RecordingControlWindow.GetTimerValue(RecordingStatus.Paused, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(9), fixedDuration), Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(RecordingControlWindow.GetTimerValue(RecordingStatus.Waiting, TimeSpan.Zero, TimeSpan.FromSeconds(2), fixedDuration), Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Toolbar_IsPlacedBelowTheRecording_OrAboveIt_OrInside()
    {
        var screens = new[] { new PixelRect(0, 0, 1920, 1040), new PixelRect(1920, 0, 1920, 1040) };
        var toolbar = new PixelSize(400, 40);
        Assert.Multiple(() =>
        {
            Assert.That(RecordingControlWindow.PlaceToolbar(new PixelRect(100, 100, 800, 600), toolbar, screens), Is.EqualTo(new PixelPoint(300, 704)));
            Assert.That(RecordingControlWindow.PlaceToolbar(new PixelRect(100, 500, 800, 520), toolbar, screens), Is.EqualTo(new PixelPoint(300, 456)));
            Assert.That(RecordingControlWindow.PlaceToolbar(new PixelRect(0, 0, 1920, 1040), toolbar, screens), Is.EqualTo(new PixelPoint(760, 996)));
            Assert.That(RecordingControlWindow.PlaceToolbar(new PixelRect(1700, 100, 100, 100), toolbar, screens), Is.EqualTo(new PixelPoint(1520, 204)),
                "kept on the screen it is below");
        });
    }

    private static RecordingControlWindow Show(Coordinator coordinator, RecordingOptions options)
    {
        var window = new RecordingControlWindow(coordinator, options);
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Click(Window window, string content)
    {
        var button = window.GetLogicalDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && Equals(b.Content, content));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private sealed class Coordinator : IScreenRecordingCoordinator
    {
        public int Aborts, Starts, Stops;
        public event EventHandler<RecordingStatusEventArgs>? StatusChanged { add { } remove { } }
        public event EventHandler<RecordingErrorEventArgs>? ErrorOccurred { add { } remove { } }
        public event EventHandler<RecordingStartedEventArgs>? RecordingStarted { add { } remove { } }
        public bool IsRecording => true;
        public bool IsPaused => false;
        public bool IsUsingFallback => false;
        public RecordingRuntimeCapabilities CurrentCapabilities => new(RecordingPauseBehavior.NativePauseResume, true);
        public Task? PlatformInitializationTask { get; set; }
        public Task StartRecordingAsync(RecordingOptions options) => Task.CompletedTask;
        public Task<string?> StopRecordingAsync() => Task.FromResult<string?>(null);
        public Task AbortRecordingAsync() { Aborts++; return Task.CompletedTask; }
        public Task TogglePauseResumeAsync() => Task.CompletedTask;
        public void SignalStop() => Stops++;
        public void SignalStart() => Starts++;
    }
}
