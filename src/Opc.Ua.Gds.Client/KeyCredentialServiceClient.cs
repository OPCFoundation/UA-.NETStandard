/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
#pragma warning disable IDE0052 // Kept to root the session supplied to the generated service proxy for this client.
        private readonly ISession m_session;
#pragma warning restore IDE0052
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
