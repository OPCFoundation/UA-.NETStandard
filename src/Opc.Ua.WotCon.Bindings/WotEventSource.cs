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
using System.Security.Cryptography;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Authenticated source facts captured by a native binding executor.
    /// The owning channel retains the session binding; a provenance record
    /// does not expose its dispatch capability or transfer its ownership.
    /// </summary>
    public sealed class WotEventSource
    {
        internal WotEventSource(ISessionBinding binding)
        {
            m_binding = binding;
            EndpointDescription endpoint = binding.Endpoint;
            ServerUri = endpoint.Server.ApplicationUri ?? string.Empty;
            SessionId = binding.SessionId;
            SecurityMode = endpoint.SecurityMode;
            SecurityPolicyUri = endpoint.SecurityPolicyUri ?? string.Empty;
            CertificateDigest = Digest(endpoint.ServerCertificate);
            Incarnation = Guid.NewGuid();
            Validate();
        }

        /// <summary>
        /// Gets the captured source server's application URI.
        /// </summary>
        public string ServerUri { get; }

        /// <summary>
        /// Gets the captured source Session identity, not its authentication token.
        /// </summary>
        public NodeId SessionId { get; }

        /// <summary>
        /// Gets the captured channel security mode.
        /// </summary>
        public MessageSecurityMode SecurityMode { get; }

        /// <summary>
        /// Gets the captured channel security policy.
        /// </summary>
        public string SecurityPolicyUri { get; }

        /// <summary>
        /// Gets the SHA-256 digest of the authenticated source certificate.
        /// </summary>
        public ByteString CertificateDigest { get; }

        /// <summary>
        /// Gets the identity of this retained source binding.
        /// </summary>
        public Guid Incarnation { get; }

        /// <summary>
        /// Gets whether this source has integrity-protected authenticated
        /// server identity suitable for transparent event admission.
        /// </summary>
        public bool IsAuthenticated =>
            SecurityMode is MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt &&
            !CertificateDigest.IsEmpty &&
            Uri.TryCreate(ServerUri, UriKind.Absolute, out _);

        /// <summary>
        /// Gets whether the captured source binding is still current.
        /// </summary>
        public bool IsCurrent => !m_binding.Disposed && m_binding.IsCurrent;

        internal IServiceMessageContext Context => m_binding.MessageContext;

        internal ISessionClient Client => m_binding;

        internal void Validate()
        {
            if (!IsCurrent)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The captured event source binding is no longer current.");
            }
        }

        internal bool HasSameAuthority(WotEventSource other)
        {
            return ServerUri == other.ServerUri && CertificateDigest == other.CertificateDigest;
        }

        internal void DisposeBinding()
        {
            m_binding.Dispose();
        }

        private static ByteString Digest(ByteString certificate)
        {
            if (certificate.IsEmpty)
            {
                return ByteString.Empty;
            }
#if NET6_0_OR_GREATER
            return ByteString.From(SHA256.HashData(certificate.Span));
#else
            using var sha256 = SHA256.Create();
            return ByteString.From(sha256.ComputeHash(certificate.Span.ToArray()));
#endif
        }

        private readonly ISessionBinding m_binding;
    }
}
