/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
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
    public interface ITransportBindingFactory<T>
        : ITransportBindingScheme
    {
        /// <summary>
        /// The factory to create a new transport.
        /// </summary>
        /// <returns>The transport.</returns>
        T Create();
    }

    /// <summary>
    /// The interface to manage transport bindings.
    /// </summary>
    public interface ITransportBindings<T>
    {
        /// <summary>
        /// Get a transport binding for a uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme.</param>
        T GetBinding(string uriScheme);

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
    public interface ITransportListenerFactory :
        ITransportBindingFactory<ITransportListener>
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
        /// <param name="instanceCertificate">The server certificate.</param>
        /// <param name="instanceCertificateChain">The cert cahin of the server certificate.</param>
        List<EndpointDescription> CreateServiceHost(
            ServerBase serverBase,
            IDictionary<string, ServiceHost> hosts,
            ApplicationConfiguration configuration,
            IList<string> baseAddresses,
            ApplicationDescription serverDescription,
            List<ServerSecurityPolicy> securityPolicies,
            X509Certificate2 instanceCertificate,
            X509Certificate2Collection instanceCertificateChain
            );
    }

    /// <summary>
    /// This is the transport channel factory interface for a binding (client).
    /// </summary>
    public interface ITransportChannelFactory :
        ITransportBindingFactory<ITransportChannel>
    {
    }

}
