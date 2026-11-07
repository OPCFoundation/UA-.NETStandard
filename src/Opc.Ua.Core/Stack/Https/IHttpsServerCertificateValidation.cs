/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Allows an HTTPS channel to require independent TLS authentication before its first request.
    /// </summary>
    public interface IHttpsServerCertificateValidation
    {
        /// <summary>
        /// Requires OS TLS trust or an exact leaf certificate pin on every TLS handshake.
        /// </summary>
        /// <param name="serverCertificate">
        /// An exact TLS leaf certificate pin, or an empty value to require OS TLS trust.
        /// </param>
        /// <remarks>
        /// Hostname and certificate validity checks always apply. This policy authenticates the TLS terminator, not the
        /// OPC UA application. Configure it before sending any request; it cannot then be changed.
        /// Implementations must reject transports that cannot enforce this policy on replacement connections.
        /// </remarks>
        void RequireServerCertificateValidation(ByteString serverCertificate);
    }
}
