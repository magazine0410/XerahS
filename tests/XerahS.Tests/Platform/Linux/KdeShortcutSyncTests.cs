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

using Avalonia.Input;
using NUnit.Framework;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux.Services.Kde;

namespace XerahS.Tests.Platform.Linux;

public class KdeShortcutSyncTests
{
    // Values read from kglobalaccel on KDE Plasma 6 (QKeyCombination::toCombined).
    private const int MetaPrint = 0x11000009;
    private const int CtrlPrint = 0x05000009;
    private const int Print = 0x01000009;
    private const int CtrlShiftR = 0x06000052;

    private sealed class FakeKdeGlobalAccel : IKdeGlobalAccel
    {
        public Dictionary<string, int> Keys { get; } = new();
        public HashSet<int> TakenByOtherApps { get; } = new();
        public List<string> Writes { get; } = new();
        public List<string> Removed { get; } = new();

        public Task<IReadOnlyList<string>> GetActionIdsAsync(string component) =>
            Task.FromResult<IReadOnlyList<string>>(Keys.Keys.ToList());

        public Task<int> GetKeyAsync(string component, string action) =>
            Task.FromResult(Keys.TryGetValue(action, out int key) ? key : 0);

        public Task SetKeyAsync(string component, string action, string actionFriendlyName, int key)
        {
            Writes.Add(action);
            // kglobalaccel drops keys another component already owns.
            Keys[action] = TakenByOtherApps.Contains(key) ? 0 : key;
            return Task.CompletedTask;
        }

        public Task UnregisterAsync(string component, string action)
        {
            Removed.Add(action);
            Keys.Remove(action);
            return Task.CompletedTask;
        }
    }

    private static HotkeyInfo Hotkey(Key key, KeyModifiers modifiers, string name = "Region capture") =>
        new(key, modifiers) { BindingName = name, Status = HotkeyStatus.Registered };

