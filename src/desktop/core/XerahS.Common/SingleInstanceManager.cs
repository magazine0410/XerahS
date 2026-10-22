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
using System.Security.Cryptography;
using System.Text;

namespace XerahS.Common
{
    public class SingleInstanceManager : IDisposable
    {
        public event Action<string[]>? ArgumentsReceived;

        public string MutexName { get; private set; }
        public string PipeName { get; private set; }
        public bool IsSingleInstance { get; private set; }
        public bool IsFirstInstance { get; private set; }

        /// <summary>
        /// Set when this instance is not the first one and could not pass its arguments to the first instance.
        /// </summary>
        public Exception? RedirectError { get; private set; }

        private const int MaxArgumentsLength = 100;
        private const int DefaultConnectTimeout = 5000;
        private const int SocketProbeTimeout = 1000;

        private readonly int connectTimeout;

        /// <summary>
        /// Darwin <c>sockaddr_un.sun_path</c> is 104 bytes. Linux allows 108.
        /// Stay at the macOS limit so one pipe name works on both.
        /// </summary>
        public const int UnixDomainSocketPathMaxLength = 104;

        private Mutex? mutex;
        private bool ownsMutex;
        private FileStream? lockFileStream;
        private string? lockFilePath;
        private CancellationTokenSource? cts;

        private enum LockAcquireResult
        {
            Acquired,
            AlreadyHeldByAnotherInstance,
            Unavailable
        }

        public SingleInstanceManager(string mutexName, string pipeName, string[] args) : this(mutexName, pipeName, true, args)
        {
        }

        public SingleInstanceManager(string mutexName, string pipeName, bool isSingleInstance, string[] args)
            : this(mutexName, pipeName, isSingleInstance, args, DefaultConnectTimeout)
        {
        }

        internal SingleInstanceManager(string mutexName, string pipeName, bool isSingleInstance, string[] args, int connectTimeout)
        {
            MutexName = mutexName;
            PipeName = GetPlatformPipeName(pipeName);
            IsSingleInstance = isSingleInstance;
            this.connectTimeout = connectTimeout;

            IsFirstInstance = !IsSingleInstance || TryAcquirePrimaryLock();

            if (IsSingleInstance && IsFirstInstance && IsUnixSocketPath(PipeName) && IsUnixSocketListening(PipeName))
            {
                // The lock was free, yet an instance answers on the socket, for example one started by a version
                // that kept its lock elsewhere. Creating a listener would unlink that instance's socket.
                DebugHelper.WriteLine($"SingleInstanceManager: An instance is already listening on '{PipeName}'; passing the arguments to it.");
                IsFirstInstance = false;
            }

            if (IsSingleInstance)
            {
                if (IsFirstInstance)
                {
                    TryRemoveStaleUnixSocket(PipeName);
                    cts = new CancellationTokenSource();

                    Task.Run(ListenForConnectionsAsync, cts.Token);
                }
                else
                {
                    RedirectArgumentsToFirstInstance(args);
                    ReleaseInstanceLocks();
                }
            }
        }

        /// <summary>
        /// Maps a logical pipe name to a platform-safe name.
        /// On Unix, <see cref="NamedPipeServerStream"/> stores the socket at
        /// <c>{TMPDIR}/CoreFxPipe_{pipeName}</c> unless <paramref name="logicalPipeName"/>
        /// is already rooted. macOS TMPDIR is already ~50 characters, so a GUID-sized
        /// name overflows the 104-byte AF_UNIX limit. A short rooted path under /tmp
        /// avoids that.
        /// </summary>
        public static string GetPlatformPipeName(string logicalPipeName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logicalPipeName);

            if (OperatingSystem.IsWindows())
            {
                return logicalPipeName;
            }

