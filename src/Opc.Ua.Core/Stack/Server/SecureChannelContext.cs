// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Net;

namespace Opc.Ua
{
    /// <summary>
    /// Stores context information for the current secure channel.
    /// </summary>
    public class SecureChannelContext
    {
        /// <summary>
        /// Initializes a new instance with the specified property values.
        /// </summary>
        /// <param name="secureChannelId">The secure channel identifier.</param>
        /// <param name="endpointDescription">The endpoint description.</param>
        /// <param name="messageEncoding">The message encoding.</param>
        /// <param name="clientChannelCertificate">The client certificate used to establish the secure channel.</param>
        /// <param name="serverChannelCertificate">The server certificate used to establish the secure channel.</param>
        /// <param name="channelThumbprint">The unique hash for the secure channel calculated during channel creation.</param>
        /// <param name="peerAddress">The observed network address of the peer, when available.</param>
        public SecureChannelContext(
            string secureChannelId,
            EndpointDescription? endpointDescription,
            RequestEncoding messageEncoding,
            byte[]? clientChannelCertificate = null,
            byte[]? serverChannelCertificate = null,
            byte[]? channelThumbprint = null,
            IPAddress? peerAddress = null)
        {
            SecureChannelId = secureChannelId;
            EndpointDescription = endpointDescription;
            MessageEncoding = messageEncoding;
            // Wrapped, not copied: the transport passes buffers it owns for the life of
            // the channel, and ByteString hands out read-only views only.
            ClientChannelCertificate = clientChannelCertificate == null ? default : new ByteString(clientChannelCertificate);
            ServerChannelCertificate = serverChannelCertificate == null ? default : new ByteString(serverChannelCertificate);
            ChannelThumbprint = channelThumbprint == null ? default : new ByteString(channelThumbprint);
            PeerAddress = peerAddress;
        }

        /// <summary>
        /// TThe unique identifier for the secure channel.
        /// </summary>
        /// <value>The secure channel identifier.</value>
        public string SecureChannelId { get; }

        /// <summary>
        /// The description of the endpoint used with the channel.
        /// May be <c>null</c> for discovery requests on listeners that match endpoints lazily
        /// (HTTPS listener for GetEndpoints/FindServers/FindServersOnNetwork without a security
        /// policy header), in which case the endpoint is not known at channel-context creation time.
        /// </summary>
        /// <value>The endpoint description.</value>
        public EndpointDescription? EndpointDescription { get; }

        /// <summary>
        /// The encoding used with the channel.
        /// </summary>
        /// <value>The message encoding.</value>
        public RequestEncoding MessageEncoding { get; }

        /// <summary>
        /// The unique hash for the secure channel calculated during channel creation.
        /// </summary>
        /// <remarks>
        /// Read-only: the transport shares one buffer with every request of the channel.
        /// <see cref="ByteString.IsNull"/> when the channel has no thumbprint.
        /// </remarks>
        public ByteString ChannelThumbprint { get; }

        /// <summary>
        /// The DER encoded client certificate used to establish the secure channel.
        /// </summary>
        /// <remarks>
        /// Read-only: the transport shares one buffer with every request of the channel,
        /// so request handlers and resource-isolation classifiers cannot alter the evidence
        /// later requests are checked against. <see cref="ByteString.IsNull"/> when the
        /// channel is not secured with a client certificate.
        /// </remarks>
        public ByteString ClientChannelCertificate { get; }

        /// <summary>
        /// The DER encoded server certificate used to establish the secure channel.
        /// </summary>
        /// <remarks>
        /// Read-only, see <see cref="ClientChannelCertificate"/>.
        /// </remarks>
        public ByteString ServerChannelCertificate { get; }

        /// <summary>
        /// The observed network address of the peer, when the transport exposes one.
        /// </summary>
        public IPAddress? PeerAddress { get; }

        /// <summary>
        /// Optional upstream user identity established by an outer
        /// transport-level authentication step (e.g. the JWT bearer
        /// validator on the WebApi binding, or an mTLS client-cert
        /// handshake). When set, the OPC UA service pipeline can use
        /// this as the trusted ambient principal for sessionless
        /// requests and cross-check it against a session's body-level
        /// <c>UserIdentityToken</c>. <see langword="null"/> for
        /// transports that do not carry an upstream identity (binary
        /// UASC, plain HTTPS-JSON without a registered auth scheme).
        /// </summary>
        /// <remarks>
        /// The setter is exposed so transport handlers can publish the
        /// identity after constructing the context (the value is
        /// typically not known until after authentication completes).
        /// Application code should treat the context as immutable after
        /// the transport's dispatch path returns.
        /// </remarks>
        public IUserIdentity? UpstreamIdentity { get; set; }
    }

    /// <summary>
    /// The message encoding used with a request.
    /// </summary>
    public enum RequestEncoding
    {
        /// <summary>
        /// The request used the UA binary encoding.
        /// </summary>
        Binary,

        /// <summary>
        /// The request used the UA XML encoding.
        /// </summary>
        Xml,

        /// <summary>
        /// The request used the UA JSON encoding.
        /// </summary>
        Json
    }
}
