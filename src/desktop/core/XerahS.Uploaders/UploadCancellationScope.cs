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

namespace XerahS.Uploaders;

/// <summary>Registers active transfers and batches for the Stop all uploads job.</summary>
public sealed class UploadCancellationScope : IDisposable
{
    private static readonly object Gate = new();
    private static readonly HashSet<UploadCancellationScope> ActiveScopes = [];
    private readonly CancellationTokenSource _source;

    public CancellationToken Token { get; }

    public UploadCancellationScope(CancellationToken cancellationToken = default)
    {
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Token = _source.Token;
        lock (Gate)
        {
            ActiveScopes.Add(this);
        }
    }

    public static void CancelAll()
    {
        UploadCancellationScope[] scopes;
        lock (Gate)
        {
            scopes = ActiveScopes.ToArray();
        }

        foreach (var scope in scopes)
        {
            try
            {
                scope._source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The upload completed between taking the snapshot and cancelling it.
            }
            catch (AggregateException ex)
            {
                DebugHelper.WriteException(ex, "An upload cancellation callback failed");
            }
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (!ActiveScopes.Remove(this)) return;
        }
        _source.Dispose();
    }
}
