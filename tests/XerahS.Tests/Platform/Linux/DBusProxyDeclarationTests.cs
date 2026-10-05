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
using XerahS.Platform.Linux;
using XerahS.Platform.Linux.Services;

namespace XerahS.Tests.Platform.Linux;

/// <summary>
/// Tmds.DBus checks an interface's declaration only when the first proxy is created, at run time.
/// Creating the proxies here needs no bus, so a declaration it rejects fails a test instead.
/// Properties are read through each interface's own GetAsync(string).
/// </summary>
[TestFixture]
public class DBusProxyDeclarationTests
{
    private static IEnumerable<Type> DeclaredInterfaces() =>
        typeof(LinuxPlatform).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.IsPublic && typeof(IDBusObject).IsAssignableFrom(type) &&
                type.GetCustomAttributes(typeof(DBusInterfaceAttribute), false).Length > 0)
            .OrderBy(type => type.FullName);

    [TestCaseSource(nameof(DeclaredInterfaces))]
    public void ProxyCanBeCreated(Type interfaceType)
    {
        using var connection = new Connection("unix:path=/nonexistent/xerahs-test-bus");
        var createProxy = typeof(Connection).GetMethods()
            .Single(method => method.Name == nameof(Connection.CreateProxy) && method.IsGenericMethod && method.GetParameters().Length == 2)
            .MakeGenericMethod(interfaceType);
        Assert.DoesNotThrow(() => createProxy.Invoke(connection, ["org.example.XerahSTest", new ObjectPath("/org/example/XerahSTest")]));
    }

    [Test]
    public async Task PropertyReadReturnsTheNumberOrNullWhenItCannotBeRead()
    {
        Assert.That(await PortalInterfaceChecker.TryGetUInt32PropertyAsync(_ => Task.FromResult<object>(2u), "version"), Is.EqualTo(2u));
        Assert.That(await PortalInterfaceChecker.TryGetUInt32PropertyAsync(
            _ => Task.FromException<object>(new InvalidOperationException("No such property")), "version"), Is.Null);
        Assert.That(await PortalInterfaceChecker.TryGetUInt32PropertyAsync(_ => Task.FromResult<object>("two"), "version"), Is.Null);
        Assert.That(await PortalInterfaceChecker.TryGetUInt32PropertyAsync(_ => Task.FromResult<object>(null!), "version"), Is.Null,
            "A missing value is unknown, not version 0.");
    }

    [TestCase(1u, false), TestCase(2u, true), TestCase(3u, true), TestCase(null, true)]
    public void ConfigureShortcutsNeedsGlobalShortcutsVersionTwoUnlessTheVersionIsUnknown(uint? version, bool expected) =>
        Assert.That(WaylandPortalHotkeyService.SupportsConfigureShortcuts(version), Is.EqualTo(expected));
}
