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
using System.Text.Json;
using NUnit.Framework;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Tests.Platform.Linux;

/// <summary>ShareX's capture preparation on KDE Plasma: panel hiding, desktop icon hiding, and crash recovery.</summary>
[TestFixture]
public sealed class KdeCapturePreparationServiceTests
{
    // Three 1920x1080 screens side by side (Plasma's screen 0 is the middle one here), each with a bottom panel.
    private const string PanelsJson = """
        {"screens":[[1920,0,1920,1080],[0,0,1920,1080],[3840,0,1920,1080]],
         "panels":[{"id":4,"screen":0,"location":"bottom","hiding":"none","height":44,"alignment":"center","offset":0,"maxLength":1920,"lengthMode":"fill"},
                   {"id":167,"screen":1,"location":"bottom","hiding":"none","height":44,"alignment":"center","offset":0,"maxLength":1920,"lengthMode":"fill"},
                   {"id":192,"screen":2,"location":"bottom","hiding":"autohide","height":44,"alignment":"center","offset":0,"maxLength":1920,"lengthMode":"fill"}]}
        """;

    private const string DesktopsJson = """[{"id":1,"filterMode":"","filterPattern":""},{"id":2,"filterMode":"2","filterPattern":"*.tmp"}]""";

    private string _directory = null!;
    private string _marker = null!;
    private FakeShell _shell = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-kde-prep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _marker = Path.Combine(_directory, KdeCapturePreparationService.MarkerFileName);
        _shell = new FakeShell();
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    private KdeCapturePreparationService Service(Func<KWinSnapshot?>? windows = null, double scale = 1) =>
        new(_shell, _marker, windows ?? (() => null), () => scale)
        {
            PanelSettleDelay = TimeSpan.Zero, IconSettleDelay = TimeSpan.Zero, PanelHideTimeout = TimeSpan.FromMilliseconds(200), PanelPollInterval = TimeSpan.FromMilliseconds(10),
            RecoveryRetryDelay = TimeSpan.Zero
        };

    [Test]
    public void PanelBounds_FollowTheScreenEdge_Length_AndAlignment()
    {
        Rectangle[] screens = [new(0, 0, 1920, 1080), new(1920, 0, 2560, 1440)];
        Assert.Multiple(() =>
        {
            Assert.That(KdeCapturePreparationService.GetPanelBounds(new PlasmaPanel(1, 0, "bottom", "none", 44, "center", 0, 1920, "fill"), screens),
                Is.EqualTo(new Rectangle(0, 1036, 1920, 44)));
            Assert.That(KdeCapturePreparationService.GetPanelBounds(new PlasmaPanel(2, 1, "top", "none", 30, "left", 100, 800, "custom"), screens),
                Is.EqualTo(new Rectangle(2020, 0, 800, 30)));
            Assert.That(KdeCapturePreparationService.GetPanelBounds(new PlasmaPanel(3, 1, "right", "none", 48, "right", 0, 1000, "custom"), screens),
                Is.EqualTo(new Rectangle(4432, 440, 48, 1000)));
            Assert.That(KdeCapturePreparationService.GetPanelBounds(new PlasmaPanel(4, 0, "left", "none", 48, "center", 0, 600, "fit"), screens),
                Is.EqualTo(new Rectangle(0, 240, 48, 600)));
            Assert.That(KdeCapturePreparationService.GetPanelBounds(new PlasmaPanel(5, 7, "bottom", "none", 44, "center", 0, 0, "fill"), screens), Is.Null, "Unknown screen.");
        });
    }

    [Test]
    public async Task HidePanels_HidesOnlyThePanelsTheWindowIntersects_AndRestoresTheirModes()
    {
        _shell.Answers[KdeCapturePreparationService.BuildPanelQueryScript()] = PanelsJson;
        var service = Service();
        // A window on the left screen that reaches into its panel.
        await using (IAsyncDisposable? scope = await service.HidePanelsIntersectingAsync(new Rectangle(100, 500, 800, 600), CancellationToken.None))
        {
            Assert.That(scope, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(_shell.Scripts, Has.Count.EqualTo(2));
                Assert.That(_shell.Scripts[1], Does.Contain("panelById(167)").And.Contain("hiding = \"autohide\"").And.Not.Contain("panelById(4)"));
                Assert.That(File.Exists(_marker), Is.True, "The original mode is written before the panel is hidden.");
                var state = JsonSerializer.Deserialize<KdeCapturePreparationService.PreparationState>(File.ReadAllText(_marker))!;
                Assert.That(state.Panels.Select(p => (p.Id, p.Hiding)), Is.EqualTo(new[] { (167, "none") }));
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(_shell.Scripts, Has.Count.EqualTo(3));
            Assert.That(_shell.Scripts[2], Does.Contain("panelById(167)").And.Contain("hiding = \"none\""));
            Assert.That(File.Exists(_marker), Is.False, "Nothing is left to recover.");
        });
    }