            if (Path.IsPathRooted(logicalPipeName) && IsUnixDomainSocketPathLengthValid(logicalPipeName))
            {
                return logicalPipeName;
            }

            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logicalPipeName)))
                .Substring(0, 8)
                .ToLowerInvariant();
            string path = Path.Combine("/tmp", $"xerahs-{hash}.sock");

            if (!IsUnixDomainSocketPathLengthValid(path))
            {
                path = "/tmp/xs-" + hash + ".s";
            }

            return path;
        }

        internal static bool IsUnixDomainSocketPathLengthValid(string path)
        {
            return path.Length >= 1 && path.Length <= UnixDomainSocketPathMaxLength;
        }

        private static bool IsUnixSocketPath(string pipeName)
        {
            return !OperatingSystem.IsWindows() && Path.IsPathRooted(pipeName);
        }

        /// <summary>
        /// True when something accepts connections on the Unix domain socket at <paramref name="path"/>.
        /// A missing file or a refused connection means nothing is listening. Any other failure counts as
        /// listening, so an uncertain socket is never deleted.
        /// </summary>
        internal static bool IsUnixSocketListening(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                using CancellationTokenSource timeout = new CancellationTokenSource(SocketProbeTimeout);
                socket.ConnectAsync(new UnixDomainSocketEndPoint(path), timeout.Token).AsTask().GetAwaiter().GetResult();
                return true;
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionRefused or SocketError.AddressNotAvailable)
            {
                return false;
            }
            catch (Exception e)
            {
                DebugHelper.WriteLine($"SingleInstanceManager: Could not tell whether '{path}' has a listener: {e.Message}");
                return true;
            }
        }

        /// <summary>
        /// Deletes a socket file left behind by an instance that exited without removing it.
        /// A socket that still has a listener belongs to a running instance and is kept.
        /// Returns true when no socket file remains at <paramref name="path"/>.
        /// </summary>
        internal static bool TryRemoveStaleUnixSocket(string path)
        {
            if (!IsUnixSocketPath(path))
            {
                return true;
            }

            try
            {
                if (!File.Exists(path))
                {
                    return true;
                }

                if (IsUnixSocketListening(path))
                {
                    DebugHelper.WriteLine($"SingleInstanceManager: Socket '{path}' has a listener; not removing it.");
                    return false;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                DebugHelper.WriteLine($"SingleInstanceManager: Could not remove stale socket '{path}': {e.Message}");
                return false;
            }
        }

        protected virtual void OnArgumentsReceived(string[] arguments)
        {
            ArgumentsReceived?.Invoke(arguments);
        }

        private async Task ListenForConnectionsAsync()
        {
            if (cts == null) return;

            NamedPipeServerStream? nextNamedPipeServer = null;

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    bool namedPipeServerCreated = false;
                    NamedPipeServerStream? namedPipeServer = null;

                    try
                    {
                        namedPipeServer = nextNamedPipeServer ?? CreateNamedPipeServer();
                        nextNamedPipeServer = null;
                        namedPipeServerCreated = true;

                        await namedPipeServer.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);

                        // On Unix, server streams with the same name share one listening socket, which is unlinked
                        // when the last of them is disposed. Opening the next one before this one is disposed keeps
                        // the socket file in place, so a launch between two connections still finds this instance.
                        nextNamedPipeServer = TryCreateNamedPipeServer();

                        using (BinaryReader reader = new BinaryReader(namedPipeServer, Encoding.UTF8))
                        {
                            int length = reader.ReadInt32();

                            if (length < 0 || length > MaxArgumentsLength)
                            {
                                throw new Exception("Invalid length: " + length);
                            }

                            string[] args = new string[length];

                            for (int i = 0; i < length; i++)
                            {
                                args[i] = reader.ReadString();
                            }

                            OnArgumentsReceived(args);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (EndOfStreamException)
                    {
                        // IsUnixSocketListening in another process connects and closes without sending anything.
                        DebugHelper.WriteLine("SingleInstanceManager: A connection closed without sending arguments.");
                    }
                    catch (Exception e)
                    {
                        DebugHelper.WriteException(e);

                        if (!namedPipeServerCreated)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        namedPipeServer?.Dispose();
                    }
                }
            }
            finally
            {
                nextNamedPipeServer?.Dispose();
            }
        }

        private NamedPipeServerStream CreateNamedPipeServer()
        {
            // Removed Windows specific PipeSecurity for cross-platform compatibility
            // On Windows we might want to AddAccessRule but for now we simplify.

            // Two instances: the one reading a connection and the next one waiting for a connection.
            return new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                2,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }

        private NamedPipeServerStream? TryCreateNamedPipeServer()
        {
            try
            {
                return CreateNamedPipeServer();
            }
            catch (Exception e)
            {
                DebugHelper.WriteLine($"SingleInstanceManager: Could not open the next pipe server on '{PipeName}': {e.Message}");
                return null;
            }
        }

        private bool TryAcquirePrimaryLock()
        {
            var mutexResult = TryAcquireNamedMutex();
            var fileResult = TryAcquireLockFile();

            if (mutexResult == LockAcquireResult.AlreadyHeldByAnotherInstance ||
                fileResult == LockAcquireResult.AlreadyHeldByAnotherInstance)
            {
                DebugHelper.WriteLine("SingleInstanceManager: Existing instance detected (mutex and/or lock file held).");
                return false;
            }

            if (mutexResult == LockAcquireResult.Acquired && fileResult == LockAcquireResult.Acquired)
            {
                DebugHelper.WriteLine($"SingleInstanceManager: Acquired mutex '{MutexName}' and lock file '{lockFilePath}'.");
                return true;
            }

            if (mutexResult == LockAcquireResult.Acquired && fileResult == LockAcquireResult.Unavailable)
            {
                DebugHelper.WriteLine($"SingleInstanceManager: Lock file unavailable; proceeding with named mutex '{MutexName}' only.");
                return true;
            }

            if (mutexResult == LockAcquireResult.Unavailable && fileResult == LockAcquireResult.Acquired)
            {
                DebugHelper.WriteLine($"SingleInstanceManager: Named mutex unavailable; proceeding with lock file '{lockFilePath}' only.");
                return true;
            }

            DebugHelper.WriteLine("SingleInstanceManager: Could not acquire single-instance lock (mutex + lock file unavailable). Allowing startup as fallback.");
            return true;
        }

        private LockAcquireResult TryAcquireNamedMutex()
        {
            try
            {
                mutex = new Mutex(false, MutexName);

                try
                {
                    ownsMutex = mutex.WaitOne(0, false);
                    if (ownsMutex)
                    {
                        return LockAcquireResult.Acquired;
                    }
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                    return LockAcquireResult.Acquired;
                }

                mutex.Dispose();
                mutex = null;
                return LockAcquireResult.AlreadyHeldByAnotherInstance;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, $"SingleInstanceManager: Failed to acquire named mutex '{MutexName}'.");
                mutex = null;
                ownsMutex = false;
                return LockAcquireResult.Unavailable;
            }
        }

        private LockAcquireResult TryAcquireLockFile()
        {
            try
            {
                lockFilePath = GetLockFilePath();
                lockFileStream = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                string marker = $"{Environment.ProcessId}{Environment.NewLine}";
                lockFileStream.SetLength(0);
                byte[] bytes = Encoding.UTF8.GetBytes(marker);
                lockFileStream.Write(bytes, 0, bytes.Length);
                lockFileStream.Flush(true);
                lockFileStream.Position = 0;

                return LockAcquireResult.Acquired;
            }
            catch (IOException ioEx)
            {
                DebugHelper.WriteLine($"SingleInstanceManager: Lock file appears held by another instance: {ioEx.Message}");
                lockFileStream = null;
                return LockAcquireResult.AlreadyHeldByAnotherInstance;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, "SingleInstanceManager: Failed to acquire lock file fallback.");
                lockFileStream = null;
                return LockAcquireResult.Unavailable;
            }
        }

        private string GetLockFilePath()
        {
            return GetLockFilePath(MutexName, PipeName);
        }

        /// <summary>
        /// On Unix the lock file sits next to the socket, both at fixed paths. The temporary folder follows
        /// TMPDIR, so a lock there would be missed by a process started with another TMPDIR, which would then
        /// take over the running instance's socket. The named mutex also follows TMPDIR, so the lock file is
        /// the lock that all processes of one identity share.
        /// </summary>
        internal static string GetLockFilePath(string mutexName, string platformPipeName)
        {
            if (IsUnixSocketPath(platformPipeName))
            {
                return Path.ChangeExtension(platformPipeName, ".lock");
            }

            string tempPath = Path.GetTempPath();
            var sb = new StringBuilder(mutexName.Length);

            foreach (char c in mutexName)
            {
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            }

            return Path.Combine(tempPath, $"{sb}.instance.lock");
        }

        /// <summary>
        /// Sends arguments to an already running instance without starting one.
        /// Returns false when no instance is listening.
        /// </summary>
        public static bool TrySendToRunningInstance(string logicalPipeName, string[] args, int timeoutMs = 1000)
        {
            string pipeName = GetPlatformPipeName(logicalPipeName);
            if (!OperatingSystem.IsWindows() && Path.IsPathRooted(pipeName) && !File.Exists(pipeName))
            {
                return false;
            }

            try
            {
                using var namedPipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                namedPipeClient.Connect(timeoutMs);

                using var writer = new BinaryWriter(namedPipeClient, Encoding.UTF8);
                writer.Write(args.Length);
                foreach (string argument in args)
                {
                    writer.Write(argument);
                }

                return true;
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                DebugHelper.WriteLine($"SingleInstanceManager: No running instance reachable on '{pipeName}': {e.Message}");
                return false;
            }
        }

        private void RedirectArgumentsToFirstInstance(string[] args)
        {
            try
            {
                using (NamedPipeClientStream namedPipeClient = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.CurrentUserOnly))
                {
                    namedPipeClient.Connect(connectTimeout);

                    using (BinaryWriter writer = new BinaryWriter(namedPipeClient, Encoding.UTF8))
                    {
                        writer.Write(args.Length);

                        foreach (string argument in args)
                        {
                            writer.Write(argument);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                RedirectError = e;
                DebugHelper.WriteException(e, $"SingleInstanceManager: Could not pass the arguments to the running instance on '{PipeName}'");
            }
        }

        public void Dispose()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }

            ReleaseInstanceLocks();
        }

        private void ReleaseInstanceLocks()
        {
            if (ownsMutex && mutex != null)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ignore release failures when mutex ownership was already lost.
                }
            }

            ownsMutex = false;
            mutex?.Dispose();
            mutex = null;

            lockFileStream?.Dispose();
            lockFileStream = null;
        }
    }
}
