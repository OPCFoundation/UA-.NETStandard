/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
