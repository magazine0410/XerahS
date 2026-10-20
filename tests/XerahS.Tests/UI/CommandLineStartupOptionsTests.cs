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

using Avalonia.Input;
using NUnit.Framework;
using XerahS.Core;
using XerahS.Core.Hotkeys;
using XerahS.Core.Tasks;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;

namespace XerahS.Tests.UI;

[TestFixture]
public class CommandLineStartupOptionsTests
{
    [Test]
    public void FailedHotkeysMessage_ListsTheHotkeysAsShareXDoes()
    {
        var region = new WorkflowSettings(WorkflowType.RectangleRegion, new HotkeyInfo(Key.PrintScreen, KeyModifiers.Control));
        var named = new WorkflowSettings(WorkflowType.PrintScreen, new HotkeyInfo(Key.F, KeyModifiers.Control | KeyModifiers.Shift));
        named.TaskSettings.Description = "Full screen";
        string nl = Environment.NewLine;

        Assert.That(WorkflowOrchestrator.GetFailedHotkeysMessage([]), Is.Null);
        Assert.That(WorkflowOrchestrator.GetFailedHotkeysMessage([region]), Is.EqualTo(
            $"Unable to register hotkey:{nl}{nl}[{region.HotkeyInfo}] Capture region{nl}{nl}" +
            $"Please select a different hotkey or quit the conflicting application and reopen {XerahS.Common.AppResources.AppName}."));
        Assert.That(WorkflowOrchestrator.GetFailedHotkeysMessage([region, named]), Does.StartWith(
            $"Unable to register hotkeys:{nl}{nl}[{region.HotkeyInfo}] Capture region{nl}[{named.HotkeyInfo}] Full screen{nl}{nl}"));
    }

    [Test]
    public void AutoClose_WaitsForOtherTasksButNotForToolsAndTasksWithoutContent()
    {
        string file = Path.Combine(Path.GetTempPath(), "xerahs-autoclose-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(file, "x");
        using var finished = WorkerTask.Create(new TaskSettings());
        using var empty = WorkerTask.Create(new TaskSettings());
        using var queued = WorkerTask.Create(new TaskSettings());
        using var tool = WorkerTask.Create(new TaskSettings());
        finished.Info.FilePath = file;
        tool.Info.IsDispatchTask = true;

        Assert.That(WorkflowOrchestrator.ShouldAutoClose(finished, [finished]), Is.True);
        Assert.That(WorkflowOrchestrator.ShouldAutoClose(finished, [finished, tool]), Is.True, "An open tool does not keep XerahS running.");
        Assert.That(WorkflowOrchestrator.ShouldAutoClose(finished, [finished, queued]), Is.False, "A queued task is busy.");
        Assert.That(WorkflowOrchestrator.ShouldAutoClose(empty, [empty]), Is.False, "A cancelled capture is not a task in ShareX.");

        tool.Info.FilePath = file;
        Assert.That(WorkflowOrchestrator.ShouldAutoClose(tool, [tool]), Is.False);

        // An aborted recording keeps the path of the file it deleted.
        File.Delete(file);
        Assert.That(WorkflowOrchestrator.ShouldAutoClose(finished, [finished]), Is.False);
    }

    [Test]
    public void VideoEditorJob_RunsItsTool()
    {
        // Jobs outside the tool list are treated as captures; the Video editor job then did nothing.
        Assert.That(WorkflowCatalog.IsToolWorkflow(WorkflowType.VideoEditor), Is.True);
    }
}
