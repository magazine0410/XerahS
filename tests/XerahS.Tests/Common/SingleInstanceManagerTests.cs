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

using System.IO.Pipes;
using System.Net.Sockets;
using NUnit.Framework;
using XerahS.App;
using XerahS.Common;

namespace XerahS.Tests.Common;

[TestFixture, NonParallelizable]
public class SingleInstanceManagerTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);
    private readonly List<string> _paths = [];

    [TearDown]
    public void TearDown()
    {
        foreach (string path in _paths)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        _paths.Clear();
    }

    [Test]
    public void GetPlatformPipeName_DefaultContract_IsWithinUnixDomainSocketLimit()
    {
        string pipeName = SingleInstanceManager.GetPlatformPipeName(AppContracts.SingleInstance.PipeName);

        Assert.That(pipeName, Is.Not.Null.And.Not.Empty);

        if (OperatingSystem.IsWindows())
        {
            Assert.That(pipeName, Is.EqualTo(AppContracts.SingleInstance.PipeName));
            return;
        }

        Assert.That(Path.IsPathRooted(pipeName), Is.True);
        Assert.That(pipeName.Length, Is.LessThanOrEqualTo(SingleInstanceManager.UnixDomainSocketPathMaxLength));
        Assert.That(
            pipeName.Length,
            Is.LessThanOrEqualTo(104),
            "macOS AF_UNIX sun_path is 104 bytes; a longer path throws ArgumentOutOfRangeException at startup.");
    }

    [Test]
    public void GetPlatformPipeName_PersonalFolderSuffix_StaysWithinUnixDomainSocketLimit()
    {
        string logicalName = $"{AppContracts.SingleInstance.PipeName}-0123456789ABCDEF";
        string pipeName = SingleInstanceManager.GetPlatformPipeName(logicalName);

        if (OperatingSystem.IsWindows())
        {
            Assert.That(pipeName, Is.EqualTo(logicalName));
            return;
        }

        Assert.That(pipeName.Length, Is.LessThanOrEqualTo(SingleInstanceManager.UnixDomainSocketPathMaxLength));
        Assert.That(pipeName, Is.Not.EqualTo(SingleInstanceManager.GetPlatformPipeName(AppContracts.SingleInstance.PipeName)));
    }

    [Test]
    public void GetPlatformPipeName_IsStableForTheSameLogicalName()
    {
        string logicalName = AppContracts.SingleInstance.PipeName;
        Assert.That(
            SingleInstanceManager.GetPlatformPipeName(logicalName),
            Is.EqualTo(SingleInstanceManager.GetPlatformPipeName(logicalName)));
    }

    [Test]
    public void GetPlatformPipeName_AllowsNamedPipeServerStreamOnUnix()
    {
        string logicalName = $"{AppContracts.SingleInstance.PipeName}-{Guid.NewGuid():N}";
        string pipeName = SingleInstanceManager.GetPlatformPipeName(logicalName);

        if (OperatingSystem.IsWindows())
        {
            Assert.That(pipeName, Is.EqualTo(logicalName));
            return;
        }

        Assert.That(pipeName.Length, Is.LessThanOrEqualTo(SingleInstanceManager.UnixDomainSocketPathMaxLength));

        try
        {
            using NamedPipeServerStream server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Assert.That(server.IsConnected, Is.False);
        }
        finally
        {
            try
            {
                if (File.Exists(pipeName))
                {
                    File.Delete(pipeName);
                }
            }
            catch
            {
            }
        }
    }

    [Test]
    public void GetLockFilePath_OnUnix_SitsNextToTheSocketWhateverTmpDir()
    {
        Assume.That(OperatingSystem.IsWindows(), Is.False, "Unix domain sockets only.");

        string socketPath = SingleInstanceManager.GetPlatformPipeName(AppContracts.SingleInstance.PipeName);
        string lockPath = SingleInstanceManager.GetLockFilePath(AppContracts.SingleInstance.MutexName, socketPath);

        string? previousTmpDir = Environment.GetEnvironmentVariable("TMPDIR");
        string privateTmpDir = Directory.CreateTempSubdirectory("xerahs-private-tmpdir-").FullName;
        string lockPathWithPrivateTmpDir;
        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", privateTmpDir);
            lockPathWithPrivateTmpDir = SingleInstanceManager.GetLockFilePath(
                AppContracts.SingleInstance.MutexName,
                SingleInstanceManager.GetPlatformPipeName(AppContracts.SingleInstance.PipeName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", previousTmpDir);
            Directory.Delete(privateTmpDir, recursive: true);
        }

        Assert.That(Path.GetDirectoryName(lockPath), Is.EqualTo(Path.GetDirectoryName(socketPath)));
        Assert.That(Path.GetFileNameWithoutExtension(lockPath), Is.EqualTo(Path.GetFileNameWithoutExtension(socketPath)));
        Assert.That(lockPath, Is.Not.EqualTo(socketPath));
        Assert.That(lockPathWithPrivateTmpDir, Is.EqualTo(lockPath));
    }

    [Test]
    public void SecondProcessWithAnotherTmpDir_FindsTheLockAndKeepsTheSocket()
    {
        Assume.That(OperatingSystem.IsWindows(), Is.False, "Unix domain sockets only.");

        (string mutexName, string pipePath) = NewIdentity();
        using SingleInstanceManager first = new SingleInstanceManager(mutexName, pipePath, ["-silent"]);
        Task<string[]> received = ReceiveNextArguments(first);
        WaitForSocket(pipePath);

        string? previousTmpDir = Environment.GetEnvironmentVariable("TMPDIR");
        string privateTmpDir = Directory.CreateTempSubdirectory("xerahs-private-tmpdir-").FullName;
        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", privateTmpDir);
            using SingleInstanceManager second = new SingleInstanceManager(mutexName, pipePath, ["/tmp/picture.png"]);

            Assert.That(second.IsFirstInstance, Is.False);
            Assert.That(second.RedirectError, Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", previousTmpDir);
            Directory.Delete(privateTmpDir, recursive: true);
        }

        Assert.That(received.Wait(DeliveryTimeout), Is.True, "The first instance did not receive the arguments.");
        Assert.That(received.Result, Is.EqualTo(new[] { "/tmp/picture.png" }));
        Assert.That(File.Exists(pipePath), Is.True);
        Assert.That(SingleInstanceManager.IsUnixSocketListening(pipePath), Is.True);
    }

    [Test]
    public void FirstInstance_DoesNotDeleteSocketWithLiveListener()
    {
        Assume.That(OperatingSystem.IsWindows(), Is.False, "Unix domain sockets only.");

        (string runningMutexName, string pipePath) = NewIdentity();
        using SingleInstanceManager running = new SingleInstanceManager(runningMutexName, pipePath, ["-silent"]);
        Task<string[]> received = ReceiveNextArguments(running);
        WaitForSocket(pipePath);

        Assert.That(SingleInstanceManager.IsUnixSocketListening(pipePath), Is.True);
        Assert.That(SingleInstanceManager.TryRemoveStaleUnixSocket(pipePath), Is.False);
        Assert.That(File.Exists(pipePath), Is.True);

        // Another lock name stands for a running instance whose lock this process cannot see.
        (string otherMutexName, _) = NewIdentity();
        using SingleInstanceManager launched = new SingleInstanceManager(otherMutexName, pipePath, ["/tmp/picture.png"]);

        Assert.That(launched.IsFirstInstance, Is.False);
        Assert.That(launched.RedirectError, Is.Null);
        Assert.That(received.Wait(DeliveryTimeout), Is.True, "The running instance did not receive the arguments.");
        Assert.That(received.Result, Is.EqualTo(new[] { "/tmp/picture.png" }));
        Assert.That(File.Exists(pipePath), Is.True);
        Assert.That(SingleInstanceManager.IsUnixSocketListening(pipePath), Is.True);
    }

    [Test]
    public void FirstInstance_RemovesStaleSocketWithoutListener()
    {
        Assume.That(OperatingSystem.IsWindows(), Is.False, "Unix domain sockets only.");

        (string mutexName, string pipePath) = NewIdentity();
        CreateStaleSocket(pipePath);
        Assert.That(SingleInstanceManager.IsUnixSocketListening(pipePath), Is.False);

        Assert.That(SingleInstanceManager.TryRemoveStaleUnixSocket(pipePath), Is.True);
        Assert.That(File.Exists(pipePath), Is.False);

        CreateStaleSocket(pipePath);
        using SingleInstanceManager first = new SingleInstanceManager(mutexName, pipePath, ["-silent"]);
        Task<string[]> received = ReceiveNextArguments(first);

        Assert.That(first.IsFirstInstance, Is.True);
        Assert.That(
            SingleInstanceManager.TrySendToRunningInstance(pipePath, ["/tmp/picture.png"], timeoutMs: 5000),
            Is.True,
            "The stale socket was not replaced by the first instance's listener.");
        Assert.That(received.Wait(DeliveryTimeout), Is.True);
        Assert.That(received.Result, Is.EqualTo(new[] { "/tmp/picture.png" }));
    }

    [Test]
    public void SecondInstance_WhenHandOffTimesOut_LogsAndNotifies()
    {
        Assume.That(OperatingSystem.IsWindows(), Is.False, "Unix domain sockets only.");

        (string mutexName, string pipePath) = NewIdentity();
        string lockPath = SingleInstanceManager.GetLockFilePath(mutexName, pipePath);
        _paths.Add(lockPath);

        // The lock is held, but no instance listens on the socket.
        using FileStream heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string[] arguments = ["/tmp/picture.png"];
        using SingleInstanceManager second = new SingleInstanceManager(mutexName, pipePath, true, arguments, connectTimeout: 300);

        Assert.That(second.IsFirstInstance, Is.False);
        Assert.That(second.RedirectError, Is.InstanceOf<TimeoutException>());

        string errorLog = File.ReadAllText(PathsManager.GetErrorLogFilePath());
        Assert.That(errorLog, Does.Contain($"Could not pass the arguments to the running instance on '{pipePath}'"));

        List<(string Title, string Message)> notifications = [];
        HandOffFailureNotice.Report(arguments, (title, message) => notifications.Add((title, message)));

        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Title, Is.EqualTo(HandOffFailureNotice.Title));
        Assert.That(notifications[0].Message, Does.Contain("/tmp/picture.png"));
    }

    [Test]
    public void HandOffFailureNotice_ListsAtMostThreeArguments()
    {
        string message = HandOffFailureNotice.BuildMessage(["/a.png", "/b.png", "/c.png", "/d.png", "/e.png"]);

        Assert.That(message, Does.Contain("/a.png, /b.png, /c.png and 2 more"));
        Assert.That(message, Does.Not.Contain("/d.png"));
        Assert.That(HandOffFailureNotice.BuildMessage([]), Does.Contain("did not respond"));
    }

    private (string MutexName, string PipePath) NewIdentity()
    {
        string id = Guid.NewGuid().ToString("N")[..12];
        string pipePath = $"/tmp/xerahs-test-{id}.sock";
        _paths.Add(pipePath);
        _paths.Add(SingleInstanceManager.GetLockFilePath($"XerahS-Test-{id}", pipePath));
        return ($"XerahS-Test-{id}", pipePath);
    }

    private static Task<string[]> ReceiveNextArguments(SingleInstanceManager manager)
    {
        TaskCompletionSource<string[]> received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ArgumentsReceived += arguments => received.TrySetResult(arguments);
        return received.Task;
    }

    private static void WaitForSocket(string path)
    {
        DateTime deadline = DateTime.UtcNow + DeliveryTimeout;
        while (!SingleInstanceManager.IsUnixSocketListening(path))
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), $"No listener appeared on '{path}'.");
            Thread.Sleep(20);
        }
    }

    /// <summary>A socket file whose process is gone: bound, then closed without unlinking.</summary>
    private static void CreateStaleSocket(string path)
    {
        using Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        Assert.That(File.Exists(path), Is.True);
    }
}