    [Test]
    public async Task HidePanels_ReturnsNull_WhenNoPanelIntersects_OrTheyAlreadyAutoHide()
    {
        _shell.Answers[KdeCapturePreparationService.BuildPanelQueryScript()] = PanelsJson;
        var service = Service();
        Assert.Multiple(async () =>
        {
            Assert.That(await service.HidePanelsIntersectingAsync(new Rectangle(100, 100, 800, 600), CancellationToken.None), Is.Null, "Above the panel.");
            Assert.That(await service.HidePanelsIntersectingAsync(new Rectangle(4000, 900, 800, 600), CancellationToken.None), Is.Null, "That panel auto-hides already.");
            Assert.That(await service.HidePanelsIntersectingAsync(new Rectangle(100, 900, 0, 0), CancellationToken.None), Is.Null, "Empty area.");
        });
        Assert.That(_shell.Scripts.All(script => script == KdeCapturePreparationService.BuildPanelQueryScript()), Is.True, "Nothing was changed.");
        Assert.That(File.Exists(_marker), Is.False);
    }

    [Test]
    public async Task HidePanels_UsesX11Coordinates_AndWaitsUntilKWinNoLongerShowsThePanel()
    {
        _shell.Answers[KdeCapturePreparationService.BuildPanelQueryScript()] = PanelsJson;
        int polls = 0;
        KWinSnapshot Snapshot() => new(
        [
            // KWin's logical coordinates: the panel at y 1036, then slid below the screen at y 1080.
            new KWinWindow("{panel}", "Panel", "plasmashell", 10, polls++ < 2 ? new Rectangle(0, 1036, 1920, 44) : new Rectangle(0, 1080, 1920, 44),
                new Rectangle(0, 1036, 1920, 44), false, false, false, false, false, Dock: true)
        ], null, Point.Empty);
        var service = Service(Snapshot, scale: 2);
        // In doubled X11 coordinates the left screen's panel is at y 2072; a window ending at y 2100 intersects it.
        await using IAsyncDisposable? scope = await service.HidePanelsIntersectingAsync(new Rectangle(200, 1000, 1600, 1100), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(scope, Is.Not.Null);
            Assert.That(polls, Is.EqualTo(3), "Two polls still showed the panel over the window; the third did not.");
        });
    }

    [Test]
    public async Task HideDesktopIcons_FiltersEveryFolderView_RestoresOriginals_AndSharesNestedScopes()
    {
        _shell.Answers[KdeCapturePreparationService.BuildDesktopQueryScript()] = DesktopsJson;
        var service = Service();
        IAsyncDisposable? outer = await service.HideDesktopIconsAsync(CancellationToken.None);
        IAsyncDisposable? inner = await service.HideDesktopIconsAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outer, Is.Not.Null);
            Assert.That(inner, Is.Not.Null);
            Assert.That(_shell.Scripts, Has.Count.EqualTo(2), "The second scope reuses the first one's change.");
            Assert.That(_shell.Scripts[1], Does.Contain("desktopById(1)").And.Contain("desktopById(2)")
                .And.Contain("writeConfig(\"filterMode\", 1)").And.Contain(KdeCapturePreparationService.HiddenIconsPattern).And.Contain("reloadConfig()"));
            Assert.That(File.Exists(_marker), Is.True);
        });

        await inner!.DisposeAsync();
        Assert.That(_shell.Scripts, Has.Count.EqualTo(2), "The icons stay hidden until the last scope ends.");
        await outer!.DisposeAsync();
        await outer.DisposeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(_shell.Scripts, Has.Count.EqualTo(3));
            Assert.That(_shell.Scripts[2], Does.Contain("desktopById(1)").And.Contain("writeConfig(\"filterMode\", 0); d1.writeConfig(\"filterPattern\", \"\")")
                .And.Contain("desktopById(2)").And.Contain("writeConfig(\"filterMode\", 2); d2.writeConfig(\"filterPattern\", \"*.tmp\")"),
                "An unset mode is restored as 0, a set filter as it was.");
            Assert.That(File.Exists(_marker), Is.False);
        });
    }

    [Test]
    public void ACaptureCancelledWhileTheIconsSettle_ShowsThemAgain()
    {
        _shell.Answers[KdeCapturePreparationService.BuildDesktopQueryScript()] = DesktopsJson;
        var service = Service();
        service.IconSettleDelay = TimeSpan.FromSeconds(30);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(async () => await service.HideDesktopIconsAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(_shell.Scripts, Has.Count.EqualTo(3));
            Assert.That(_shell.Scripts[2], Does.Contain("writeConfig(\"filterMode\", 2); d2.writeConfig(\"filterPattern\", \"*.tmp\")"));
            Assert.That(File.Exists(_marker), Is.False);
        });
    }

    [Test]
    public void ACaptureCancelledWhileThePanelsHide_ShowsThemAgain()
    {
        _shell.Answers[KdeCapturePreparationService.BuildPanelQueryScript()] = PanelsJson;
        var service = Service();
        service.PanelSettleDelay = TimeSpan.FromSeconds(30);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(async () => await service.HidePanelsIntersectingAsync(new Rectangle(100, 500, 800, 600), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(_shell.Scripts, Has.Count.EqualTo(3));
            Assert.That(_shell.Scripts[2], Does.Contain("panelById(167)").And.Contain("hiding = \"none\""));
            Assert.That(File.Exists(_marker), Is.False);
        });
    }

    [Test]
    public async Task HideDesktopIcons_LeavesThemAlone_WhenTheActiveWindowIsFullScreen()
    {
        _shell.Answers[KdeCapturePreparationService.BuildDesktopQueryScript()] = DesktopsJson;
        KWinSnapshot Snapshot() => new(
            [new KWinWindow("{game}", "Game", "game", 10, new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080), false, false, false, false, true, FullScreen: true)],
            "{game}", Point.Empty);
        var service = Service(Snapshot);
        Assert.That(await service.HideDesktopIconsAsync(CancellationToken.None), Is.Null);
        Assert.That(_shell.Scripts, Is.Empty);
    }

    [Test]
    public async Task Recover_UndoesWhatAnEarlierRunLeftBehind()
    {
        File.WriteAllText(_marker, """{"panels":[{"id":4,"hiding":"dodgewindows"}],"desktops":[{"id":1,"filterMode":"","filterPattern":""}]}""");
        await Service().RecoverAsync();
        Assert.Multiple(() =>
        {
            Assert.That(_shell.Scripts, Has.Count.EqualTo(2));
            Assert.That(_shell.Scripts[0], Does.Contain("panelById(4)").And.Contain("hiding = \"dodgewindows\""));
            Assert.That(_shell.Scripts[1], Does.Contain("desktopById(1)").And.Contain("writeConfig(\"filterMode\", 0)"));
            Assert.That(File.Exists(_marker), Is.False);
        });

        await Service().RecoverAsync();
        Assert.That(_shell.Scripts, Has.Count.EqualTo(2), "Nothing to recover the second time.");
    }

    [Test]
    public async Task PanelRestore_ThatTheShellDoesNotConfirm_KeepsTheModeAndIsRetriedAfterTheNextCapture()
    {
        _shell.Answers[KdeCapturePreparationService.BuildPanelQueryScript()] = PanelsJson;
        var service = Service();
        IAsyncDisposable? first = await service.HidePanelsIntersectingAsync(new Rectangle(100, 500, 800, 600), CancellationToken.None);
        _shell.Unanswered = true;
        await first!.DisposeAsync();
        Assert.That(File.Exists(_marker), Is.True, "The panel's mode stays in the recovery file.");
        Assert.That(ReadMarker().Panels.Select(p => (p.Id, p.Hiding)), Is.EqualTo(new[] { (167, "none") }));

        _shell.Unanswered = false;
        IAsyncDisposable? second = await service.HidePanelsIntersectingAsync(new Rectangle(2000, 500, 800, 600), CancellationToken.None);
        await second!.DisposeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(_shell.Scripts[^1], Does.Contain("panelById(167)").And.Contain("panelById(4)").And.Contain("hiding = \"none\""));
            Assert.That(File.Exists(_marker), Is.False);
        });
    }

    [Test]
    public async Task DesktopRestore_ThatTheShellDoesNotConfirm_KeepsTheOriginalFilters()
    {
        _shell.Answers[KdeCapturePreparationService.BuildDesktopQueryScript()] = DesktopsJson;
        var service = Service();
        IAsyncDisposable? first = await service.HideDesktopIconsAsync(CancellationToken.None);
        _shell.Unanswered = true;
        await first!.DisposeAsync();
        Assert.That(ReadMarker().Desktops.Select(d => (d.Id, d.FilterPattern)), Is.EqualTo(new[] { (1, ""), (2, "*.tmp") }));

        // The filters still hide the icons, so reading them again would take XerahS's filter for the user's.
        _shell.Unanswered = false;
        _shell.Answers[KdeCapturePreparationService.BuildDesktopQueryScript()] =
            $$"""[{"id":1,"filterMode":"1","filterPattern":"{{KdeCapturePreparationService.HiddenIconsPattern}}"},{"id":2,"filterMode":"1","filterPattern":"{{KdeCapturePreparationService.HiddenIconsPattern}}"}]""";
        IAsyncDisposable? second = await service.HideDesktopIconsAsync(CancellationToken.None);
        await second!.DisposeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(_shell.Scripts[^1], Does.Contain("writeConfig(\"filterMode\", 2); d2.writeConfig(\"filterPattern\", \"*.tmp\")"));
            Assert.That(File.Exists(_marker), Is.False);
        });
    }

    [Test]
    public async Task HideDesktopIcons_DoesNotKeepXerahSsOwnFilterAsTheOriginal()
    {
        _shell.Answers[KdeCapturePreparationService.BuildDesktopQueryScript()] =
            $$"""[{"id":1,"filterMode":"1","filterPattern":"{{KdeCapturePreparationService.HiddenIconsPattern}}"}]""";
        var service = Service();
        IAsyncDisposable? scope = await service.HideDesktopIconsAsync(CancellationToken.None);
        await scope!.DisposeAsync();
        Assert.That(_shell.Scripts[^1], Does.Contain("writeConfig(\"filterMode\", 0); d1.writeConfig(\"filterPattern\", \"\")"));
    }

    [Test]
    public async Task Recover_KeepsTheRecoveryFile_UntilTheShellConfirms_AndTriesAgain()
    {
        File.WriteAllText(_marker, """{"panels":[{"id":4,"hiding":"dodgewindows"}],"desktops":[{"id":1,"filterMode":"2","filterPattern":"*.tmp"}]}""");
        _shell.Unanswered = true;
        var service = Service();
        service.RecoveryAttempts = 2;
        await service.RecoverAsync();
        Assert.That(File.Exists(_marker), Is.True, "Nothing the shell did not confirm is forgotten.");
        Assert.That(ReadMarker().Panels.Select(p => (p.Id, p.Hiding)), Is.EqualTo(new[] { (4, "dodgewindows") }));
        Assert.That(ReadMarker().Desktops.Select(d => d.FilterPattern), Is.EqualTo(new[] { "*.tmp" }));
        Assert.That(_shell.Scripts, Has.Count.EqualTo(4), "Two attempts, each for the panels and the desktops.");

        _shell.Unanswered = false;
        await Service().RecoverAsync();
        Assert.That(File.Exists(_marker), Is.False);
    }

    [Test]
    public async Task Unavailable_ShellDoesNothing()
    {
        _shell.Available = false;
        var service = Service();
        Assert.Multiple(async () =>
        {
            Assert.That(service.CanHidePanels, Is.False);
            Assert.That(await service.HidePanelsIntersectingAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None), Is.Null);
            Assert.That(await service.HideDesktopIconsAsync(CancellationToken.None), Is.Null);
        });
        Assert.That(_shell.Scripts, Is.Empty);
    }

    private KdeCapturePreparationService.PreparationState ReadMarker() =>
        JsonSerializer.Deserialize<KdeCapturePreparationService.PreparationState>(File.ReadAllText(_marker))!;

    private sealed class FakeShell : IPlasmaShellScripting
    {
        public bool Available = true;
        /// <summary>Stands for a shell that does not answer: only the queries in <see cref="Answers"/> get a reply.</summary>
        public bool Unanswered;
        public Dictionary<string, string> Answers { get; } = new();
        public List<string> Scripts { get; } = new();

        public bool IsAvailable => Available;

        public Task<string?> EvaluateAsync(string script, CancellationToken token)
        {
            Scripts.Add(script);
            return Task.FromResult<string?>(Answers.TryGetValue(script, out string? answer) ? answer : Unanswered ? null : "ok");
        }
    }
}
