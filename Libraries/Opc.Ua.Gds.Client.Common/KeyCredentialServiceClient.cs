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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Client surface for the OPC 10000-12 §8 KeyCredentialService.
    /// Wraps the generated <see cref="KeyCredentialServiceTypeClient"/>
    /// proxy and provides a session-connected experience.
    /// </summary>
    public sealed class KeyCredentialServiceClient
    {
        private readonly ISession m_session;
        private readonly KeyCredentialServiceTypeClient m_proxy;

        /// <summary>
        /// Creates a client targeting a specific KeyCredentialService
        /// instance on the connected session.
        /// </summary>
        /// <param name="session">A connected OPC UA session.</param>
        /// <param name="serviceNodeId">
        /// The NodeId of the KeyCredentialService object instance.
        /// </param>
        public KeyCredentialServiceClient(ISession session, NodeId serviceNodeId)
        {
            m_session = session;
            m_proxy = new KeyCredentialServiceTypeClient(
                session,
                serviceNodeId,
                session.MessageContext.Telemetry);
        }

        /// <summary>Starts a key-credential request.</summary>
        public ValueTask<NodeId> StartRequestAsync(
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            CancellationToken ct = default)
        {
            return m_proxy.StartRequestAsync(
                applicationUri,
                publicKey,
                securityPolicyUri!,
                requestedRoles,
                ct);
        }

        /// <summary>Finishes (approves or cancels) a key-credential request.</summary>
        public ValueTask<(
            string credentialId,
            ByteString credentialSecret,
            string certificateThumbprint,
            string securityPolicyUri,
            ArrayOf<NodeId> grantedRoles)> FinishRequestAsync(
            NodeId requestId,
            bool cancelRequest,
            CancellationToken ct = default)
        {
            return m_proxy.FinishRequestAsync(requestId, cancelRequest, ct);
        }

        /// <summary>Revokes a previously issued credential.</summary>
        public ValueTask RevokeAsync(
            string credentialId,
            CancellationToken ct = default)
        {
            return m_proxy.RevokeAsync(credentialId, ct);
        }
    }
}
