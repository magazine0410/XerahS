using NUnit.Framework;
using XerahS.Platform.Linux.Services;

namespace XerahS.Tests.Platform;

public class LinuxStartupServiceTests
{
    [Test]
    public void BuildDesktopEntry_EscapesQuotedExecutablePathCharacters()
    {
        const string executablePath = "/opt/XerahS \"nightly\"/xerahs\\launcher";

        string desktopEntry = LinuxStartupService.BuildDesktopEntry(executablePath);

        Assert.That(desktopEntry, Does.Contain("Exec=\"/opt/XerahS \\\"nightly\\\"/xerahs\\\\launcher\" -silent"));
    }

    [Test]
    public void EscapeQuotedDesktopEntryArgument_EscapesBackslashesAndDoubleQuotes()
    {
        const string value = "/tmp/XerahS \"beta\"/app\\binary";

        string escaped = LinuxStartupService.EscapeQuotedDesktopEntryArgument(value);

        Assert.That(escaped, Is.EqualTo("/tmp/XerahS \\\"beta\\\"/app\\\\binary"));
    }

    [Test]
    public void BuildDesktopEntry_StartsXerahSInTheTrayLikeShareX()
    {
        Assert.That(LinuxStartupService.BuildDesktopEntry("/usr/bin/xerahs"), Does.Contain("Exec=\"/usr/bin/xerahs\" -silent\n"));
    }

    [Test]
    public void ResolveExecutablePath_UsesTheAppImageFileInsteadOfItsTemporaryMount()
    {
        string appImage = Path.GetTempFileName();
        try
        {
            Assert.That(LinuxStartupService.ResolveExecutablePath(appImage, "/tmp/.mount_XerahSab12/usr/bin/XerahS"), Is.EqualTo(appImage));
            Assert.That(LinuxStartupService.ResolveExecutablePath(null, "/usr/lib/xerahs/XerahS"), Is.EqualTo("/usr/lib/xerahs/XerahS"));
            Assert.That(LinuxStartupService.ResolveExecutablePath(appImage + ".missing", "/usr/lib/xerahs/XerahS"), Is.EqualTo("/usr/lib/xerahs/XerahS"));
        }
        finally
        {
            File.Delete(appImage);
        }
    }

    [Test]
    public void RefreshEntry_RewritesAnOutdatedEntry_AndNeverCreatesOne()
    {
        string folder = Path.Combine(Path.GetTempPath(), "xerahs-autostart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new LinuxStartupService(folder, "/home/user/XerahS.AppImage");
            string entryPath = Path.Combine(folder, $"{XerahS.Common.AppResources.AppName}.desktop");

            Assert.That(service.RefreshEntry(), Is.False);
            Assert.That(File.Exists(entryPath), Is.False);

            File.WriteAllText(entryPath, LinuxStartupService.BuildDesktopEntry("/tmp/.mount_XerahSab12/usr/bin/XerahS").Replace(" -silent", ""));
            Assert.That(service.RefreshEntry(), Is.True);
            Assert.That(File.ReadAllText(entryPath), Is.EqualTo(LinuxStartupService.BuildDesktopEntry("/home/user/XerahS.AppImage")));

            Assert.That(service.RefreshEntry(), Is.False);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
