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

namespace XerahS.UI.Services;

/// <summary>
/// Runs a save once changes have stopped for a moment. Dragging a color or typing a name changes a setting many
/// times a second, and each save writes the whole configuration file and its backups.
/// </summary>
internal sealed class DelayedSave(Func<Task> save, TimeSpan delay)
{
    private readonly object _lock = new();
    private CancellationTokenSource? _pending;

    /// <summary>Saves after the delay, unless another change comes first and starts the delay again.</summary>
    public void Request()
    {
        CancellationTokenSource next = new();
        lock (_lock)
        {
            _pending?.Cancel();
            _pending = next;
        }

        _ = RunAsync(next);
    }

    private async Task RunAsync(CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(delay, pending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            pending.Dispose();
            return;
        }

        lock (_lock)
        {
            if (ReferenceEquals(_pending, pending)) _pending = null;
        }

        pending.Dispose();
        await save().ConfigureAwait(false);
    }
}
