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

namespace XerahS.App;

internal enum IntegrationAction { ImageEditor, CustomUploader, ImageEffect, NativeMessaging }
internal sealed record IntegrationRequest(IntegrationAction Action, string Path);

internal static class IntegrationArguments
{
    internal static string[] Extract(string[] arguments, out List<IntegrationRequest> requests)
    {
        requests = new();
        var remaining = new List<string>();
        IntegrationAction? mode = null;
        foreach (string argument in arguments)
        {
            IntegrationAction? command = argument.ToLowerInvariant() switch
            {
                "-imageeditor" or "--image-editor" => IntegrationAction.ImageEditor,
                "-customuploader" => IntegrationAction.CustomUploader,
                "-imageeffect" => IntegrationAction.ImageEffect,
                "-nativemessaginginput" => IntegrationAction.NativeMessaging,
                _ => null
            };
            if (command != null) { mode = command; continue; }
            if (argument.StartsWith('-')) { mode = null; remaining.Add(argument); continue; }
            string path = Uri.TryCreate(argument, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : argument;
            IntegrationAction? action = mode ?? Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".sxcu" => IntegrationAction.CustomUploader,
                ".sxie" or ".xsie" => IntegrationAction.ImageEffect,
                _ => (IntegrationAction?)null
            };
            if (action != null) requests.Add(new IntegrationRequest(action.Value, path));
            else remaining.Add(argument);
        }
        return remaining.ToArray();
    }

    internal static List<string> ExpandUploadPaths(IEnumerable<string> files, IEnumerable<string> folders)
    {
        var result = new HashSet<string>(files.Select(Path.GetFullPath), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string folder in folders)
            foreach (string file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint
            })) result.Add(Path.GetFullPath(file));
        return result.ToList();
    }
}
