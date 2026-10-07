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

using System.Buffers.Binary;

namespace XerahS.Common;

public static partial class ImageHelpers
{
    /// <summary>Removes exactly the four color-space chunks removed by ShareX.</summary>
    public static MemoryStream PNGStripColorSpaceInformation(MemoryStream input)
    {
        ReadOnlySpan<byte> bytes = input.ToArray();
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!bytes.StartsWith(signature)) throw new InvalidDataException("Invalid PNG signature.");
        var output = new MemoryStream();
        try
        {
            output.Write(signature);
            int offset = signature.Length;
            while (offset < bytes.Length)
            {
                if (bytes.Length - offset < 12) throw new InvalidDataException("Truncated PNG chunk.");
                uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
                if (length > bytes.Length - offset - 12) throw new InvalidDataException("Invalid PNG chunk length.");
                var type = bytes.Slice(offset + 4, 4);
                int chunkLength = checked((int)length + 12);
                if (!type.SequenceEqual("gAMA"u8) && !type.SequenceEqual("cHRM"u8) &&
                    !type.SequenceEqual("sRGB"u8) && !type.SequenceEqual("iCCP"u8))
                    output.Write(bytes.Slice(offset, chunkLength));
                offset += chunkLength;
            }
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
