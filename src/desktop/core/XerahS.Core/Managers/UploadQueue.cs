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

using XerahS.Common;

namespace XerahS.Core.Managers
{
    /// <summary>
    /// ShareX's simultaneous upload limit (Application Settings → Upload): when the limit is above 0, at most that many
    /// tasks run at a time, and the other tasks wait in the order they arrived, as in ShareX's TaskManager.StartTasks.
    /// 0 disables the limit.
    /// </summary>
    public sealed class UploadQueue
    {
        public static UploadQueue Instance { get; } = new(() => SettingsManager.Settings?.UploadLimit ?? 0);

        private readonly Func<int> _getLimit;
        private readonly object _lock = new();
        private readonly LinkedList<TaskCompletionSource> _waiting = new();
        private int _running;

        internal UploadQueue(Func<int> getLimit)
        {
            _getLimit = getLimit;
        }

        public int RunningCount
        {
            get { lock (_lock) return _running; }
        }

        public int WaitingCount
        {
            get { lock (_lock) return _waiting.Count; }
        }

        /// <summary>
        /// Waits until the task may run. Disposing the result frees its place for the next waiting task.
        /// </summary>
        public async Task<IDisposable> EnterAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            LinkedListNode<TaskCompletionSource> node;
            int running;
            lock (_lock)
            {
                // The limit may have been raised since the last task finished.
                StartWaiting();
                if (_waiting.Count == 0 && HasFreePlace())
                {
                    _running++;
                    return new Place(this);
                }

                node = _waiting.AddLast(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                running = _running;
            }

            DebugHelper.WriteLine($"Upload limit reached ({running} running); the task is waiting in the queue.");
            using (token.Register(() => Cancel(node)))
            {
                await node.Value.Task.ConfigureAwait(false);
            }

            return new Place(this);
        }

        private void Cancel(LinkedListNode<TaskCompletionSource> node)
        {
            lock (_lock)
            {
                // A task that has just been started keeps its place; it stops on its own cancellation.
                if (node.List == null) return;
                _waiting.Remove(node);
            }

            node.Value.TrySetCanceled();
        }

        private void Release()
        {
            lock (_lock)
            {
                _running--;
                StartWaiting();
            }
        }

        private void StartWaiting()
        {
            while (_waiting.First is { } first && HasFreePlace())
            {
                _waiting.RemoveFirst();
                _running++;
                first.Value.TrySetResult();
            }
        }

        private bool HasFreePlace()
        {
            int limit = _getLimit();
            return limit <= 0 || _running < limit;
        }

        private sealed class Place(UploadQueue queue) : IDisposable
        {
            private int _released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0) queue.Release();
            }
        }
    }
}
