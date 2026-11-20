/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
