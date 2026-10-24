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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Distinguishes admission, staged revision authorization and continuing permission.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public enum DataChannelAuthorizationPurpose
    {
        /// <summary>
        /// Redeem a fresh grant or check configured policy before readiness.
        /// </summary>
        Open,

        /// <summary>
        /// Authorize a complete configured or Session-staged revision proposal.
        /// </summary>
        Update,

        /// <summary>
        /// Re-evaluate continuing permission without redeeming the grant again.
        /// </summary>
        Recheck
    }

    /// <summary>
    /// A confirmed context binding, with both peer challenges and verified carrier identities.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelContextIdentity
    {
        /// <summary>
        /// Creates a confirmed immutable context snapshot.
        /// </summary>
        internal DataChannelContextIdentity(
            Guid contextId,
            ulong generation,
            DataChannelPeerIdentity peer,
            ByteString localChallenge,
            ByteString peerChallenge,
            DataChannelCarrierKind carrier)
        {
            ContextId = contextId;
            Generation = generation;
            Peer = peer;
            LocalChallenge = localChallenge.Copy();
            PeerChallenge = peerChallenge.Copy();
            Carrier = carrier;
        }

        /// <summary>
        /// Gets the fresh initiator-selected identifier.
        /// </summary>
        public Guid ContextId { get; }

        /// <summary>
        /// Gets the nonzero context generation.
        /// </summary>
        public ulong Generation { get; }

        /// <summary>
        /// Gets authenticated application/key evidence.
        /// </summary>
        public DataChannelPeerIdentity Peer { get; }

        /// <summary>
        /// Gets the local peer's fresh binding challenge.
        /// </summary>
        public ByteString LocalChallenge { get; }

        /// <summary>
        /// Gets the remote peer's independently generated challenge.
        /// </summary>
        public ByteString PeerChallenge { get; }

        /// <summary>
        /// Gets the confirmed carrier.
        /// </summary>
        public DataChannelCarrierKind Carrier { get; }
    }

    /// <summary>
    /// Inputs for resource-specific admission on either peer. No Session or Server runtime is required.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelAuthorizationRequest
    {
        /// <summary>
        /// Creates a snapshot passed to an injected authorization provider.
        /// </summary>
        internal DataChannelAuthorizationRequest(
            DataChannelContextIdentity context,
            uint channelId,
            bool isLocalOpener,
            DataChannelAuthorizationMode mode,
            Guid? grantId,
            DataChannelAuthorizationPurpose purpose,
            uint revision,
            uint operationId,
            DataChannelParametersDataType parameters)
        {
            Context = context;
            ChannelId = channelId;
            IsLocalOpener = isLocalOpener;
            Mode = mode;
            GrantId = grantId;
            Purpose = purpose;
            Revision = revision;
            OperationId = operationId;
            m_parameters = FlowModel.Copy(parameters);
        }

        /// <summary>
        /// Gets the actual confirmed security context.
        /// </summary>
        public DataChannelContextIdentity Context { get; }

        /// <summary>
        /// Gets the fresh or existing flow identifier.
        /// </summary>
        public uint ChannelId { get; }

        /// <summary>
        /// Gets the opener role independently of transport and payload roles.
        /// </summary>
        public bool IsLocalOpener { get; }

        /// <summary>
        /// Gets the requested authorization route.
        /// </summary>
        public DataChannelAuthorizationMode Mode { get; }

        /// <summary>
        /// Gets the untrusted grant identifier, not proof of permission.
        /// </summary>
        public Guid? GrantId { get; }

        /// <summary>
        /// Gets whether admission, a staged update or continuing permission is being checked.
        /// </summary>
        public DataChannelAuthorizationPurpose Purpose { get; }

        /// <summary>
        /// Gets the proposed or current revision.
        /// </summary>
        public uint Revision { get; }

        /// <summary>
        /// Gets the opener's revision-operation identifier, or zero outside a revision.
        /// </summary>
        public uint OperationId { get; }

        /// <summary>
        /// Gets a defensive copy of the complete proposal.
        /// </summary>
        public DataChannelParametersDataType Parameters => FlowModel.Copy(m_parameters);

        private readonly DataChannelParametersDataType m_parameters;
    }

    /// <summary>
    /// Resource-specific admission result. A successful result must explicitly return admitted parameters.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelAuthorization
    {
        /// <summary>
        /// Creates a result. Lifetime bounds continuing authorization, not grant-redemption time.
        /// </summary>
        public DataChannelAuthorization(
            DataChannelResult result,
            DataChannelParametersDataType? parameters = null,
            ExpandedNodeId sourceNodeId = default,
            NodeId sessionId = default,
            TimeSpan? lifetime = null,
            CancellationToken revocationToken = default)
        {
            if (result == DataChannelResult.Good && parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }
            Result = result;
            m_parameters = parameters == null ? null : FlowModel.Copy(parameters);
            SourceNodeId = sourceNodeId;
            SessionId = sessionId;
            Lifetime = lifetime;
            RevocationToken = revocationToken;
        }

        /// <summary>
        /// Gets the permission result.
        /// </summary>
        public DataChannelResult Result { get; }

        /// <summary>
        /// Gets a defensive copy of the admitted descriptor.
        /// </summary>
        public DataChannelParametersDataType? Parameters
            => m_parameters == null ? null : FlowModel.Copy(m_parameters);

        /// <summary>
        /// Gets optional model projection identity.
        /// </summary>
        public ExpandedNodeId SourceNodeId { get; }

        /// <summary>
        /// Gets the optional authorizing Session, never its authentication token.
        /// </summary>
        public NodeId SessionId { get; }

        /// <summary>
        /// Gets the validity duration of current authorization evidence. At its deadline,
        /// payload admission pauses until a live recheck renews permission or denies it.
        /// </summary>
        public TimeSpan? Lifetime { get; }

        /// <summary>
        /// Gets an immediate policy/grant revocation signal, including while OPEN is still pending.
        /// This is independent of the optional control transport's lifetime.
        /// </summary>
        public CancellationToken RevocationToken { get; }

        private readonly DataChannelParametersDataType? m_parameters;
    }

    /// <summary>
    /// Verifies configured policy or Session grants/staged operations against the live context.
    /// Implementations must deny unmatched resources and revalidate continuing permission.
    /// Loss of a control connection alone is not Session closure or authorization revocation.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public interface IDataChannelAuthorizer
    {
        /// <summary>
        /// Checks both application identity/key and resource-specific permissions.
        /// An Open grant is single-use; Recheck must not redeem it a second time.
        /// </summary>
        ValueTask<DataChannelAuthorization> AuthorizeAsync(
            DataChannelAuthorizationRequest request,
            CancellationToken cancellationToken = default);
    }
}
