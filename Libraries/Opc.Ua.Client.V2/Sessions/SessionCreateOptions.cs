// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using Polly;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Session creation options
    /// </summary>
    public record class SessionCreateOptions : SessionOptions
    {
        /// <summary>
        /// Available endpoints the server presented during initial
        /// discovery. Leave blank to disable any validation during
        /// session creation against the available endpoints found
        /// in discovery.
        /// </summary>
        public EndpointDescriptionCollection? AvailableEndpoints { get; init; }

        /// <summary>
        /// Discovery profile uris that were returned in the initial
        /// discovery sequence.
        /// </summary>
        public StringCollection? DiscoveryProfileUris { get; init; }

        /// <summary>
        /// An existing channel to use. This can be an open channel
        /// that was used during discovery. The session will create
        /// a new channel if the channel is not workable.
        /// </summary>
        public ITransportChannel? Channel { get; init; }

        /// <summary>
        /// Connection to use. One can pass an existing reverse
        /// connection, the session however will create one if the
        /// connection is closed or unusable.
        /// </summary>
        public ITransportWaitingConnection? Connection { get; init; }

        /// <summary>
        /// The initial user identity to use when connecting the
        /// session. The default is anonymous user.
        /// </summary>
        public IUserIdentity? Identity { get; init; }

        /// <summary>
        /// Client certificate to use. If not provided or not valid
        /// the session will load the certificate from the own
        /// store.
        /// </summary>
        public X509Certificate2? ClientCertificate { get; init; }

        /// <summary>
        /// Reconnect policy to use. The resiliency pipelines will
        /// be used to reconnect the session when the connection
        /// is determined lost. Use rate limiting to limit the
        /// number of reconnects across the entire client.
        /// </summary>
        public ResiliencePipeline? ReconnectStrategy { get; init; }
    }
}
