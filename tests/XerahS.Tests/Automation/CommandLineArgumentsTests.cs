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

using NUnit.Framework;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Automation;
using XerahS.Core.Hotkeys;

namespace XerahS.Tests.Automation;

[TestFixture]
public class CommandLineArgumentsTests
{
    [Test]
    public void Extract_TakesJobsWorkflowsAndTaskAndLeavesTheRestInOrder()
    {
        string[] left = CommandLineArguments.Extract(
            ["-silent", "/tmp/a.png", "--send-to", "-RectangleRegion", "-workflow", "My flow", "/tmp/b.png",
             "-task", "Uploads", "-ImageEditor", "/tmp/c.png", "-AutoClose"],
            out CommandLineCommands commands);

        Assert.That(left, Is.EqualTo(new[] { "-silent", "/tmp/a.png", "--send-to", "/tmp/b.png", "-AutoClose" }));
        Assert.That(commands.Requests, Is.EqualTo(new[]
        {
            new CommandLineRequest(CommandLineRequestKind.Job, WorkflowType.RectangleRegion, null),
            new CommandLineRequest(CommandLineRequestKind.Workflow, WorkflowType.None, "My flow"),
            new CommandLineRequest(CommandLineRequestKind.Job, WorkflowType.ImageEditor, "/tmp/c.png")
        }));
        Assert.That(commands.TaskName, Is.EqualTo("Uploads"));
    }

    [Test]
    public void Extract_MatchesJobNamesInAnyCaseButOnlyWithOneHyphen()
    {
        string[] left = CommandLineArguments.Extract(
            ["-rectangleregion", "-PRINTSCREEN", "--ActiveWindow", "-None", "-5", "-RectangleRegion,PrintScreen", "-"],
            out CommandLineCommands commands);

        Assert.That(commands.Requests.Select(r => r.Job), Is.EqualTo(new[] { WorkflowType.RectangleRegion, WorkflowType.PrintScreen }));
        Assert.That(left, Is.EqualTo(new[] { "--ActiveWindow", "-None", "-5", "-RectangleRegion,PrintScreen", "-" }));
    }

    [Test]
    public void Extract_IgnoresWorkflowAndTaskWithoutNameAndKeepsTheFirstTask()
    {
        string[] left = CommandLineArguments.Extract(["-workflow", "-task", "-task", "First", "-task", "Second", "-workflow"],
            out CommandLineCommands commands);

        Assert.That(left, Is.Empty);
        Assert.That(commands.Requests, Is.Empty);
        Assert.That(commands.TaskName, Is.EqualTo("First"));
    }

    [Test]
    public void IsCommandPresent_MatchesShortNamesAndAnyCase()
    {
        Assert.That(CommandLineArguments.IsCommandPresent(["/tmp/a.png", "-M"], CommandLineArguments.MultiInstanceCommands), Is.True);
        Assert.That(CommandLineArguments.IsCommandPresent(["-Multi"], CommandLineArguments.MultiInstanceCommands), Is.True);
        Assert.That(CommandLineArguments.IsCommandPresent(["--multi"], CommandLineArguments.MultiInstanceCommands), Is.False);
        Assert.That(CommandLineArguments.IsCommandPresent(["-p"], CommandLineArguments.PortableCommands), Is.True);
        Assert.That(CommandLineArguments.IsCommandPresent(["-nohotkeys"], CommandLineArguments.NoHotkeysCommand), Is.True);
        Assert.That(CommandLineArguments.IsCommandPresent(["-autoclose"], CommandLineArguments.AutoCloseCommand), Is.True);
        Assert.That(CommandLineArguments.IsCommandPresent(["-AutoCloses"], CommandLineArguments.AutoCloseCommand), Is.False);
        Assert.That(CommandLineArguments.IsCommandPresent(null, CommandLineArguments.AutoCloseCommand), Is.False);
    }

    [Test]
    public void FindWorkflow_MatchesTheNameExactlyAndNeedsAJob()
    {
        var unnamed = new WorkflowSettings();
        unnamed.TaskSettings.Job = WorkflowType.RectangleRegion;
        var none = new WorkflowSettings();
        none.TaskSettings.Description = "Uploads";
        var named = new WorkflowSettings();
        named.TaskSettings.Job = WorkflowType.PrintScreen;
        named.TaskSettings.Description = "Uploads";
        WorkflowSettings[] workflows = [unnamed, none, named];

        Assert.That(CommandLineArguments.FindWorkflow(workflows, "Uploads"), Is.SameAs(named));
        Assert.That(CommandLineArguments.FindWorkflow(workflows, "uploads"), Is.Null);
        Assert.That(CommandLineArguments.FindWorkflow(workflows, EnumExtensions.GetDescription(WorkflowType.RectangleRegion)), Is.SameAs(unnamed));
        Assert.That(CommandLineArguments.FindTask(workflows, "Uploads"), Is.SameAs(none));
        Assert.That(CommandLineArguments.FindTask(null, "Uploads"), Is.Null);
    }

    [Test]
    public void ResolveRelativePaths_MakesExistingFilesAndFoldersAbsolute()
    {
        string folder = Path.Combine(Path.GetTempPath(), "xerahs-cli-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "dir"));
        File.WriteAllText(Path.Combine(folder, "shot.png"), "x");
        try
        {
            string[] resolved = CommandLineArguments.ResolveRelativePaths(
                ["shot.png", "-workflow", "shot.png", "dir", "missing.png", "-OCR", "shot.png", "https://example.com/shot.png",
                 "--run-workflow", "shot.png", "-task", "dir", "/tmp/absolute.png"],
                folder);

            string shot = Path.Combine(folder, "shot.png");
            Assert.That(resolved, Is.EqualTo(new[]
            {
                shot, "-workflow", "shot.png", Path.Combine(folder, "dir"), "missing.png", "-OCR", shot,
                "https://example.com/shot.png", "--run-workflow", "shot.png", "-task", "dir", "/tmp/absolute.png"
            }));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
