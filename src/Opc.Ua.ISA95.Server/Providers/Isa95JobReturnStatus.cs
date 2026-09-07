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

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// The individual bits of the ISA-95 Job Control <c>ReturnStatus</c>
    /// <see cref="ulong"/> bitmap defined in Annex B.2 of the V1 and V2 companion
    /// specifications. The value returned by a Job Control method is a bitwise
    /// combination of these flags; more than one bit can be set at once.
    /// </summary>
    public static class Isa95JobReturnStatus
    {
        /// <summary>
        /// No bits set. Used as a starting accumulator only; a successful call
        /// sets <see cref="Success"/>.
        /// </summary>
        public const ulong None = 0UL;

        /// <summary>
        /// The operation completed successfully (bit 0).
        /// </summary>
        public const ulong Success = 1UL << 0;

        /// <summary>
        /// The supplied job order identifier is unknown or invalid (bit 1).
        /// </summary>
        public const ulong UnknownJobOrderId = 1UL << 1;

        /// <summary>
        /// The supplied command is not valid (bit 2). Primarily used by V1.
        /// </summary>
        public const ulong InvalidCommand = 1UL << 2;

        /// <summary>
        /// The job order is in a state that does not permit the operation
        /// (bit 3).
        /// </summary>
        public const ulong InvalidStatus = 1UL << 3;

        /// <summary>
        /// The receiver is unable to accept the job order or response (bit 4).
        /// </summary>
        public const ulong UnableToAccept = 1UL << 4;

        /// <summary>
        /// The request itself is invalid, for example a required field is missing
        /// (bit 32).
        /// </summary>
        public const ulong InvalidRequest = 1UL << 32;
    }
}
