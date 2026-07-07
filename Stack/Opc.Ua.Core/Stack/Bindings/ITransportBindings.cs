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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// This is an interface to the scheme of a transport binding.
    /// </summary>
    public interface ITransportBindingScheme
    {
        /// <summary>
        /// The protocol scheme supported by the binding.
        /// </summary>
        string UriScheme { get; }
    }

    /// <summary>
    /// This is an interface to the factory of a transport binding.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ITransportBindingFactory<T> : ITransportBindingScheme
    {
        /// <summary>
        /// The factory to create a new transport.
        /// </summary>
        /// <returns>The transport.</returns>
        T Create(ITelemetryContext telemetry);
    }

    /// <summary>
    /// This is the transport listener factory interface for a binding (server).
    /// </summary>
    public interface ITransportListenerFactory : ITransportBindingFactory<ITransportListener>
    {
        /// <summary>
        /// Create the service host for a server using <see cref="ServerBase"/>
        /// </summary>
        /// <param name="serverBase">The server base.</param>
        /// <param name="hosts">The service hosts are added to this list.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="baseAddresses">The base addreses for the service host.</param>
        /// <param name="serverDescription">The server description.</param>
        /// <param name="securityPolicies">The list of supported security policies.</param>
        /// <param name="serverCertificates">
        /// The registry that exposes the server's instance certificates.
        /// </param>
        /// <param name="clientCertificateValidator">
        /// The validator used by the listener to validate inbound client
        /// certificates.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<List<EndpointDescription>> CreateServiceHostAsync(
            ServerBase serverBase,
            IDictionary<string, ServiceHost> hosts,
            ApplicationConfiguration configuration,
            ArrayOf<string> baseAddresses,
            ApplicationDescription serverDescription,
            ArrayOf<ServerSecurityPolicy> securityPolicies,
            ICertificateRegistry serverCertificates,
            ICertificateValidatorEx clientCertificateValidator,
            CancellationToken ct = default);
    }

    /// <summary>
    /// This is the transport channel factory interface for a binding (client).
    /// </summary>
    public interface ITransportChannelFactory : ITransportBindingFactory<ITransportChannel>;
}
