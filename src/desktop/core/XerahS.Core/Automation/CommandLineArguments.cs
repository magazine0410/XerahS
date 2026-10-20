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

using XerahS.Core.Hotkeys;

namespace XerahS.Core.Automation;

public enum CommandLineRequestKind
{
    /// <summary><c>-&lt;JobName&gt;</c>, such as <c>-RectangleRegion</c>, optionally followed by a file path.</summary>
    Job,

    /// <summary><c>-workflow "&lt;name&gt;"</c>.</summary>
    Workflow
}

/// <summary>A job or workflow argument. <see cref="Parameter"/> is the job's file path or the workflow's name.</summary>
public sealed record CommandLineRequest(CommandLineRequestKind Kind, WorkflowType Job, string? Parameter)
{
    public override string ToString()
    {
        string command = Kind == CommandLineRequestKind.Workflow ? "-" + CommandLineArguments.WorkflowCommand : "-" + Job;
        return string.IsNullOrEmpty(Parameter) ? command : $"{command} \"{Parameter}\"";
    }
}

/// <summary>The job, workflow, and task arguments found by <see cref="CommandLineArguments.Extract"/>.</summary>
public sealed class CommandLineCommands
{
    /// <summary>The jobs and workflows to run, in the order of the arguments.</summary>
    public List<CommandLineRequest> Requests { get; } = [];

    /// <summary><c>-task "&lt;name&gt;"</c>: the workflow whose settings the files on the command line are uploaded with.</summary>
    public string? TaskName { get; set; }
}

/// <summary>
/// ShareX's command line arguments (CLIManager and ShareXCLIManager). A command is an argument that starts with one hyphen,
/// matched without regard to case, and it takes the next argument as its parameter when that one does not start with a hyphen.
/// </summary>
public static class CommandLineArguments
{
    public const string WorkflowCommand = "workflow";
    public const string TaskCommand = "task";
    public const string NoHotkeysCommand = "NoHotkeys";
    public const string AutoCloseCommand = "AutoClose";
    public static readonly string[] MultiInstanceCommands = ["multi", "m"];
    public static readonly string[] PortableCommands = ["portable", "p"];

    private static readonly string[] JobNames = Enum.GetNames<WorkflowType>()
        .Where(name => name != nameof(WorkflowType.None))
        .ToArray();

    /// <summary>ShareX's IsCommandExist: true when an argument is "-name" for one of <paramref name="commands"/>.</summary>
    public static bool IsCommandPresent(IEnumerable<string>? arguments, params string[] commands)
    {
        return arguments != null &&
            arguments.Any(argument => TryGetCommandName(argument, out string name) &&
                commands.Any(command => command.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The job of a "-&lt;JobName&gt;" argument, such as <c>-RectangleRegion</c> (any case).</summary>
    public static bool TryGetJob(string argument, out WorkflowType job)
    {
        job = WorkflowType.None;
        if (!TryGetCommandName(argument, out string name))
        {
            return false;
        }

        string? jobName = JobNames.FirstOrDefault(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return jobName != null && Enum.TryParse(jobName, out job);
    }

    /// <summary>
    /// Takes out the arguments that ShareX's ShareXCLIManager runs: "-&lt;JobName&gt;" for every job, with an optional file path,
    /// "-workflow &lt;name&gt;", and "-task &lt;name&gt;". Returns the other arguments, in order.
    /// </summary>
    public static string[] Extract(IReadOnlyList<string> arguments, out CommandLineCommands commands)
    {
        commands = new CommandLineCommands();
        var remaining = new List<string>(arguments.Count);

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            bool isWorkflow = IsCommand(argument, WorkflowCommand);
            bool isTask = IsCommand(argument, TaskCommand);
            WorkflowType job = WorkflowType.None;

            if (!isWorkflow && !isTask && !TryGetJob(argument, out job))
            {
                remaining.Add(argument);
                continue;
            }

            string? parameter = i + 1 < arguments.Count && !arguments[i + 1].StartsWith('-') ? arguments[++i] : null;

            if (isWorkflow)
            {
                // As in ShareX, a workflow argument without a name does nothing.
                if (!string.IsNullOrEmpty(parameter))
                {
                    commands.Requests.Add(new CommandLineRequest(CommandLineRequestKind.Workflow, WorkflowType.None, parameter));
                }
            }
            else if (isTask)
            {
                // ShareX uses the first task argument that has a name.
                if (commands.TaskName == null && !string.IsNullOrEmpty(parameter))
                {
                    commands.TaskName = parameter;
                }
            }
            else
            {
                commands.Requests.Add(new CommandLineRequest(CommandLineRequestKind.Job, job, parameter));
            }
        }

        return remaining.ToArray();
    }

    /// <summary>
    /// ShareX's CheckCLIWorkflow: the first workflow that has a job and whose name (its description, or its job's name when it
    /// has none) is <paramref name="name"/>, in the same case.
    /// </summary>
    public static WorkflowSettings? FindWorkflow(IEnumerable<WorkflowSettings>? workflows, string name)
    {
        return workflows?.FirstOrDefault(workflow => workflow.TaskSettings != null &&
            workflow.Job != WorkflowType.None &&
            workflow.TaskSettings.ToString() == name);
    }

    /// <summary>ShareX's FindCLITask: as <see cref="FindWorkflow"/>, but a workflow without a job also counts.</summary>
    public static WorkflowSettings? FindTask(IEnumerable<WorkflowSettings>? workflows, string name)
    {
        return workflows?.FirstOrDefault(workflow => workflow.TaskSettings != null &&
            workflow.TaskSettings.ToString() == name);
    }

    /// <summary>
    /// Makes the relative paths of files and folders that exist in <paramref name="currentDirectory"/> absolute. Arguments are
    /// passed to a running instance as they are, and it would look for a relative path in its own working folder.
    /// The names after <c>-workflow</c> and <c>-task</c>, and other arguments, are left as they are.
    /// </summary>
    public static string[] ResolveRelativePaths(IReadOnlyList<string> arguments, string currentDirectory)
    {
        var resolved = new string[arguments.Count];

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            resolved[i] = argument;

            if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-') || Path.IsPathRooted(argument) ||
                argument.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            if (i > 0 && (IsCommand(arguments[i - 1], WorkflowCommand) || IsCommand(arguments[i - 1], TaskCommand) ||
                arguments[i - 1].Equals(XerahS.Common.AppContracts.Cli.RunWorkflowFlag, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                string fullPath = Path.GetFullPath(argument, currentDirectory);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    resolved[i] = fullPath;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return resolved;
    }

    private static bool IsCommand(string argument, string command)
    {
        return TryGetCommandName(argument, out string name) && name.Equals(command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"-name" gives "name". XerahS's own "--" options are not ShareX commands.</summary>
    private static bool TryGetCommandName(string? argument, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrEmpty(argument) || argument.Length < 2 || argument[0] != '-' || argument[1] == '-')
        {
            return false;
        }

        name = argument[1..];
        return true;
    }
}
