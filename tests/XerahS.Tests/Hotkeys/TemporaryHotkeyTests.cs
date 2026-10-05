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

namespace XerahS.Tests.Hotkeys;

[TestFixture]
public class TemporaryHotkeyTests
{
    [Test]
    public void OrdinaryBackendBindsTheKeyUntilDisposed()
    {
        var service = new FakeHotkeyService();
        var escape = new HotkeyInfo(Key.Escape);
        int presses = 0;
        var registration = TemporaryHotkeyRegistration.TryRegister(service, escape, () => presses++);
        Assert.That(registration, Is.Not.Null);
        service.Trigger(new HotkeyInfo(Key.F1) { Id = 99 });
        service.Trigger(escape);
        registration!.Dispose();
        service.Trigger(escape);
        Assert.Multiple(() =>
        {
            Assert.That(presses, Is.EqualTo(1), "Only its own key, and only while registered.");
            Assert.That(service.Registered, Is.Empty);
            Assert.That(service.Subscribers, Is.Zero);
        });
    }

    [Test]
    public void RefusedKeyLeavesNoSubscription()
    {
        var service = new FakeHotkeyService { Accept = false };
        Assert.That(TemporaryHotkeyRegistration.TryRegister(service, new HotkeyInfo(Key.Escape), () => { }), Is.Null);
        Assert.That(service.Subscribers, Is.Zero);
    }

    [Test]
    public async Task KdeBindingReportsOnlyItsOwnActionAndIsRemovedWhenDisposed()
    {
        var registry = new FakeKdeRegistry();
        int presses = 0;
        var shortcut = await KdeTemporaryShortcut.TryRegisterAsync(registry, 0x01000000, "Escape", () => presses++);
        Assert.That(shortcut, Is.Not.Null);
        string action = registry.Actions.Single();
        registry.Press("another-action");
        registry.Press(action);
        shortcut!.Dispose();
        registry.Press(action);
        Assert.Multiple(() =>
        {
            Assert.That(presses, Is.EqualTo(1));
            Assert.That(registry.Actions, Is.Empty);
            Assert.That(registry.CleanUps, Is.EqualTo(1));
            Assert.That(registry.Disposed, Is.True);
        });
    }

    [Test]
    public async Task EachKdeBindingHasItsOwnActionSoRemovingOneKeepsTheNext()
    {
        var registry = new FakeKdeRegistry();
        var first = await KdeTemporaryShortcut.TryRegisterAsync(registry, 0x01000000, "Escape", () => { });
        var second = await KdeTemporaryShortcut.TryRegisterAsync(registry, 0x01000000, "Escape", () => { });
        first!.Dispose();
        Assert.That(registry.Actions, Has.Count.EqualTo(1));
        Assert.That(second, Is.Not.Null);
    }

    [Test]
    public async Task KeyThatKdeDoesNotAssignIsNotKept()
    {
        var registry = new FakeKdeRegistry { Accept = false };
        Assert.That(await KdeTemporaryShortcut.TryRegisterAsync(registry, 0x01000000, "Escape", () => { }), Is.Null);
        Assert.That(registry.Actions, Is.Empty);
        Assert.That(registry.Watches, Is.Zero, "The press subscription is removed too.");
    }

    [Test]
    public async Task BindingsLeftByAnEarlierProcessAreRemoved()
    {
        var registry = new FakeKdeRegistry();
        registry.Actions.AddRange(["key-left-by-crash", "key-other"]);
        await KdeTemporaryShortcut.RemoveStaleAsync(registry);
        Assert.That(registry.Actions, Is.Empty);
        Assert.That(registry.CleanUps, Is.EqualTo(1));
    }

    private sealed class FakeHotkeyService : IHotkeyService
    {
        private EventHandler<HotkeyTriggeredEventArgs>? _triggered;
        public bool Accept { get; init; } = true;
        public List<HotkeyInfo> Registered { get; } = [];
        public int Subscribers => _triggered?.GetInvocationList().Length ?? 0;

        public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered
        {
            add => _triggered += value;
            remove => _triggered -= value;
        }

        public event EventHandler? HotkeysChanged { add { } remove { } }

        public bool RegisterHotkey(HotkeyInfo hotkeyInfo)
        {
            if (!Accept) return false;
            hotkeyInfo.Id = (ushort)(Registered.Count + 1);
            Registered.Add(hotkeyInfo);
            return true;
        }

        public bool UnregisterHotkey(HotkeyInfo hotkeyInfo) => Registered.Remove(hotkeyInfo);
        public void UnregisterAll() => Registered.Clear();
        public bool IsRegistered(HotkeyInfo hotkeyInfo) => Registered.Contains(hotkeyInfo);
        public bool IsSuspended { get; set; }
        public void Trigger(HotkeyInfo hotkey) => _triggered?.Invoke(this, new HotkeyTriggeredEventArgs(hotkey));
        public void Dispose() { }
    }

    private sealed class FakeKdeRegistry : IKdeShortcutRegistry
    {
        private readonly List<Action<string>> _watchers = [];
        public bool Accept { get; init; } = true;
        public List<string> Actions { get; } = [];
        public int CleanUps { get; private set; }
        public int Watches => _watchers.Count;
        public bool Disposed { get; private set; }

        public Task<bool> RegisterAsync(string component, string componentFriendlyName, string action, string actionFriendlyName, int qtKey)
        {
            Assert.That(component, Is.EqualTo(KdeTemporaryShortcut.Component));
            Actions.Add(action);
            return Task.FromResult(Accept);
        }

        public Task<IDisposable> WatchPressedAsync(string component, Action<string> pressed)
        {
            _watchers.Add(pressed);
            return Task.FromResult<IDisposable>(new Subscription(() => _watchers.Remove(pressed)));
        }

        public Task<IReadOnlyList<string>> GetActionIdsAsync(string component) => Task.FromResult<IReadOnlyList<string>>(Actions.ToArray());

        public Task UnregisterAsync(string component, string action)
        {
            Actions.Remove(action);
            return Task.CompletedTask;
        }

        public Task CleanUpAsync(string component)
        {
            CleanUps++;
            return Task.CompletedTask;
        }

        public void Press(string action)
        {
            foreach (var watcher in _watchers.ToArray()) watcher(action);
        }

        public void Dispose() => Disposed = true;

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
