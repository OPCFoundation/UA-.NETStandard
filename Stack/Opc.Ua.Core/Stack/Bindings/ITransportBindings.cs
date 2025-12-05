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
using System.Reflection;
using Opc.Ua.Security.Certificates;

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
    /// The interface to manage transport bindings.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ITransportBindings<T>
    {
        /// <summary>
        /// Get a transport binding for a uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        T GetBinding(string uriScheme, ITelemetryContext telemetry);

        /// <summary>
        /// Return if there is a transport listener for a uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme.</param>
        bool HasBinding(string uriScheme);

        /// <summary>
        /// Set the transport factory to the binding.
        /// Overrides other bindings with the same uri scheme.
        /// </summary>
        void SetBinding(T binding);

        /// <summary>
        /// Add all bindings with interface exported from a assembly.
        /// </summary>
        /// <param name="assembly">The assembly with the bindings.</param>
        IEnumerable<Type> AddBindings(Assembly assembly);

        /// <summary>
        /// Add all bindings with interface from a list.
        /// </summary>
        /// <param name="bindings">The array of binding types with interface.</param>
        IEnumerable<Type> AddBindings(IEnumerable<Type> bindings);
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
        /// <param name="instanceCertificateTypesProvider">The provider for application certificates.</param>
        List<EndpointDescription> CreateServiceHost(
            ServerBase serverBase,
            IDictionary<string, ServiceHost> hosts,
            ApplicationConfiguration configuration,
            IList<string> baseAddresses,
            ApplicationDescription serverDescription,
            List<ServerSecurityPolicy> securityPolicies,
            CertificateTypesProvider instanceCertificateTypesProvider);
    }

    /// <summary>
    /// This is the transport channel factory interface for a binding (client).
    /// </summary>
    public interface ITransportChannelFactory : ITransportBindingFactory<ITransportChannel>;
}
