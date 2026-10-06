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


using Microsoft.Data.Sqlite;

namespace XerahS.History;

/// <summary>
/// Copies the history database through SQLite's backup API, which gives a consistent copy while
/// XerahS keeps the database open, and writes a copy back into the open database.
/// </summary>
public static class HistoryDatabaseBackup
{
    public static byte[] Export(string databasePath)
    {
        string copy = GetTemporaryPath(databasePath);
        try
        {
            using (var source = Open(databasePath, SqliteOpenMode.ReadOnly))
            using (var target = Open(copy, SqliteOpenMode.ReadWriteCreate))
            {
                source.BackupDatabase(target);
            }
            return File.ReadAllBytes(copy);
        }
        finally { DeleteDatabaseFiles(copy); }
    }

    /// <summary>Replaces the history in <paramref name="databasePath"/> with the backed-up database.</summary>
    public static void Import(byte[] database, string databasePath)
    {
        string copy = WriteTemporaryCopy(database, databasePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            using var source = Open(copy, SqliteOpenMode.ReadOnly);
            using var target = Open(databasePath, SqliteOpenMode.ReadWriteCreate);
            source.BackupDatabase(target);
        }
        finally { DeleteDatabaseFiles(copy); }
    }

    /// <summary>Throws when the bytes are not an intact SQLite database with XerahS's History table.</summary>
    public static void Validate(byte[] database, string databasePath)
    {
        string copy = WriteTemporaryCopy(database, databasePath);
        try
        {
            using var connection = Open(copy, SqliteOpenMode.ReadOnly);
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(check.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                    throw new InvalidDataException("The backed-up history database is damaged.");
            }
            using var table = connection.CreateCommand();
            table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'History';";
            if (Convert.ToInt64(table.ExecuteScalar()) != 1)
                throw new InvalidDataException("The backed-up database is not a XerahS history.");
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("The backed-up history is not a valid database: " + ex.Message, ex);
        }
        finally { DeleteDatabaseFiles(copy); }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 30
        }.ToString());
        connection.Open();
        return connection;
    }

    // Temporary copies sit next to the history, not in the temporary folder, which can be small.
    private static string GetTemporaryPath(string databasePath)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $".history-backup-{Guid.NewGuid():N}.db");
    }

    private static string WriteTemporaryCopy(byte[] database, string databasePath)
    {
        string copy = GetTemporaryPath(databasePath);
        File.WriteAllBytes(copy, database);
        return copy;
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
