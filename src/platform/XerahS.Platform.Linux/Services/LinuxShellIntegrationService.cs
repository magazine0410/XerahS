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
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using XerahS.Common;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux.Services;

public sealed class LinuxShellIntegrationService : IShellIntegrationService
{
    private const string Marker = "X-XerahS-Managed=true";
    private readonly LinuxXdgDirectories _xdg;
    private readonly string _executable;
    private readonly bool _updateDatabases;
    private string Applications => Path.Combine(_xdg.DataHome, "applications");
    private string ServiceMenus => Path.Combine(_xdg.DataHome, "kio", "servicemenus");
    private string ThunarActions => Path.Combine(_xdg.ConfigHome, "Thunar", "uca.xml");
    private string NativeHost => Path.Combine(_xdg.DataDirectory, "native-messaging-host");
    // GIO checks the first executable before expanding %% in its path. Use a fixed shell
    // executable and pass the application as a literal argument, also permitting '=' in its name.
    private string DesktopCommand => "/bin/sh -c \"" + EscapeDesktopArgument("exec \"$@\"") + "\" xerahs \"" + EscapeDesktopArgument(_executable) + "\"";

    public LinuxShellIntegrationService() : this(LinuxXdgDirectories.Detect(),
        LinuxStartupService.ResolveExecutablePath(Environment.GetEnvironmentVariable("APPIMAGE"), Environment.ProcessPath) ?? "", true) { }

    internal LinuxShellIntegrationService(LinuxXdgDirectories xdg, string executable, bool updateDatabases = false)
    {
        _xdg = xdg;
        _executable = executable;
        _updateDatabases = updateDatabases;
    }

    public bool SupportsPluginExtensionRegistration => OperatingSystem.IsLinux();
    public bool SupportsContextMenuIntegration => OperatingSystem.IsLinux();
    public bool SupportsSendToIntegration => OperatingSystem.IsLinux();
    public bool SupportsIntegration(ShellIntegrationKind kind) => OperatingSystem.IsLinux();

    public bool IsPluginExtensionRegistered() => IsAssociationEnabled("xsdp");
    public void SetPluginExtensionRegistration(bool register) => Apply(() => SetAssociation("xsdp", "application/x-xerahs-plugin", "XerahS plugin package", "", register));
    public bool IsContextMenuIntegrationEnabled() => ScriptPaths("Upload with XerahS").Any(File.Exists) || File.Exists(Path.Combine(ServiceMenus, "xerahs-upload.desktop"));
    public bool SetContextMenuIntegration(bool enable) => Apply(() => SetContextAction("upload", "Upload with XerahS", "", false, enable));
    public bool IsSendToIntegrationEnabled() => IsOwnedSendTo(Path.Combine(ServiceMenus, "XerahS.desktop")) || IsOwnedSendTo(Path.Combine(_xdg.DataHome, "Thunar", "sendto", "XerahS.desktop"));

    public bool SetSendToIntegration(bool enable) => Apply(() =>
    {
        string kde = Path.Combine(ServiceMenus, "XerahS.desktop");
        string thunar = Path.Combine(_xdg.DataHome, "Thunar", "sendto", "XerahS.desktop");
        if (enable)
        {
            Write(kde, BuildKdeEntry("SendToXerahS", "XerahS", AppContracts.Cli.SendToFlag, false, "X-KDE-Submenu=Send To\n"), true);
            Write(thunar, $"[Desktop Entry]\nType=Application\nName=Send to XerahS\nExec={DesktopCommand} {AppContracts.Cli.SendToFlag} %F\nIcon=xerahs\nMimeType=all/allfiles;inode/directory;\nTerminal=false\n{Marker}\n{AppContracts.LinuxIntegration.SendToMarkerKey}=true\n");
        }
        else
        {
            if (IsOwnedSendTo(kde)) File.Delete(kde);
            if (IsOwnedSendTo(thunar)) File.Delete(thunar);
        }
    });

