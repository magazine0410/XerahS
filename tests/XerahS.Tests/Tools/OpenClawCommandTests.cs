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
using System.Text.Json;
using XerahS.CLI.Commands;

namespace XerahS.Tests.Tools;

[TestFixture]
public sealed class OpenClawCommandTests
{
    [Test]
    public void BuildManifest_AllJsonOutputCommandsIncludeJsonFlag()
    {
        // Defensive regression: if anyone adds a new command with JsonOutput=true but forgets the
        // --json flag, the OpenClaw wrapper's requireUploaderReport-style checks will fail at runtime.
        // The wrapper has multiple commands that consume JSON; this guards the entire manifest.
        OpenClawManifest manifest = OpenClawCommand.BuildManifest();

        List<string> offenders = new();
        foreach (OpenClawManifestCommand command in manifest.Commands)
        {
            if (command.JsonOutput && !command.Command.Contains("--json", StringComparison.Ordinal))
            {
                offenders.Add(command.Command);
            }
        }

        Assert.That(offenders, Is.Empty,
            "Every command with JsonOutput=true must include '--json' in its literal command string. "
            + "Offending commands: " + string.Join(", ", offenders));
    }

    [Test]
    public void BuildManifest_SerializedJsonIncludesJsonFlagOnBootstrapCommands()
    {
        // The manifest subcommand's actual on-the-wire output is JsonSerializer.Serialize(BuildManifest(),
        // OpenClawJsonOptions.Default). This test pins the serialized form so a future serializer-options
        // change (e.g. dropping CamelCase) cannot silently re-introduce a key mismatch with the wrapper.
        OpenClawManifest manifest = OpenClawCommand.BuildManifest();
        string json = JsonSerializer.Serialize(manifest, OpenClawJsonOptions.Default);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("bootstrapCommand").GetString(), Is.EqualTo("xerahscli bootstrap uploaders --json"));
            Assert.That(root.GetProperty("healthCommand").GetString(), Is.EqualTo("xerahscli doctor uploaders --json"));

            JsonElement commands = root.GetProperty("commands");
            JsonElement bootstrapEntry = default;
            bool foundBootstrap = false;
            foreach (JsonElement element in commands.EnumerateArray())
            {
                string? command = element.GetProperty("command").GetString();
                if (command is not null && command.StartsWith("bootstrap uploaders", StringComparison.Ordinal))
                {
                    bootstrapEntry = element.Clone();
                    foundBootstrap = true;
                    break;
                }
            }

            Assert.That(foundBootstrap, Is.True, "Serialized manifest must include a 'bootstrap uploaders ...' command entry.");
            Assert.That(bootstrapEntry.GetProperty("command").GetString(), Is.EqualTo("bootstrap uploaders --json"));
            Assert.That(bootstrapEntry.GetProperty("jsonOutput").GetBoolean(), Is.True);
        });
    }
}
