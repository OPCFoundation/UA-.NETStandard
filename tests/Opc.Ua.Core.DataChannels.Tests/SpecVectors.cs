/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Loads the annotated hex vectors published alongside the OPC UA
    /// Data Channels errata, so the framing here is checked against the
    /// specification's own bytes rather than against itself.
    /// </summary>
    internal static class SpecVectors
    {
        /// <summary>
        /// Bytes preceding the stream header under inline framing:
        /// message header, symmetric security header and sequence header.
        /// </summary>
        public const int InlinePrefix = 12 + 4 + 8;

        /// <summary>
        /// Bytes preceding the stream header under QUIC framing.
        /// </summary>
        public const int QuicPrefix = 12;

        /// <summary>
        /// Reads one vector and returns the whole chunk.
        /// </summary>
        /// <param name="name">The vector name without extension.</param>
        public static byte[] Load(string name)
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                name + ".hex.txt");

            var bytes = new List<byte>();

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length < 6)
                {
                    continue;
                }

                // Each row is "OFFSET  HH HH ... HH  ascii". The hex runs
                // from column 6 to column 6 + 16 * 3.
                int end = Math.Min(line.Length, 6 + (16 * 3));

                for (int ii = 6; ii + 1 < end; ii += 3)
                {
                    string pair = line.Substring(ii, 2);

                    if (pair == "  ")
                    {
                        break;
                    }

                    bytes.Add(byte.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }
            }

            return [.. bytes];
        }

        /// <summary>
        /// The MessageType of a chunk, as three ASCII characters.
        /// </summary>
        /// <param name="chunk">The chunk.</param>
        public static string MessageType(byte[] chunk)
        {
            return Encoding.ASCII.GetString(chunk, 0, 3);
        }

        /// <summary>
        /// The IsFinal byte of a chunk.
        /// </summary>
        /// <param name="chunk">The chunk.</param>
        public static char IsFinal(byte[] chunk)
        {
            return (char)chunk[3];
        }

        /// <summary>
        /// The MessageSize the chunk declares.
        /// </summary>
        /// <param name="chunk">The chunk.</param>
        public static uint MessageSize(byte[] chunk)
        {
            return BitConverter.ToUInt32(chunk, 4);
        }

        /// <summary>
        /// The secured body of a vector: everything from the stream
        /// header to the start of the footer.
        /// </summary>
        /// <param name="chunk">The whole chunk.</param>
        /// <param name="prefix">The bytes before the stream header.</param>
        /// <param name="footerSize">The message footer size.</param>
        public static ReadOnlyMemory<byte> Body(
            byte[] chunk,
            int prefix,
            int footerSize = 0)
        {
            return new ReadOnlyMemory<byte>(
                chunk,
                prefix,
                chunk.Length - prefix - footerSize);
        }
    }
}
