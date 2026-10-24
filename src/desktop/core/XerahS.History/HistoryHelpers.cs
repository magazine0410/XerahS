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
using System.Text;

namespace XerahS.History
{
    public static class HistoryHelpers
    {
        private static string FormatCountWithPercentage<TKey>(IGrouping<TKey, HistoryItem> grouping, int totalCount)
        {
            float percentage = totalCount > 0 ? grouping.Count() / (float)totalCount * 100 : 0;
            return string.Format("{0}: {1} ({2:N0}%)", grouping.Key, grouping.Count(), percentage);
        }

        /// <summary>
        /// Formats tags for editing, one <c>key=value</c> per line, ordered by key, as ShareX's "Edit item" does.
        /// XerahS stores values with line breaks (the OCR text, upload errors), which ShareX does not; they are written
        /// as <c>\n</c> (and a backslash as <c>\\</c>), so that every tag stays on its line.
        /// </summary>
        public static string FormatTags(IDictionary<string, string?>? tags)
        {
            if (tags == null || tags.Count == 0) return string.Empty;
            return string.Join(Environment.NewLine, tags.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={EscapeTagValue(pair.Value)}"));
        }

        /// <summary>
        /// Parses tags edited as one <c>key=value</c> per line, as ShareX's "Edit item" does: a line without "=" is a key with no value.
        /// <c>\n</c>, <c>\r</c>, and <c>\\</c> in a value are read back as written by <see cref="FormatTags"/>.
        /// </summary>
        public static Dictionary<string, string?> ParseTags(string? text)
        {
            Dictionary<string, string?> tags = new();
            foreach (string line in (text ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.IndexOf('=');
                string key = (separator >= 0 ? line[..separator] : line).Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    tags[key] = separator >= 0 ? UnescapeTagValue(line[(separator + 1)..]) : null;
                }
            }
            return tags;
        }

        private static string? EscapeTagValue(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOfAny(['\\', '\r', '\n']) < 0) return value;
            return value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string UnescapeTagValue(string value)
        {
            if (!value.Contains('\\')) return value;
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char next = i + 1 < value.Length ? value[i + 1] : '\0';
                if (value[i] == '\\' && next is '\\' or 'n' or 'r')
                {
                    sb.Append(next switch { 'n' => '\n', 'r' => '\r', _ => '\\' });
                    i++;
                }
                else
                {
                    sb.Append(value[i]);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Creates history items for every file in a folder and its subfolders, dated by the file's last write time and
        /// ordered by it, as ShareX's "Import folder" does. <paramref name="existingFilePaths"/> are skipped.
        /// </summary>
        public static List<HistoryItem> CreateImportItems(string folderPath, bool onlyImages, ISet<string>? existingFilePaths = null)
        {
            IEnumerable<string> files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories);
            if (onlyImages)
            {
                files = files.Where(FileHelpers.IsImageFile);
            }

            List<HistoryItem> imported = [];
            foreach (string file in files.OrderBy(File.GetLastWriteTime))
            {
                if (existingFilePaths != null && existingFilePaths.Count > 0 && existingFilePaths.Contains(file))
                {
                    continue;
                }

                imported.Add(new HistoryItem
                {
                    FileName = Path.GetFileName(file),
                    FilePath = file,
                    DateTime = File.GetLastWriteTime(file),
                    Type = FileHelpers.IsImageFile(file) ? "Image" : FileHelpers.IsTextFile(file) ? "Text" : "File"
                });
            }

            return imported;
        }

        public static string OutputStats(List<HistoryItem> historyItems)
        {
            string empty = "(empty)";
            int totalCount = historyItems.Count;

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("History item counts:");
            sb.AppendLine("Total: " + totalCount);

            IEnumerable<string> types = historyItems.
                GroupBy(x => x.Type).
                OrderByDescending(x => x.Count()).
                Select(x => FormatCountWithPercentage(x, totalCount));

            sb.AppendLine(string.Join(Environment.NewLine, types));

            sb.AppendLine();
            sb.AppendLine("Yearly usages:");

            IEnumerable<string> yearlyUsages = historyItems.
                GroupBy(x => x.DateTime.Year).
                OrderByDescending(x => x.Key).
                Select(x => FormatCountWithPercentage(x, totalCount));

            sb.AppendLine(string.Join(Environment.NewLine, yearlyUsages));

            sb.AppendLine();
            sb.AppendLine("File extensions:");

            IEnumerable<string> fileExtensions = historyItems.
                Where(x => !string.IsNullOrEmpty(x.FileName) && !x.FileName.EndsWith(")")).
                Select(x => FileHelpers.GetFileNameExtension(x.FileName)).
                GroupBy(x => string.IsNullOrWhiteSpace(x) ? empty : x).
                OrderByDescending(x => x.Count()).
                Select(x => string.Format("[{0}] {1}", x.Count(), x.Key));

            sb.AppendLine(string.Join(Environment.NewLine, fileExtensions));

            sb.AppendLine();
            sb.AppendLine("Hosts:");

            IEnumerable<string> hosts = historyItems.
                GroupBy(x => string.IsNullOrWhiteSpace(x.Host) ? empty : x.Host).
                OrderByDescending(x => x.Count()).
                Select(x => string.Format("[{0}] {1}", x.Count(), x.Key));

            sb.AppendLine(string.Join(Environment.NewLine, hosts));

            sb.AppendLine();
            sb.AppendLine("Process names:");

            IEnumerable<string> processNames = historyItems.
                GroupBy(x => string.IsNullOrWhiteSpace(x.TagsProcessName) ? empty : x.TagsProcessName).
                OrderByDescending(x => x.Count()).
                Select(x => string.Format("[{0}] {1}", x.Count(), x.Key));

            sb.Append(string.Join(Environment.NewLine, processNames));

            return sb.ToString();
        }
    }
}

