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

using System.Reflection;
using NUnit.Framework;
using XerahS.Core;
using XerahS.Core.Managers;

namespace XerahS.Tests.Helpers;

[TestFixture]
public class WatchFolderManagerStopAsyncTests
{
    [Test]
    public async Task StopAsync_ReturnsFalse_WhenTimeoutExpiresWithInFlightTasks()
    {
        WatchFolderManager manager = WatchFolderManager.Instance;
        FieldInfo activeCountField = GetActiveCountField();
        int originalValue = (int)(activeCountField.GetValue(manager) ?? 0);

        try
        {
            activeCountField.SetValue(manager, 1);

            bool stopped = await manager.StopAsync(TimeSpan.FromMilliseconds(30));

            Assert.That(stopped, Is.False);
        }
        finally
        {
            activeCountField.SetValue(manager, originalValue);
        }
    }

    [Test]
    public async Task StopAsync_ReturnsTrue_WhenNoInFlightTasks()
    {
        WatchFolderManager manager = WatchFolderManager.Instance;
        FieldInfo activeCountField = GetActiveCountField();
        int originalValue = (int)(activeCountField.GetValue(manager) ?? 0);

        try
        {
            activeCountField.SetValue(manager, 0);

            bool stopped = await manager.StopAsync(TimeSpan.FromMilliseconds(30));

            Assert.That(stopped, Is.True);
        }
        finally
        {
            activeCountField.SetValue(manager, originalValue);
        }
    }

    [Test]
    public async Task OnFileDetected_CountsTheFileBeforeItsTaskStarts()
    {
        WatchFolderManager manager = WatchFolderManager.Instance;
        FieldInfo activeCountField = GetActiveCountField();
        FieldInfo acceptNewFilesField = typeof(WatchFolderManager).GetField("_acceptNewFiles", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find WatchFolderManager accept field.");
        MethodInfo onFileDetected = typeof(WatchFolderManager).GetMethod("OnFileDetected", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find WatchFolderManager.OnFileDetected.");
        int originalCount = (int)(activeCountField.GetValue(manager) ?? 0);
        object? originalAccept = acceptNewFilesField.GetValue(manager);

        // An empty file never reads as ready, so its task keeps polling until the file is deleted.
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, []);

        try
        {
            activeCountField.SetValue(manager, 0);
            acceptNewFilesField.SetValue(manager, true);

            onFileDetected.Invoke(manager, [new WatchFolderSettings { Enabled = true }, path]);
            int countAfterDetection = (int)(activeCountField.GetValue(manager) ?? 0);

            File.Delete(path);
            bool drained = await manager.StopAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(countAfterDetection, Is.EqualTo(1));
                Assert.That(drained, Is.True);
            });
        }
        finally
        {
            File.Delete(path);
            activeCountField.SetValue(manager, originalCount);
            acceptNewFilesField.SetValue(manager, originalAccept);
        }
    }

    private static FieldInfo GetActiveCountField()
    {
        return typeof(WatchFolderManager).GetField("_activeProcessingCount", BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException("Could not find WatchFolderManager active processing field.");
    }
}
