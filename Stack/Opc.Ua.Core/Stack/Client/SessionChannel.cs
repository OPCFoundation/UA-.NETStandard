/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    ///  A channel object used by clients to access a UA service.
    /// </summary>
    public partial class SessionChannel 
    {
        #region Constructors
        /// <summary>
        /// Creates a new transport channel that supports the ISessionChannel service contract.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <returns></returns>
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            IServiceMessageContext messageContext)
        {
            return Create(configuration, description, endpointConfiguration, clientCertificate, null, messageContext);
        }

        /// <summary>
        /// Creates a new transport channel that supports the ISessionChannel service contract.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <returns></returns>
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            IServiceMessageContext messageContext)
        {
            // create a UA binary channel.
            ITransportChannel channel = CreateUaBinaryChannel(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext);

            return channel;
        }

        /// <summary>
        /// Creates a new transport channel that supports the ISessionChannel service contract.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="connection"></param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <returns></returns>
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            IServiceMessageContext messageContext)
        {
            // create a UA binary channel.
            ITransportChannel channel = CreateUaBinaryChannel(
                configuration,
                connection,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext);

            return channel;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a secure channel to the specified endpoint.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">A configured endpoint to connect to.</param> 
        /// <param name="updateBeforeConnect">Update configuration based on server prior connect.</param>
        /// <param name="checkDomain">Check that the certificate specifies a valid domain (computer) name.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static async Task<ITransportChannel> CreateAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain)
        {
            endpoint.UpdateBeforeConnect = updateBeforeConnect;

            EndpointDescription endpointDescription = endpoint.Description;

            // create the endpoint configuration (use the application configuration to provide default values).
            EndpointConfiguration endpointConfiguration = endpoint.Configuration;

            if (endpointConfiguration == null)
            {
                endpoint.Configuration = endpointConfiguration = EndpointConfiguration.Create(configuration);
            }

            // create message context.
            IServiceMessageContext messageContext = configuration.CreateMessageContext(true);

            // update endpoint description using the discovery endpoint.
            if (endpoint.UpdateBeforeConnect && connection == null)
            {
                endpoint.UpdateFromServer();
                endpointDescription = endpoint.Description;
                endpointConfiguration = endpoint.Configuration;
            }

            // checks the domains in the certificate.
            if (checkDomain &&
                endpoint.Description.ServerCertificate != null &&
                endpoint.Description.ServerCertificate.Length > 0)
            {
                configuration.CertificateValidator?.ValidateDomains(
                    new X509Certificate2(endpoint.Description.ServerCertificate),
                    endpoint);
                checkDomain = false;
            }

            X509Certificate2 clientCertificate = null;
            X509Certificate2Collection clientCertificateChain = null;
            if (endpointDescription.SecurityPolicyUri != SecurityPolicies.None)
            {
                clientCertificate = await LoadCertificate(configuration).ConfigureAwait(false);
                clientCertificateChain = await LoadCertificateChain(configuration, clientCertificate).ConfigureAwait(false);
            }

            // initialize the channel which will be created with the server.
            ITransportChannel channel;
            if (connection != null)
            {
                channel = SessionChannel.CreateUaBinaryChannel(
                    configuration,
                    connection,
                    endpointDescription,
                    endpointConfiguration,
                    clientCertificate,
                    clientCertificateChain,
                    messageContext);
            }
            else
            {
                channel = SessionChannel.Create(
                     configuration,
                     endpointDescription,
                     endpointConfiguration,
                     clientCertificate,
                     clientCertificateChain,
                     messageContext);
            }

            return channel;
        }

        #endregion

        #region Static Methods
        /// <summary>
        /// Load certificate chain for connection.
        /// </summary>
        private static async Task<X509Certificate2> LoadCertificate(ApplicationConfiguration configuration)
        {
            X509Certificate2 clientCertificate;
            if (configuration.SecurityConfiguration.ApplicationCertificate == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate must be specified.");
            }

            clientCertificate = await configuration.SecurityConfiguration.ApplicationCertificate.Find(true).ConfigureAwait(false);

            if (clientCertificate == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate cannot be found.");
            }
            return clientCertificate;
        }

        /// <summary>
        /// Load certificate chain for connection.
        /// </summary>
        private static async Task<X509Certificate2Collection> LoadCertificateChain(ApplicationConfiguration configuration, X509Certificate2 clientCertificate)
        {
            X509Certificate2Collection clientCertificateChain = null;
            // load certificate chain.
            if (configuration.SecurityConfiguration.SendCertificateChain)
            {
                clientCertificateChain = new X509Certificate2Collection(clientCertificate);
                List<CertificateIdentifier> issuers = new List<CertificateIdentifier>();
                await configuration.CertificateValidator.GetIssuers(clientCertificate, issuers).ConfigureAwait(false);

                for (int i = 0; i < issuers.Count; i++)
                {
                    clientCertificateChain.Add(issuers[i].Certificate);
                }
            }
            return clientCertificateChain;
        }
        #endregion
    }
}
