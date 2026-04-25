// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A factory that creates transport channels.
    /// </summary>
    internal interface IChannelFactory
    {
        /// <summary>
        /// Creates a new transport channel for the specified endpoint.
        /// </summary>
        /// <param name="endpoint">The configured endpoint.</param>
        /// <param name="messageContext">The message context.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <returns>The transport channel.</returns>
        ITransportChannel CreateChannel(ConfiguredEndpoint endpoint,
            IServiceMessageContext messageContext,
            X509Certificate2? clientCertificate,
            X509Certificate2Collection? clientCertificateChain);
    }
}
