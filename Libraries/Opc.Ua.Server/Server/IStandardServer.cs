/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Interface of the standard server.
    /// </summary>
    public interface IStandardServer : ISessionServer
    {
        /// <summary>
        /// Begins an asynchronous publish operation.
        /// </summary>
        /// <param name="request">The request.</param>
        void BeginPublish(IEndpointIncomingRequest request);

        /// <summary>
        /// Completes an asynchronous publish operation.
        /// </summary>
        /// <param name="request">The request.</param>
        void CompletePublish(IEndpointIncomingRequest request);

        /// <summary>
        /// The state object associated with the server.
        /// It provides the shared components for the Server.
        /// </summary>
        /// <value>The current instance.</value>
        IServerInternal CurrentInstance { get; }

        /// <summary>
        /// Gets the current server state.
        /// </summary>
        ServerState CurrentState { get; }

        /// <summary>
        /// The node manager factories that are used on startup of the server.
        /// </summary>
        IEnumerable<INodeManagerFactory> NodeManagerFactories { get; }

        /// <summary>
        /// The object used to verify client certificates
        /// </summary>
        /// <value>The identifier for an X509 certificate.</value>
        CertificateValidator CertificateValidator { get; }

        /// <summary>
        /// The server's application instance certificate types provider.
        /// </summary>
        /// <value>The provider for the X.509 certificates.</value>
        CertificateTypesProvider InstanceCertificateTypesProvider { get; }

        /// <summary>
        /// Registers the server with the discovery server.
        /// </summary>
        /// <returns>Boolean value.</returns>
        bool RegisterWithDiscoveryServer();

        /// <summary>
        /// Add a node manager factory which is used on server start
        /// to instantiate the node manager in the server.
        /// </summary>
        /// <param name="nodeManagerFactory">The node manager factory used to create the NodeManager.</param>
        void AddNodeManager(INodeManagerFactory nodeManagerFactory);

        /// <summary>
        /// Remove a node manager factory from the list of node managers.
        /// Does not remove a NodeManager from a running server,
        /// only removes the factory before the server starts.
        /// </summary>
        /// <param name="nodeManagerFactory">The node manager factory to remove.</param>
        void RemoveNodeManager(INodeManagerFactory nodeManagerFactory);

        /// <summary>
        /// Raised when the status of a monitored connection changes.
        /// </summary>
        event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// Creates a new connection with a client.
        /// </summary>
        void CreateConnection(Uri url, int timeout);

        /// <summary>
        /// Create the transport listener for the service host endpoint.
        /// </summary>
        /// <param name="endpointUri">The endpoint Uri.</param>
        /// <param name="endpoints">The description of the endpoints.</param>
        /// <param name="endpointConfiguration">The configuration of the endpoints.</param>
        /// <param name="listener">The transport listener.</param>
        /// <param name="certificateValidator">The certificate validator for the transport.</param>
        void CreateServiceHostEndpoint(
            Uri endpointUri,
            EndpointDescriptionCollection endpoints,
            EndpointConfiguration endpointConfiguration,
            ITransportListener listener,
            ICertificateValidator certificateValidator
        );

        /// <summary>
        /// Returns the UserTokenPolicies supported by the server.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="description">The description.</param>
        /// <returns>
        /// Returns a collection of UserTokenPolicy objects,
        /// the return type is <seealso cref="UserTokenPolicyCollection"/> .
        /// </returns>
        UserTokenPolicyCollection GetUserTokenPolicies(ApplicationConfiguration configuration, EndpointDescription description);
    }
}
