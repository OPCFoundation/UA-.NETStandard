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
using Opc.Ua;
using Opc.Ua.Client.DataChannels;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Transports.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Restricts the server source to its pinned client application and the selected authorization route.
    /// </summary>
    internal sealed class SampleSourcePolicy(ByteString expectedPeerKey, bool sessionAuthorization)
        : IDataChannelSourcePolicy
    {
        /// <inheritdoc/>
        public ValueTask<DataChannelResult> AuthorizeAsync(
            DataChannelSourceAccess access,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool callerAllowed = sessionAuthorization
                ? access.Session?.ClientApplicationUri == SampleConfiguration.ClientApplicationUri &&
                    access.Session.Identity.TokenType == UserTokenType.Certificate
                : access.Session == null;
            bool peerAllowed = access.Context == null
                ? sessionAuthorization && callerAllowed
                : access.Context.Peer.PeerApplicationUri == SampleConfiguration.ClientApplicationUri &&
                    access.Context.Peer.PeerKeyThumbprint == expectedPeerKey &&
                    access.Context.Peer.Protection == DataChannelProtectionMode.Confidential;
            return ValueTask.FromResult(callerAllowed && peerAllowed
                ? access.Source.Validate(access.Parameters)
                : DataChannelResult.NotAuthorized);
        }
    }

    /// <summary>
    /// Independently restricts the client to the selected source, descriptor and pinned server key.
    /// Session grant validation is additionally performed by the library's SessionDataChannelAuthorizer.
    /// </summary>
    internal sealed class SampleClientPolicy(
        SampleOptions options,
        ByteString expectedPeerKey) : IDataChannelAuthorizer
    {
        /// <inheritdoc/>
        public ValueTask<DataChannelAuthorization> AuthorizeAsync(
            DataChannelAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataChannelParametersDataType parameters = request.Parameters;
            DataChannelParametersDataType expected = SampleConfiguration.Parameters(options);
            bool allowed = request.IsLocalOpener &&
                request.Mode == (options.SessionAuthorization
                    ? DataChannelAuthorizationMode.Session
                    : DataChannelAuthorizationMode.Configured) &&
                request.Context.Peer.PeerApplicationUri == SampleConfiguration.ServerApplicationUri &&
                request.Context.Peer.PeerKeyThumbprint == expectedPeerKey &&
                request.Context.Peer.Protection == DataChannelProtectionMode.Confidential &&
                request.Context.Carrier == options.Carrier &&
                parameters.ResourceUri == expected.ResourceUri &&
                parameters.Protocol == expected.Protocol &&
                parameters.ContentType == expected.ContentType &&
                parameters.Direction == expected.Direction &&
                parameters.Delivery == expected.Delivery &&
                parameters.Protection == expected.Protection &&
                parameters.Priority == expected.Priority &&
                parameters.AllowQueueExpiry == expected.AllowQueueExpiry &&
                parameters.MaxRate == expected.MaxRate &&
                parameters.MaxMessageSize > 0 &&
                parameters.MaxMessageSize <= expected.MaxMessageSize &&
                parameters.MaxReceiveBytes > 0 &&
                parameters.MaxReceiveBytes <= expected.MaxReceiveBytes;
            return ValueTask.FromResult(allowed
                ? new DataChannelAuthorization(DataChannelResult.Good, parameters)
                : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
        }
    }

    /// <summary>
    /// Adapts the existing generated-proxy Session control helper to the PubSub grant-provider seam.
    /// </summary>
    internal sealed class SampleGrantProvider(DataChannelSessionControl control, NodeId sourceId)
        : IDataChannelPubSubGrantProvider
    {
        /// <summary>
        /// Gets the number of actual successful grant Calls, not ready flows.
        /// </summary>
        internal int Calls => Volatile.Read(ref m_calls);

        /// <inheritdoc/>
        public async ValueTask<Guid> AcquireAsync(
            DataChannelContext context,
            uint channelId,
            DataChannelParametersDataType parameters,
            CancellationToken cancellationToken = default)
        {
            DataChannelGrantDataType grant = await control.GetGrantAsync(
                context, channelId, sourceId, parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref m_calls);
            return grant.GrantId;
        }

        private int m_calls;
    }
}
