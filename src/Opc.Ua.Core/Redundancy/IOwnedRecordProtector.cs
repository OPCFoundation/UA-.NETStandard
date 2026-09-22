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

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Protects records and transfers ownership of decrypted plaintext so callers can erase secrets.
    /// </summary>
    public interface IOwnedRecordProtector : IRecordProtector
    {
        /// <summary>
        /// Authenticates the required context and transfers the original decrypted buffer to the caller.
        /// </summary>
        /// <remarks>
        /// The buffer must not alias the protected input. Implementations must not leave another
        /// unwiped plaintext copy behind. The caller must wipe the returned buffer in a finally block.
        /// Rejection must not trigger an unbound decrypt or an immutable-plaintext fallback.
        /// </remarks>
        /// <param name="context">The exact authenticated context; null and empty bytes are equivalent.</param>
        /// <param name="protectedRecord">The protected envelope.</param>
        /// <param name="plaintext">The caller-owned plaintext buffer on success; an empty buffer on failure.</param>
        /// <returns>Whether the record was authenticated for the context and decrypted.</returns>
        bool TryUnprotectOwned(ByteString context, ByteString protectedRecord, out byte[] plaintext);
    }
}