    [Test]
    public void QtKeyMapper_MatchesKdeValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QtKeyMapper.ToQt(new HotkeyInfo(Key.PrintScreen, KeyModifiers.Meta)), Is.EqualTo(MetaPrint));
            Assert.That(QtKeyMapper.ToQt(new HotkeyInfo(Key.Print, KeyModifiers.Control)), Is.EqualTo(CtrlPrint));
            Assert.That(QtKeyMapper.ToQt(new HotkeyInfo(Key.R, KeyModifiers.Control | KeyModifiers.Shift)), Is.EqualTo(CtrlShiftR));
            Assert.That(QtKeyMapper.ToQt(new HotkeyInfo(Key.Space, KeyModifiers.Control | KeyModifiers.Alt)), Is.EqualTo(0x0C000020));
            Assert.That(QtKeyMapper.ToQt(new HotkeyInfo(Key.NumPad5, KeyModifiers.Control)), Is.EqualTo(0x24000035));
            Assert.That(QtKeyMapper.ToQt(new HotkeyInfo(Key.None)), Is.EqualTo(0));
        });
    }

    [Test]
    public void QtKeyMapper_RoundTripsEveryMappedKey()
    {
        foreach (Key key in Enum.GetValues<Key>())
        {
            var hotkey = new HotkeyInfo(key, KeyModifiers.Control | KeyModifiers.Shift);
            int qt = QtKeyMapper.ToQt(hotkey);
            if (qt == 0)
                continue;

            Assert.That(QtKeyMapper.TryFromQt(qt, out var back, out var modifiers), Is.True, key.ToString());
            Assert.That(QtKeyMapper.ToQt(new HotkeyInfo(back, modifiers)), Is.EqualTo(qt), key.ToString());
        }

        Assert.That(QtKeyMapper.TryFromQt(0x01000100, out _, out _), Is.False, "Unmapped Qt key");
    }

    [Test]
    public async Task FirstSync_KeepsKeysChangedInKdeWhileXerahSWasClosed()
    {
        var accel = new FakeKdeGlobalAccel { Keys = { ["region"] = MetaPrint } };
        var sync = new KdeShortcutSync(accel, "xerahs");
        var hotkey = Hotkey(Key.PrintScreen, KeyModifiers.Control);

        var changed = await sync.SyncAsync([("region", hotkey)]);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.EqualTo(new[] { hotkey }));
            Assert.That(hotkey.Key, Is.EqualTo(Key.PrintScreen));
            Assert.That(hotkey.Modifiers, Is.EqualTo(KeyModifiers.Meta));
            Assert.That(accel.Writes, Is.Empty);
        });
    }

    [Test]
    public async Task FirstSync_WritesHotkeyWhenKdeHasNoKey()
    {
        var accel = new FakeKdeGlobalAccel { Keys = { ["region"] = 0 } };
        var sync = new KdeShortcutSync(accel, "xerahs");

        var changed = await sync.SyncAsync([("region", Hotkey(Key.PrintScreen, KeyModifiers.Control))]);

        Assert.That(changed, Is.Empty);
        Assert.That(accel.Keys["region"], Is.EqualTo(CtrlPrint));
    }

    [Test]
    public async Task EditInXerahS_IsWrittenToKde()
    {
        var accel = new FakeKdeGlobalAccel { Keys = { ["region"] = CtrlPrint } };
        var sync = new KdeShortcutSync(accel, "xerahs");
        var hotkey = Hotkey(Key.PrintScreen, KeyModifiers.Control);
        await sync.SyncAsync([("region", hotkey)]);

        hotkey.Modifiers = KeyModifiers.Shift | KeyModifiers.Meta;
        var changed = await sync.SyncAsync([("region", hotkey)]);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.Empty);
            Assert.That(accel.Keys["region"], Is.EqualTo(0x13000009));
            Assert.That(hotkey.Status, Is.EqualTo(HotkeyStatus.Registered));
        });
    }

    [Test]
    public async Task EditInKde_IsCopiedToHotkey_AndRemovalClearsIt()
    {
        var accel = new FakeKdeGlobalAccel { Keys = { ["region"] = CtrlPrint } };
        var sync = new KdeShortcutSync(accel, "xerahs");
        var hotkey = Hotkey(Key.PrintScreen, KeyModifiers.Control);
        await sync.SyncAsync([("region", hotkey)]);

        accel.Keys["region"] = Print;
        var changed = await sync.SyncAsync([("region", hotkey)]);
        Assert.That(changed, Is.EqualTo(new[] { hotkey }));
        Assert.That(hotkey.ToString(), Is.EqualTo("Print Screen"));
        Assert.That(accel.Writes, Is.Empty);

        accel.Keys["region"] = 0;
        changed = await sync.SyncAsync([("region", hotkey)]);
        Assert.That(changed, Is.EqualTo(new[] { hotkey }));
        Assert.That(hotkey.IsValid, Is.False);
    }

    [Test]
    public async Task KeyRefusedByKde_MarksHotkeyFailed_AndIsNotClearedLater()
    {
        var accel = new FakeKdeGlobalAccel { Keys = { ["region"] = CtrlPrint } };
        var sync = new KdeShortcutSync(accel, "xerahs");
        var hotkey = Hotkey(Key.PrintScreen, KeyModifiers.Control);
        await sync.SyncAsync([("region", hotkey)]);

        accel.TakenByOtherApps.Add(MetaPrint);
        hotkey.Modifiers = KeyModifiers.Meta;
        await sync.SyncAsync([("region", hotkey)]);

        Assert.Multiple(() =>
        {
            Assert.That(hotkey.Status, Is.EqualTo(HotkeyStatus.Failed));
            Assert.That(hotkey.GetDisplayString(), Does.Contain("not assigned in KDE"));
        });

        hotkey.Status = HotkeyStatus.Registered;
        var changed = await sync.SyncAsync([("region", hotkey)]);
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.Empty);
            Assert.That(hotkey.Modifiers, Is.EqualTo(KeyModifiers.Meta));
            Assert.That(hotkey.Status, Is.EqualTo(HotkeyStatus.Failed));
        });
    }

    [Test]
    public async Task KdeKeyWithoutXerahSEquivalent_KeepsHotkeyAndIsNotOverwritten()
    {
        const int metaLaunchMail = 0x110000A0; // Qt::Key_LaunchMail is not a XerahS key.
        var accel = new FakeKdeGlobalAccel { Keys = { ["region"] = metaLaunchMail } };
        var sync = new KdeShortcutSync(accel, "xerahs");
        var hotkey = Hotkey(Key.PrintScreen, KeyModifiers.Control);

        await sync.SyncAsync([("region", hotkey)]);
        await sync.SyncAsync([("region", hotkey)]);

        Assert.Multiple(() =>
        {
            Assert.That(hotkey.Modifiers, Is.EqualTo(KeyModifiers.Control));
            Assert.That(accel.Keys["region"], Is.EqualTo(metaLaunchMail));
            Assert.That(accel.Writes, Is.Empty);
        });
    }

    [Test]
    public async Task Sync_RemovesKdeShortcutsNoHotkeyUses()
    {
        var accel = new FakeKdeGlobalAccel { Keys = { ["region"] = CtrlPrint, ["6"] = 0, ["7"] = 0 } };
        var sync = new KdeShortcutSync(accel, "xerahs");

        await sync.SyncAsync([("region", Hotkey(Key.PrintScreen, KeyModifiers.Control))]);

        Assert.That(accel.Removed, Is.EquivalentTo(new[] { "6", "7" }));
        Assert.That(accel.Keys.Keys, Is.EquivalentTo(new[] { "region" }));
    }

    [Test]
    public void SysRqCombination_IsAltWithEitherPrintKey()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new HotkeyInfo(Key.PrintScreen, KeyModifiers.Alt).IsSysRqCombination, Is.True);
            Assert.That(new HotkeyInfo(Key.Print, KeyModifiers.Alt | KeyModifiers.Control).IsSysRqCombination, Is.True);
            Assert.That(new HotkeyInfo(Key.PrintScreen, KeyModifiers.Meta).IsSysRqCombination, Is.False);
            Assert.That(new HotkeyInfo(Key.S, KeyModifiers.Alt).IsSysRqCombination, Is.False);
        });
    }
}
