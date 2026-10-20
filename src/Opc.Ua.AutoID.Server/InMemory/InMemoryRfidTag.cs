/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// The initial state of a simulated EPC Gen2 tag.
    /// </summary>
    /// <remarks>
    /// The memory banks follow EPC Class 1 Gen2: bank 0 holds the kill and the access password (four
    /// bytes each), bank 1 the CRC, the protocol control word and the EPC, bank 2 the tag identifier
    /// and bank 3 the user memory.
    /// </remarks>
    public sealed class InMemoryRfidTag
    {
        /// <summary>
        /// Gets the size of the reserved bank in bytes.
        /// </summary>
        public const int ReservedBankSize = 8;

        /// <summary>
        /// Gets the size of the tag identifier bank in bytes.
        /// </summary>
        public const int TidBankSize = 12;

        /// <summary>
        /// Creates a tag.
        /// </summary>
        /// <param name="epc">The EPC. An even number of bytes, 2 to 62.</param>
        /// <exception cref="ArgumentException">The EPC length is not valid.</exception>
        public InMemoryRfidTag(ByteString epc)
        {
            if (epc.Length is < 2 or > 62 || (epc.Length & 1) != 0)
            {
                throw new ArgumentException("An EPC has an even number of bytes, from 2 to 62.", nameof(epc));
            }
            Epc = epc;
        }

        /// <summary>
        /// Gets the EPC of the tag.
        /// </summary>
        public ByteString Epc { get; }

        /// <summary>
        /// Gets or sets the access password. Four bytes; all zero means no password.
        /// </summary>
        public ByteString AccessPassword { get; set; } = ByteString.From(new byte[4]);

        /// <summary>
        /// Gets or sets the kill password. Four bytes; all zero disables the kill command.
        /// </summary>
        public ByteString KillPassword { get; set; } = ByteString.From(new byte[4]);

        /// <summary>
        /// Gets or sets the size of the user memory in bytes. Defaults to 64.
        /// </summary>
        public int UserMemorySize { get; set; } = 64;

        /// <summary>
        /// Gets or sets the antenna that sees the tag. Defaults to 1.
        /// </summary>
        public int Antenna { get; set; } = 1;

        /// <summary>
        /// Gets or sets the received signal strength in dBm. Defaults to -55.
        /// </summary>
        public int Strength { get; set; } = -55;
    }
}
