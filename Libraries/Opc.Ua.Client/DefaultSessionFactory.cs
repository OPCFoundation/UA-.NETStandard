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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Object that creates instances of an Opc.Ua.Client.Session object
    /// </summary>
    public class DefaultSessionFactory : ISessionFactory
    {
        #region Public Methods
        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The identity.</param>
        /// <param name="preferredLocales">The user identity to associate with the session.</param>
        /// <returns>The new session object</returns>
        public Task<ISession> Create(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            return Create(configuration, endpoint, updateBeforeConnect, false, sessionName, sessionTimeout, identity, preferredLocales);
        }

        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <returns>The new session object.</returns>
        public Task<ISession> Create(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            return Create(configuration, null, endpoint, updateBeforeConnect, checkDomain, sessionName, sessionTimeout, identity, preferredLocales);
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connection.
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <returns>The new session object.</returns>
        public async Task<ISession> Create(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
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

            // create the session object.
            ISession session = new Session(channel, configuration, endpoint, null);

            // create the session.
            try
            {
                session.Open(sessionName, sessionTimeout, identity, preferredLocales, checkDomain);
            }
            catch (Exception)
            {
                session.Dispose();
                throw;
            }

            return session;
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connect manager.
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="reverseConnectManager">The reverse connect manager for the client connection.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="userIdentity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        public async Task<ISession> Create(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity userIdentity,
            IList<string> preferredLocales,
            CancellationToken ct = default(CancellationToken)
            )
        {
            if (reverseConnectManager == null)
            {
                return await Create(configuration, endpoint, updateBeforeConnect,
                    checkDomain, sessionName, sessionTimeout, userIdentity, preferredLocales).ConfigureAwait(false);
            }

            ITransportWaitingConnection connection = null;
            do
            {
                connection = await reverseConnectManager.WaitForConnection(
                    endpoint.EndpointUrl,
                    endpoint.ReverseConnect?.ServerUri,
                    ct).ConfigureAwait(false);

                if (updateBeforeConnect)
                {
                    await endpoint.UpdateFromServerAsync(
                        endpoint.EndpointUrl, connection,
                        endpoint.Description.SecurityMode,
                        endpoint.Description.SecurityPolicyUri).ConfigureAwait(false);
                    updateBeforeConnect = false;
                    connection = null;
                }
            } while (connection == null);

            return await Create(
                configuration,
                connection,
                endpoint,
                false,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales).ConfigureAwait(false);
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="sesionTemplate">The Session object to use as template</param>
        /// <returns>The new session object.</returns>
        public ISession Recreate(ISession sesionTemplate)
        {
            if (sesionTemplate is not Session template)
                throw new ArgumentOutOfRangeException("The ISession provided is not of a supported type");

            var messageContext = template.Configuration.CreateMessageContext();
            messageContext.Factory = template.Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                template.Configuration,
                template.ConfiguredEndpoint.Description,
                template.ConfiguredEndpoint.Configuration,
                template.InstanceCertificate,
                template.Configuration.SecurityConfiguration.SendCertificateChain ?
                    template.InstanceCertificateChain : null,
                messageContext);

            // create the session object.
            ISession session = new Session(channel, template, true);

            try
            {
                // open the session.
                session.Open(
                    template.SessionName,
                    (uint)template.SessionTimeout,
                    template.Identity,
                    template.PreferredLocales,
                    template.CheckDomain);

                // try transfer
                if (!session.TransferSubscriptions(new SubscriptionCollection(session.Subscriptions), false))
                {
                    // if transfer failed, create the subscriptions.
                    foreach (Subscription subscription in session.Subscriptions)
                    {
                        subscription.Create();
                    }
                }
            }
            catch (Exception e)
            {
                session.Dispose();
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "Could not recreate session. {0}", template.SessionName);
            }

            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="sesionTemplate">The Session object to use as template</param>
        /// <param name="connection">The waiting reverse connection.</param>
        /// <returns>The new session object.</returns>
        public ISession Recreate(ISession sesionTemplate, ITransportWaitingConnection connection)
        {
            if (sesionTemplate is not Session template)
                throw new ArgumentOutOfRangeException("The ISession provided is not of a supported type");

            var messageContext = template.Configuration.CreateMessageContext();
            messageContext.Factory = template.Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                template.Configuration,
                connection,
                template.ConfiguredEndpoint.Description,
                template.ConfiguredEndpoint.Configuration,
                template.InstanceCertificate,
                template.Configuration.SecurityConfiguration.SendCertificateChain ?
                    template.InstanceCertificateChain : null,
                messageContext);

            // create the session object.
            ISession session = new Session(channel, template, true);

            try
            {
                // open the session.
                session.Open(
                    template.SessionName,
                    (uint)template.SessionTimeout,
                    template.Identity,
                    template.PreferredLocales,
                    template.CheckDomain);

                // try transfer
                if (!session.TransferSubscriptions(new SubscriptionCollection(session.Subscriptions), false))
                {
                    // if transfer failed, create the subscriptions.
                    foreach (Subscription subscription in session.Subscriptions)
                    {
                        subscription.Create();
                    }
                }
            }
            catch (Exception e)
            {
                session.Dispose();
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "Could not recreate session. {0}", template.SessionName);
            }

            return session;
        }

        /// <summary>
        /// Load certificate for connection.
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