    public bool IsIntegrationEnabled(ShellIntegrationKind kind) => kind switch
    {
        ShellIntegrationKind.ImageEditor => File.Exists(Path.Combine(ServiceMenus, "xerahs-edit.desktop")) || ScriptPaths("Edit with XerahS").Any(File.Exists),
        ShellIntegrationKind.CustomUploader => IsAssociationEnabled("sxcu"),
        ShellIntegrationKind.ImageEffect => IsAssociationEnabled("sxie"),
        ShellIntegrationKind.Chrome or ShellIntegrationKind.Firefox => BrowserManifests(kind).Any(IsOwnedManifest),
        _ => false
    };

    public bool SetIntegrationEnabled(ShellIntegrationKind kind, bool enable) => Apply(() =>
    {
        switch (kind)
        {
            case ShellIntegrationKind.ImageEditor: SetContextAction("edit", "Edit with XerahS", "-ImageEditor", true, enable); break;
            case ShellIntegrationKind.CustomUploader: SetAssociation("sxcu", "application/x-sharex-custom-uploader", "ShareX custom uploader", "-CustomUploader", enable); break;
            case ShellIntegrationKind.ImageEffect: SetAssociation("sxie", "application/x-sharex-image-effect", "ShareX image effect", "-ImageEffect", enable); break;
            case ShellIntegrationKind.Chrome:
            case ShellIntegrationKind.Firefox: SetBrowserIntegration(kind, enable); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    });

    /// <summary>Refresh installed entries after an upgrade or an AppImage move, without enabling new integrations.</summary>
    public void RefreshRegisteredEntries()
    {
        if (IsContextMenuIntegrationEnabled()) SetContextMenuIntegration(true);
        if (IsSendToIntegrationEnabled()) SetSendToIntegration(true);
        if (IsPluginExtensionRegistered()) Apply(() => SetAssociation("xsdp", "application/x-xerahs-plugin", "XerahS plugin package", "", true, updateAssociation: false));
        if (IsIntegrationEnabled(ShellIntegrationKind.ImageEditor)) SetIntegrationEnabled(ShellIntegrationKind.ImageEditor, true);
        if (IsIntegrationEnabled(ShellIntegrationKind.CustomUploader)) Apply(() => SetAssociation("sxcu", "application/x-sharex-custom-uploader", "ShareX custom uploader", "-CustomUploader", true, updateAssociation: false));
        if (IsIntegrationEnabled(ShellIntegrationKind.ImageEffect)) Apply(() => SetAssociation("sxie", "application/x-sharex-image-effect", "ShareX image effect", "-ImageEffect", true, updateAssociation: false));
        foreach (ShellIntegrationKind kind in new[] { ShellIntegrationKind.Chrome, ShellIntegrationKind.Firefox })
            if (IsIntegrationEnabled(kind)) Apply(() => SetBrowserIntegration(kind, true, refreshOnly: true));
    }

    private bool Apply(Action action)
    {
        try
        {
            if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(_executable)) return false;
            action();
            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Linux shell integration");
            return false;
        }
    }

    private IEnumerable<string> ScriptPaths(string name) => new[] { "nautilus", "nemo", "caja" }
        .Select(manager => Path.Combine(_xdg.DataHome, manager, "scripts", name));

    private void SetContextAction(string id, string name, string flag, bool imagesOnly, bool enable)
    {
        // Validate and preserve existing user actions before changing the other file managers.
        UpdateThunarAction(id, name, flag, imagesOnly, enable);
        foreach (string path in ScriptPaths(name))
        {
            if (enable)
            {
                // Nautilus may provide the selection through its environment instead of argv.
                string script = $"#!/usr/bin/env bash\n# {Marker}\n" +
                    "if [ \"$#\" -eq 0 ]; then\n" +
                    "  selected=\"${NAUTILUS_SCRIPT_SELECTED_FILE_PATHS:-${NEMO_SCRIPT_SELECTED_FILE_PATHS:-${CAJA_SCRIPT_SELECTED_FILE_PATHS:-}}}\"\n" +
                    "  while IFS= read -r path; do [ -z \"$path\" ] || set -- \"$@\" \"$path\"; done <<< \"$selected\"\nfi\n" +
                    $"[ \"$#\" -gt 0 ] && exec {QuoteShell(_executable)} {flag} \"$@\"\n";
                Write(path, script, true);
            }
            else Delete(path);
        }
        string kde = Path.Combine(ServiceMenus, $"xerahs-{id}.desktop");
        if (enable) Write(kde, BuildKdeEntry(id, name, flag, imagesOnly), true);
        else Delete(kde);
    }

