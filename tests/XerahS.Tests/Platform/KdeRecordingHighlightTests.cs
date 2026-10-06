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
using Tmds.DBus;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Tests.Platform;

[TestFixture]
public sealed class KdeRecordingHighlightTests
{
    [TestCase("mouseclick:\nenabled: true\n", true)]
    [TestCase("enabled: false\r\n", false)]
    public void ReadsEnabledState(string info, bool enabled) => Assert.That(KdeRecordingHighlight.ParseEnabled(info), Is.EqualTo(enabled));

    [TestCase("KDE", true)]
    [TestCase("ubuntu:GNOME", false)]
    [TestCase("sway", false)]
    [TestCase(null, false)]
    public void OnlyKdeUsesTheKWinEffect(string? desktop, bool kde) => Assert.That(KdeRecordingHighlight.IsKdeSession(desktop), Is.EqualTo(kde));

    [Test]
    public void ReadsTheLiveClickLabelState() =>
        Assert.That(KdeRecordingHighlight.ParseShowText("mouseclick:\nshowText: false\nenabled: true\n"), Is.False);

    [Test]
    public void UnknownStateIsNotGuessed() => Assert.Throws<NotSupportedException>(() => KdeRecordingHighlight.ParseEnabled("mouseclick:"));

    [TestCase(null, "false", null)]
    [TestCase("true", "false", "true")]
    public async Task ClickLabel_IsHiddenWhileRecording_AndRestoredExactly(string? before, string during, string? after)
    {
        using var config = new FakeKwinrc(before);
        var setting = config.Setting();
        Assert.That(await setting.HideAsync(), Is.True);
        Assert.That(config.Value, Is.EqualTo(during));
        Assert.That(setting.IsPending, Is.True, "the previous value is saved before changing it");
        Assert.That(await setting.RestoreAsync(), Is.True);
        Assert.That(config.Value, Is.EqualTo(after), "an unset key is removed again rather than written");
        Assert.That(setting.IsPending, Is.False);
    }

    [Test]
    public async Task ClickLabel_AlreadyOff_IsLeftAlone()
    {
        using var config = new FakeKwinrc("false");
        var setting = config.Setting();
        Assert.That(await setting.HideAsync(), Is.False);
        Assert.That(config.Writes, Is.Zero);
        Assert.That(await setting.RestoreAsync(), Is.False);
    }

    [Test]
    public async Task ClickLabel_LeftOffByAnUnexpectedExit_IsRestoredFirst()
    {
        using var config = new FakeKwinrc("true");
        await config.Setting().HideAsync();          // XerahS exited during this recording
        Assert.That(config.Value, Is.EqualTo("false"));
        var next = config.Setting();
        Assert.That(next.IsPending, Is.True);
        Assert.That(await next.HideAsync(), Is.True, "the saved value is restored, then hidden again");
        Assert.That(await next.RestoreAsync(), Is.True);
        Assert.That(config.Value, Is.EqualTo("true"), "the user's own value survives, not the hidden one");
    }

    [Test]
    public async Task ClickLabel_UnchangeableConfig_IsNotMarkedAsChanged()
    {
        using var config = new FakeKwinrc("true") { FailWrites = true };
        var setting = config.Setting();
        Assert.That(await setting.HideAsync(), Is.False);
        Assert.That(setting.IsPending, Is.False);
    }

    private sealed class FakeKwinrc(string? value) : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "kwinrc-" + Guid.NewGuid().ToString("N"));
        public string? Value { get; private set; } = value;
        public int Writes { get; private set; }
        public bool FailWrites { get; init; }

        public KdeClickLabelSetting Setting() => new(RunAsync, Path.Combine(_directory, "saved"));

        private Task<(int, string)> RunAsync(string executable, IReadOnlyList<string> arguments)
        {
            Assert.That(arguments, Is.SupersetOf(new[] { "--file", "kwinrc", "--group", "Effect-mouseclick", "--key", "ShowText" }));
            if (executable == "kreadconfig6") return Task.FromResult((0, (Value ?? "") + "\n"));
            Writes++;
            if (FailWrites) return Task.FromResult((1, ""));
            Value = arguments[^1] == "--delete" ? null : arguments[^1];
            return Task.FromResult((0, ""));
        }

        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    [Test, Explicit("Briefly enables and restores the real KDE click effect; needs a desktop session.")]
    public async Task LiveKde_EnablesAndRestoresEffect()
    {
        using var connection = new Connection(Address.Session);
        await connection.ConnectAsync();
        var effects = connection.CreateProxy<IKWinRecordingEffects>("org.kde.KWin", new ObjectPath("/Effects"));
        bool loadedBefore = await effects.isEffectLoadedAsync("mouseclick");
        string before = await effects.supportInformationAsync("mouseclick");
        string labelBefore = await RecordingEncodingTestsRun("kreadconfig6", "--file kwinrc --group Effect-mouseclick --key ShowText");
        await using (await KdeRecordingHighlight.BeginAsync())
        {
            string during = await effects.supportInformationAsync("mouseclick");
            Assert.That(KdeRecordingHighlight.ParseEnabled(during), Is.True);
            Assert.That(KdeRecordingHighlight.ParseShowText(during), Is.False, "KWin itself must have dropped the click label");
        }
        Assert.That(await effects.isEffectLoadedAsync("mouseclick"), Is.EqualTo(loadedBefore));
        Assert.That(await effects.supportInformationAsync("mouseclick"), Is.EqualTo(before));
        Assert.That(await RecordingEncodingTestsRun("kreadconfig6", "--file kwinrc --group Effect-mouseclick --key ShowText"), Is.EqualTo(labelBefore));
        if (!loadedBefore)
        {
            // KWin's in-memory settings must be restored too, or the user's next use of the effect loses its label.
            await effects.loadEffectAsync("mouseclick");
            try
            {
                Assert.That(KdeRecordingHighlight.ParseShowText(await effects.supportInformationAsync("mouseclick")),
                    Is.EqualTo(!KdeClickLabelSetting.IsFalse(labelBefore.Trim())));
            }
            finally { await effects.unloadEffectAsync("mouseclick"); }
        }
    }

    private static Task<string> RecordingEncodingTestsRun(string executable, string arguments) =>
        XerahS.Tests.RegionCapture.RecordingEncodingTests.Run(executable, arguments);
}
