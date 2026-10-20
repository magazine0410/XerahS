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

using System.Drawing;
using NUnit.Framework;
using XerahS.Core.Tasks;
using XerahS.Core.Tasks.Pipeline;
using XerahS.RegionCapture.ScreenRecording;

namespace XerahS.Tests.Tasks;

public class LastRecordingRegionTests
{
    [Test]
    public void LastRecordingRegion_IsKeptWithinTheScreens()
    {
        var screens = new Rectangle(0, 0, 1920, 1080);

        Assert.That(CaptureStage.GetLastRecordingRegion(new Rectangle(100, 100, 640, 480), screens), Is.EqualTo(new Rectangle(100, 100, 640, 480)));
        Assert.That(CaptureStage.GetLastRecordingRegion(new Rectangle(1800, 1000, 400, 300), screens), Is.EqualTo(new Rectangle(1800, 1000, 120, 80)));
        Assert.That(CaptureStage.GetLastRecordingRegion(new Rectangle(3000, 100, 400, 300), screens).IsEmpty, Is.True, "A region on a monitor that is gone");
        Assert.That(CaptureStage.GetLastRecordingRegion(Rectangle.Empty, screens).IsEmpty, Is.True, "No recording yet");
        Assert.That(CaptureStage.GetLastRecordingRegion(new Rectangle(100, 100, 640, 480), Rectangle.Empty), Is.EqualTo(new Rectangle(100, 100, 640, 480)));
    }

    [Test]
    public void RecordingArea_IsTheRegion_OrUnknownForAPortalSource()
    {
        Assert.That(WorkerTask.GetRecordingArea(CaptureMode.Region, IntPtr.Zero, new Rectangle(10, 20, 300, 200), clientArea: false),
            Is.EqualTo(new Rectangle(10, 20, 300, 200)));
        Assert.That(WorkerTask.GetRecordingArea(CaptureMode.Screen, IntPtr.Zero, null, clientArea: false), Is.Null);
        Assert.That(WorkerTask.GetRecordingArea(CaptureMode.Window, IntPtr.Zero, null, clientArea: false), Is.Null);
    }
}
