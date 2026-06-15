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
    /// Provides per-message nonces honouring the AES-CTR layout for
    /// PubSub security. A nonce is composed of the SKS-issued
    /// <c>KeyNonce</c> prefix, the publisher-chosen
    /// <c>MessageRandom</c> middle, and a monotonic counter suffix
    /// that increments per encrypted NetworkMessage.
    /// </summary>
    /// <remarks>
    /// Implements the nonce layout from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.2">
    /// Part 14 §7.2.4.4.3.2 (Table 156) PubSub nonce composition</see>.
    /// </remarks>
    public interface INonceProvider
    {
        /// <summary>
        /// Writes the next nonce into <paramref name="buffer"/>.
        /// The buffer length must equal the policy's
        /// <see cref="IPubSubSecurityPolicy.NonceLength"/>.
        /// </summary>
        /// <param name="buffer">Destination span receiving the nonce.</param>
        void GetNext(Span<byte> buffer);
    }
}
