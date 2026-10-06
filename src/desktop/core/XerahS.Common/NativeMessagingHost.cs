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

using System.Text;

namespace XerahS.Common;

/// <summary>Browser native messaging framing (a native-endian length followed by UTF-8 JSON).</summary>
public class NativeMessagingHost
{
    public const int MaximumInputBytes = 64 * 1024 * 1024;
    public const int MaximumOutputBytes = 1024 * 1024;
    public string? Read() => Read(Console.OpenStandardInput());
    public void Write(string data) => Write(Console.OpenStandardOutput(), data);

    public static string? Read(Stream input)
    {
        byte[] length = new byte[4];
        int first = input.ReadByte();
        if (first < 0) return null;
        length[0] = (byte)first;
        input.ReadExactly(length.AsSpan(1));
        int count = BitConverter.ToInt32(length);
        if (count <= 0 || count > MaximumInputBytes) throw new InvalidDataException("Invalid native message length.");
        byte[] data = new byte[count];
        input.ReadExactly(data);
        return new UTF8Encoding(false, true).GetString(data);
    }

    public static void Write(Stream output, string data)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(data);
        if (bytes.Length > MaximumOutputBytes) throw new InvalidDataException("Native response exceeds the browser limit.");
        output.Write(BitConverter.GetBytes(bytes.Length));
        output.Write(bytes);
        output.Flush();
    }
}
