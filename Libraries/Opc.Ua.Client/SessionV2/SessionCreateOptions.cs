#if OPCUA_CLIENT_V2
/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        public ArrayOf<EndpointDescription>? AvailableEndpoints { get; init; }

        /// <summary>
        /// Discovery profile uris that were returned in the initial
        /// discovery sequence.
        /// </summary>
        public ArrayOf<string>? DiscoveryProfileUris { get; init; }

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
#endif
