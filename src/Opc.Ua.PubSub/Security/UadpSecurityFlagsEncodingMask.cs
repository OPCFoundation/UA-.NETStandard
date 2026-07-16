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

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// SecurityFlags byte from the UADP NetworkMessage SecurityHeader.
    /// </summary>
    /// <remarks>
    /// Mirrors the bit layout from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.1.6">
    /// Part 14 §A.2.1.6 (NetworkMessage signed and encrypted)</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.5">
    /// Part 14 §A.2.2.5 (NetworkMessage signed)</see>.
    /// </remarks>
    [Flags]
    public enum UadpSecurityFlagsEncodingMask : byte
    {
        /// <summary>
        /// No flags set.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// NetworkMessage Signed.
        /// </summary>
        NetworkMessageSigned = 0x01,

        /// <summary>
        /// NetworkMessage Encrypted.
        /// </summary>
        NetworkMessageEncrypted = 0x02,

        /// <summary>
        /// SecurityFooter present.
        /// </summary>
        SecurityFooterEnabled = 0x04,

        /// <summary>
        /// Force key reset.
        /// </summary>
        ForceKeyReset = 0x08
    }
}