    private string BuildKdeEntry(string id, string name, string flag, bool imagesOnly, string extra = "") =>
        $"[Desktop Entry]\nType=Service\nMimeType={(imagesOnly ? "image/*;" : "all/all;")}\nActions={id};\nX-KDE-Priority=TopLevel\n{extra}{Marker}\n{AppContracts.LinuxIntegration.SendToMarkerKey}=true\n\n[Desktop Action {id}]\nName={name}\nIcon=xerahs\nExec={DesktopCommand} {flag} %F\n";

    private void UpdateThunarAction(string id, string name, string flag, bool imagesOnly, bool enable)
    {
        if (!enable && !File.Exists(ThunarActions)) return;
        XDocument document = File.Exists(ThunarActions) ? XDocument.Load(ThunarActions) : new XDocument(new XElement("actions"));
        XElement root = document.Root ?? throw new InvalidDataException("Thunar custom actions have no root element.");
        if (root.Name != "actions") throw new InvalidDataException("Invalid Thunar custom actions file.");
        string uniqueId = "xerahs-" + id;
        root.Elements("action").Where(a => (string?)a.Element("unique-id") == uniqueId).Remove();
        if (enable)
        {
            var action = new XElement("action", new XElement("icon", "xerahs"), new XElement("name", name),
                new XElement("unique-id", uniqueId), new XElement("command", $"{QuoteShell(_executable).Replace("%", "%%")} {flag} %F"),
                new XElement("description", name), new XElement("patterns", "*"), new XElement("startup-notify"), new XElement("image-files"));
            if (!imagesOnly)
                foreach (string type in new[] { "directories", "audio-files", "other-files", "text-files", "video-files" }) action.Add(new XElement(type));
            root.Add(action);
        }
        Write(ThunarActions, document.ToString());
    }

    private bool IsAssociationEnabled(string extension) => File.Exists(Path.Combine(Applications, $"xerahs-{extension}.desktop")) &&
        File.Exists(Path.Combine(_xdg.DataHome, "mime", "packages", $"xerahs-{extension}.xml"));

    private void SetAssociation(string extension, string mime, string description, string flag, bool enable, bool updateAssociation = true)
    {
        string desktopName = $"xerahs-{extension}.desktop";
        string desktop = Path.Combine(Applications, desktopName);
        string xml = Path.Combine(_xdg.DataHome, "mime", "packages", $"xerahs-{extension}.xml");
        bool changed;
        if (enable)
        {
            changed = Write(desktop, $"[Desktop Entry]\nType=Application\nName={description}\nExec={DesktopCommand} {flag} %F\nIcon=xerahs\nNoDisplay=true\nTerminal=false\nMimeType={mime};\n{Marker}\n")
                | Write(xml, $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\"><mime-type type=\"{mime}\"><comment>{description}</comment><glob pattern=\"*.{extension}\"/></mime-type></mime-info>");
        }
        else changed = Delete(desktop) | Delete(xml);
        if (updateAssociation) UpdateMimeApps(mime, desktopName, enable);
        // The startup refresh usually finds the entries unchanged; the caches are rebuilt only after a change.
        if (_updateDatabases && changed)
        {
            RunCacheUpdate("update-mime-database", Path.Combine(_xdg.DataHome, "mime"));
            RunCacheUpdate("update-desktop-database", Applications);
        }
    }

    private void UpdateMimeApps(string mime, string desktop, bool enable)
    {
        string path = Path.Combine(_xdg.ConfigHome, "mimeapps.list");
        if (!enable && !File.Exists(path)) return;
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        int section = lines.FindIndex(l => l.Trim() == "[Default Applications]");
        if (section < 0)
        {
            if (!enable) return;
            lines.Add("[Default Applications]");
            section = lines.Count - 1;
        }
        int end = section + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('[')) end++;
        int index = lines.FindIndex(section + 1, end - section - 1, l => l.StartsWith(mime + "=", StringComparison.Ordinal));
        var entries = index < 0 ? new List<string>() : lines[index][(mime.Length + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        entries.RemoveAll(e => e == desktop);
        if (enable) entries.Insert(0, desktop);
        string line = mime + "=" + string.Join(';', entries) + ";";
        if (index >= 0) { if (entries.Count == 0) lines.RemoveAt(index); else lines[index] = line; }
        else if (enable) lines.Insert(end, line);
        Write(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private IEnumerable<string> BrowserManifests(ShellIntegrationKind kind) => kind == ShellIntegrationKind.Firefox
        ? new[] { Path.Combine(_xdg.HomeDirectory, ".mozilla", "native-messaging-hosts", "ShareX.json") }
        : new[] { "google-chrome", "chromium", "BraveSoftware/Brave-Browser", "microsoft-edge" }
            .Select(browser => Path.Combine(_xdg.ConfigHome, browser, "NativeMessagingHosts", "com.getsharex.sharex.json"));

    private void SetBrowserIntegration(ShellIntegrationKind kind, bool enable, bool refreshOnly = false)
    {
        if (enable)
        {
            Write(NativeHost, $"#!/bin/sh\n# {Marker}\nexec {QuoteShell(_executable)} --native-messaging-host \"$@\"\n", true);
            var manifest = new Dictionary<string, object>
            {
                ["name"] = kind == ShellIntegrationKind.Firefox ? "ShareX" : "com.getsharex.sharex",
                ["description"] = "XerahS browser integration", ["path"] = NativeHost, ["type"] = "stdio"
            };
            if (kind == ShellIntegrationKind.Firefox) manifest["allowed_extensions"] = new[] { "firefox@getsharex.com" };
            else manifest["allowed_origins"] = new[] { "chrome-extension://nlkoigbdolhchiicbonbihbphgamnaoc/" };
            foreach (string path in BrowserManifests(kind))
                if (!refreshOnly || IsOwnedManifest(path)) Write(path, JsonSerializer.Serialize(manifest));
        }
        else
        {
            foreach (string path in BrowserManifests(kind)) if (IsOwnedManifest(path)) File.Delete(path);
            if (!BrowserManifests(ShellIntegrationKind.Chrome).Concat(BrowserManifests(ShellIntegrationKind.Firefox)).Any(IsOwnedManifest)) Delete(NativeHost);
        }
    }

    private bool IsOwnedManifest(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("path", out var entry) && entry.GetString() == NativeHost;
        }
        catch { return false; }
    }

    private static bool IsOwnedSendTo(string path)
    {
        try { return File.Exists(path) && File.ReadAllText(path).Contains(AppContracts.LinuxIntegration.SendToMarkerKey + "=true", StringComparison.Ordinal); }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Read Send To entry"); return false; }
    }

    internal static string QuoteShell(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
    internal static string EscapeDesktopArgument(string value) => value.Replace("\\", "\\\\\\\\").Replace("\"", "\\\\\"")
        .Replace("`", "\\\\`").Replace("$", "\\\\$").Replace("%", "%%").Replace("\n", "\\n").Replace("\r", "\\r");

    /// <summary>Writes the file unless it already has this content; returns whether it changed.</summary>
    private static bool Write(string path, string content, bool executable = false)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        bool unchanged = File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        if (!unchanged)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        if (executable && OperatingSystem.IsLinux())
        {
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            if (File.GetUnixFileMode(path) != mode) File.SetUnixFileMode(path, mode);
        }
        return !unchanged;
    }
    private static bool Delete(string path)
    {
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
    private static void RunCacheUpdate(string command, string folder)
    {
        try
        {
            var info = new ProcessStartInfo(command) { UseShellExecute = false };
            info.ArgumentList.Add(folder);
            using var process = Process.Start(info);
            process?.WaitForExit();
        }
        catch (Exception ex) { DebugHelper.WriteException(ex, "Refresh desktop integration cache"); }
    }
}
