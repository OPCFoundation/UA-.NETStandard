// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Explicitly authorizes one authenticated application pair to use a configured CONNECT endpoint.
    /// Native HTTP/3 independently enforces the exact authority/path/Origin/protocol before accepting.
    /// This hook neither authorizes a flow resource nor creates a Session.
    /// </summary>
    public interface IWebTransportConnectAuthorizer
    {
        /// <summary>
        /// Authorizes the verified identities for the configured endpoint, not unverified request headers.
        /// False emits a forbidden response and never creates a common data context.
        /// </summary>
        ValueTask<bool> AuthorizeAsync(
            DataChannelPeerIdentity identity,
            Uri endpointUrl,
            CancellationToken cancellationToken = default);
    }
}
