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

using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using XerahS.Common;
using XerahS.Platform.Abstractions;
using XerahS.Platform.Linux.Services;

namespace XerahS.Tests.Platform;

[TestFixture, NonParallelizable]
public class LinuxShellIntegrationTests
{
    private string _root = null!;
    private LinuxXdgDirectories _xdg = null!;
    private LinuxShellIntegrationService _service = null!;
    [SetUp] public void Setup()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Linux desktop integration");
        _root = Path.Combine(Path.GetTempPath(), "xerahs-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _xdg = LinuxXdgDirectories.Resolve(_ => null, _root);
        _service = new LinuxShellIntegrationService(_xdg, "/opt/My XerahS.AppImage");
    }
    [TearDown] public void Cleanup() { if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Test]
    public void Menus_RefreshStableExecutable_PreserveOtherThunarActions_AndAreExecutable()
    {
        string thunar = Path.Combine(_xdg.ConfigHome, "Thunar", "uca.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(thunar)!);
        File.WriteAllText(thunar, "<actions><action><name>Keep me</name><unique-id>other</unique-id></action></actions>");
        Assert.That(_service.SetContextMenuIntegration(true), Is.True);
        Assert.That(_service.SetIntegrationEnabled(ShellIntegrationKind.ImageEditor, true), Is.True);
        Assert.That(_service.SetSendToIntegration(true), Is.True);
        string kde = Path.Combine(_xdg.DataHome, "kio", "servicemenus", "XerahS.desktop");
        if (OperatingSystem.IsLinux()) Assert.That(File.GetUnixFileMode(kde).HasFlag(UnixFileMode.UserExecute), Is.True);
        var moved = new LinuxShellIntegrationService(_xdg, "/opt/Moved.AppImage");
        moved.RefreshRegisteredEntries();
        Assert.That(File.ReadAllText(kde), Does.Contain("/opt/Moved.AppImage").And.Not.Contains("My XerahS"));
        Assert.That(XDocument.Load(thunar).Root!.Elements("action").Count(), Is.EqualTo(3));
        Assert.That(_service.SetContextMenuIntegration(false), Is.True);
        Assert.That(_service.SetIntegrationEnabled(ShellIntegrationKind.ImageEditor, false), Is.True);
        Assert.That(_service.SetSendToIntegration(false), Is.True);
        Assert.That(XDocument.Load(thunar).Root!.Elements("action").Single().Element("unique-id")!.Value, Is.EqualTo("other"));
        Assert.That(File.Exists(kde), Is.False);
    }

    [Test]
    public void InvalidThunarActions_ArePreservedAndRegistrationReportsFailure()
    {
        string path = Path.Combine(_xdg.ConfigHome, "Thunar", "uca.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<actions><broken>");
        Assert.That(_service.SetContextMenuIntegration(true), Is.False);
        Assert.That(_service.IsContextMenuIntegrationEnabled(), Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo("<actions><broken>"));
    }

    [Test]
    public void MimeAssociations_RestorePreviousDefault_AndPreserveUnrelatedEntries()
    {
        string path = Path.Combine(_xdg.ConfigHome, "mimeapps.list");
        Directory.CreateDirectory(_xdg.ConfigHome);
        File.WriteAllText(path, "[Default Applications]\napplication/x-sharex-custom-uploader=other.desktop;\ntext/plain=editor.desktop;\n[Added Associations]\ntext/plain=more.desktop;\n");
        Assert.That(_service.SetIntegrationEnabled(ShellIntegrationKind.CustomUploader, true), Is.True);
        Assert.That(File.ReadAllText(path), Does.Contain("application/x-sharex-custom-uploader=xerahs-sxcu.desktop;other.desktop;"));
        Assert.That(_service.SetIntegrationEnabled(ShellIntegrationKind.CustomUploader, false), Is.True);
        Assert.That(File.ReadAllText(path), Does.Contain("application/x-sharex-custom-uploader=other.desktop;").And.Contains("text/plain=more.desktop;"));
    }

    [TestCase(ShellIntegrationKind.Chrome, "com.getsharex.sharex.json")]
    [TestCase(ShellIntegrationKind.Firefox, "ShareX.json")]
    public void BrowserManifest_UsesShareXExtensionIdentityAndExecutableHost(ShellIntegrationKind kind, string name)
    {
        Assert.That(_service.SetIntegrationEnabled(kind, true), Is.True);
        string manifest = Directory.GetFiles(_root, name, SearchOption.AllDirectories).First();
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        string host = doc.RootElement.GetProperty("path").GetString()!;
        Assert.That(File.ReadAllText(host), Does.Contain("--native-messaging-host").And.Contains("/opt/My XerahS.AppImage"));
        if (OperatingSystem.IsLinux()) Assert.That(File.GetUnixFileMode(host).HasFlag(UnixFileMode.UserExecute), Is.True);
        string identity = kind == ShellIntegrationKind.Chrome ? "chrome-extension://nlkoigbdolhchiicbonbihbphgamnaoc/" : "firefox@getsharex.com";
        Assert.That(File.ReadAllText(manifest), Does.Contain(identity));
        Assert.That(_service.SetIntegrationEnabled(kind, false), Is.True);
        Assert.That(File.Exists(manifest), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Scripts_KeepFilenamesIntactWithSpacesQuotesAndShellCharacters(bool selectionInEnvironment)
    {
        if (!OperatingSystem.IsLinux()) return;
        string executable = Path.Combine(_root, "XerahS ' $test;.AppImage"), output = Path.Combine(_root, "arguments");
        File.WriteAllText(executable, "#!/bin/sh\nprintf '%s\\0' \"$@\" > " + LinuxShellIntegrationService.QuoteShell(output) + "\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var service = new LinuxShellIntegrationService(_xdg, executable);
        Assert.That(service.SetContextMenuIntegration(true), Is.True);
        string script = Path.Combine(_xdg.DataHome, "nautilus", "scripts", "Upload with XerahS");
        string[] paths = { "/tmp/file with spaces.txt", "/tmp/single'quote;$HOME.png" };
        var start = new ProcessStartInfo(script) { UseShellExecute = false };
        if (selectionInEnvironment) start.Environment["NAUTILUS_SCRIPT_SELECTED_FILE_PATHS"] = string.Join('\n', paths) + "\n";
        else foreach (string path in paths) start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero);
        Assert.That(File.ReadAllText(output).Split('\0', StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(paths));
    }

    [Test]
    public async Task DesktopAssociation_LaunchesThroughGioWithLiteralExecutableAndFileArguments()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/gio")) Assert.Ignore("Requires Linux GIO");
        string executable = Path.Combine(_root, "XerahS ' \" $percent%test=ok.AppImage");
        string output = Path.Combine(_root, "desktop-arguments");
        File.WriteAllText(executable, "#!/bin/sh\nprintf '%s\\0' \"$@\" > " + LinuxShellIntegrationService.QuoteShell(output) + "\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var service = new LinuxShellIntegrationService(_xdg, executable);
        Assert.That(service.SetIntegrationEnabled(ShellIntegrationKind.CustomUploader, true), Is.True);
        string desktop = Path.Combine(_xdg.DataHome, "applications", "xerahs-sxcu.desktop");
        string file = Path.Combine(_root, "file ' with $chars.sxcu");
        File.WriteAllText(file, "{}");
        var start = new ProcessStartInfo("/usr/bin/gio") { UseShellExecute = false, RedirectStandardError = true };
        start.ArgumentList.Add("launch"); start.ArgumentList.Add(desktop); start.ArgumentList.Add(file);
        using var process = Process.Start(start)!;
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, error);
        for (int attempt = 0; attempt < 50 && !File.Exists(output); attempt++) await Task.Delay(20);
        Assert.That(File.ReadAllText(output).Split('\0', StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(new[] { "-CustomUploader", file }));
    }

    [Test]
    public void Refresh_PreservesDefaultsAndBrowserManifestsChangedOutsideXerahS()
    {
        _service.SetIntegrationEnabled(ShellIntegrationKind.CustomUploader, true);
        _service.SetIntegrationEnabled(ShellIntegrationKind.Chrome, true);
        string mimeApps = Path.Combine(_xdg.ConfigHome, "mimeapps.list");
        File.WriteAllText(mimeApps, "[Default Applications]\napplication/x-sharex-custom-uploader=other.desktop;\n");
        string chrome = Path.Combine(_xdg.ConfigHome, "google-chrome", "NativeMessagingHosts", "com.getsharex.sharex.json");
        File.WriteAllText(chrome, "{\"path\":\"/other/native-host\"}");
        new LinuxShellIntegrationService(_xdg, "/opt/Moved.AppImage").RefreshRegisteredEntries();
        Assert.That(File.ReadAllText(mimeApps), Does.Contain("=other.desktop;").And.Not.Contains("=xerahs"));
        Assert.That(File.ReadAllText(chrome), Does.Contain("/other/native-host"));
    }

    [Test]
    public void Refresh_LeavesUnchangedEntriesAlone()
    {
        Assert.That(_service.SetContextMenuIntegration(true), Is.True);
        Assert.That(_service.SetIntegrationEnabled(ShellIntegrationKind.CustomUploader, true), Is.True);
        DateTime earlier = DateTime.UtcNow.AddDays(-1);
        string[] files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);
        foreach (string file in files) File.SetLastWriteTimeUtc(file, earlier);

        // Startup with the same executable rewrites nothing.
        new LinuxShellIntegrationService(_xdg, "/opt/My XerahS.AppImage").RefreshRegisteredEntries();

        Assert.That(files.Where(file => File.GetLastWriteTimeUtc(file) != earlier), Is.Empty);
    }

    [Test]
    public void Refresh_DoesNotEnableUnregisteredFeatures()
    {
        _service.RefreshRegisteredEntries();
        Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.Empty);
    }
}
