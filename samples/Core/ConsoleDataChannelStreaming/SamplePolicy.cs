/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Explicitly admits one resource, authenticated application/key pair and complete descriptor.
    /// The optional Session grant adds authorization; it never replaces this independent peer policy.
    /// </summary>
    internal sealed class SamplePolicy(
        SampleOptions options,
        DataChannelParametersDataType parameters,
        string peerApplicationUri,
        ByteString peerKey) : IDataChannelAuthorizer, IDataChannelSourcePolicy, IWebTransportConnectAuthorizer
    {
        /// <inheritdoc/>
        public ValueTask<DataChannelAuthorization> AuthorizeAsync(
            DataChannelAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool admitted = request.IsLocalOpener &&
                request.Mode == (options.SessionAuthorization
                    ? DataChannelAuthorizationMode.Session : DataChannelAuthorizationMode.Configured) &&
                request.Context.Carrier == options.Carrier &&
                IsPeer(request.Context.Peer) && IsDescriptor(request.Parameters);
            return ValueTask.FromResult(admitted
                ? new DataChannelAuthorization(DataChannelResult.Good, request.Parameters)
                : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
        }

        /// <inheritdoc/>
        public ValueTask<DataChannelResult> AuthorizeAsync(
            DataChannelSourceAccess access,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool session = options.SessionAuthorization
                ? access.Session?.ClientApplicationUri == peerApplicationUri &&
                    access.Session.Identity.TokenType == UserTokenType.Certificate
                : access.Session == null;
            bool peer = access.Context != null
                ? access.Context.Carrier == options.Carrier && IsPeer(access.Context.Peer)
                : options.SessionAuthorization && session;
            return ValueTask.FromResult(session && peer && IsDescriptor(access.Parameters)
                ? access.Source.Validate(access.Parameters) : DataChannelResult.NotAuthorized);
        }

        /// <inheritdoc/>
        public ValueTask<bool> AuthorizeAsync(
            DataChannelPeerIdentity identity,
            Uri configuredEndpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(IsPeer(identity) &&
                configuredEndpoint.Scheme == "opc.wt" &&
                configuredEndpoint.Host == "127.0.0.1" &&
                configuredEndpoint.Port > 0 &&
                configuredEndpoint.AbsolutePath == "/flow/v1" &&
                configuredEndpoint.Query.Length == 0);
        }

        /// <summary>
        /// Checks actual carrier-authenticated identity, not identity claims from a payload.
        /// </summary>
        private bool IsPeer(DataChannelPeerIdentity identity)
        {
            return identity.PeerApplicationUri == peerApplicationUri &&
                identity.PeerKeyThumbprint == peerKey &&
                identity.Protection == DataChannelProtectionMode.Confidential;
        }

        /// <summary>
        /// Admits only the selected workload and its demonstrated live priority revision.
        /// </summary>
        private bool IsDescriptor(DataChannelParametersDataType proposed)
        {
            return proposed.ResourceUri == parameters.ResourceUri &&
                proposed.Protocol == DataChannelProfiles.OpaquePayload &&
                proposed.ContentType == parameters.ContentType &&
                proposed.Direction == parameters.Direction &&
                proposed.Delivery == DataChannelDeliveryMode.Ordered &&
                proposed.Protection == DataChannelProtectionMode.Confidential &&
                proposed.MaxMessageSize == parameters.MaxMessageSize &&
                proposed.MaxReceiveBytes == parameters.MaxReceiveBytes &&
                proposed.MaxRate == 0 && !proposed.AllowQueueExpiry &&
                proposed.Priority is 1 or 2;
        }
    }
}
