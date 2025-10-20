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
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// A class that provides access to a Global Discovery Server.
    /// </summary>
    public class GlobalDiscoveryServerClient : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalDiscoveryServerClient"/> class.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="adminUserIdentity">The user identity for the administrator.</param>
        /// <param name="sessionFactory">Used to create session to the server</param>
        /// <param name="diagnosticsMasks">Return diagnostics to use for all requests</param>
        public GlobalDiscoveryServerClient(
            ApplicationConfiguration configuration,
            IUserIdentity adminUserIdentity = null,
            ISessionFactory sessionFactory = null,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None)
        {
            Configuration = configuration;
            MessageContext = configuration.CreateMessageContext(true);
            m_logger = MessageContext.Telemetry.CreateLogger<GlobalDiscoveryServerClient>();
            m_sessionFactory = sessionFactory ??
                new DefaultSessionFactory(MessageContext.Telemetry)
                {
                    ReturnDiagnostics = diagnosticsMasks
                };
            // preset admin
            AdminCredentials = adminUserIdentity;
        }

        /// <summary>
        /// Gets the application.
        /// </summary>
        public ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// Message context
        /// </summary>
        public IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Gets or sets the admin credentials.
        /// </summary>
        public IUserIdentity AdminCredentials { get; set; }

        /// <summary>
        /// Gets or sets the endpoint URL.
        /// </summary>
        public string EndpointUrl => m_endpoint?.EndpointUrl.ToString();

        /// <summary>
        /// Gets or sets the preferred locales.
        /// </summary>
        public string[] PreferredLocales { get; set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Called when the client is disposed
        /// </summary>
        /// <param name="disposing"></param>
        protected void Dispose(bool disposing)
        {
            if (disposing && !m_disposed)
            {
                m_disposed = true;
                m_lock.Wait();
                try
                {
                    Session?.Dispose();
                }
                finally
                {
                    m_lock.Release();
                    m_lock.Dispose();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the session is connected.
        /// </summary>
        /// <value>
        /// <c>true</c> if the session is connected; otherwise, <c>false</c>.
        /// </value>
        public bool IsConnected
        {
            get
            {
                m_lock.Wait();
                try
                {
                    return Session != null && Session.Connected;
                }
                finally
                {
                    m_lock.Release();
                }
            }
        }

        /// <summary>
        /// Gets the session.
        /// </summary>
        public ISession Session { get; private set; }

        /// <summary>
        /// Gets the endpoint.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public ConfiguredEndpoint Endpoint
        {
            get
            {
                m_lock.Wait();
                try
                {
                    if (Session != null && Session.ConfiguredEndpoint != null)
                    {
                        return Session.ConfiguredEndpoint;
                    }
                    return m_endpoint;
                }
                finally
                {
                    m_lock.Release();
                }
            }
            set
            {
                if (IsConnected)
                {
                    throw new InvalidOperationException(
                        "Session must be closed before changing endpoint.");
                }

                if (value == null ||
                    m_endpoint == null ||
                    value.EndpointUrl != m_endpoint.EndpointUrl)
                {
                    AdminCredentials = null;
                }

                m_endpoint = value;
            }
        }

        /// <summary>
        ///  Returns list of servers known to the LDS, excluding GDS servers.
        /// </summary>
        /// <param name="lds">The LDS to use.</param>
        /// <returns>
        /// TRUE if successful; FALSE otherwise.
        /// </returns>
        [Obsolete("Use GetDefaultServerUrlsAsync instead.")]
        public List<string> GetDefaultServerUrls(LocalDiscoveryServerClient lds)
        {
            return GetDefaultServerUrlsAsync(lds).GetAwaiter().GetResult();
        }

        /// <summary>
        ///  Returns list of servers known to the LDS, excluding GDS servers.
        /// </summary>
        /// <param name="lds">The LDS to use.</param>
        /// <param name="ct"> The cancellationToken.</param>
        /// <returns>
        /// Whatever urls were found.
        /// </returns>
        public async Task<List<string>> GetDefaultServerUrlsAsync(
            LocalDiscoveryServerClient lds,
            CancellationToken ct = default)
        {
            var serverUrls = new List<string>();

            try
            {
                lds ??= new LocalDiscoveryServerClient(Configuration);

                (List<ServerOnNetwork> servers, DateTime _) = await lds.FindServersOnNetworkAsync(
                    0,
                    1000,
                    ct).ConfigureAwait(false);

                foreach (ServerOnNetwork server in servers)
                {
                    if (server.ServerCapabilities != null)
                    {
                        // ignore GDS and LDS servers
                        if (server.ServerCapabilities
                                .Contains(ServerCapability.GlobalDiscoveryServer) ||
                            server.ServerCapabilities
                                .Contains(ServerCapability.LocalDiscoveryServer))
                        {
                            continue;
                        }
                    }
                    serverUrls.Add(server.DiscoveryUrl);
                }
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Unexpected error connecting to LDS");
            }

            return serverUrls;
        }

        /// <summary>
        /// Returns list of GDS servers known to the LDS.
        /// </summary>
        /// <param name="lds">The LDS to use.</param>
        /// <returns>
        /// Whatever urls were found.
        /// </returns>
        [Obsolete("Use GetDefaultGdsUrlsAsync instead.")]
        public List<string> GetDefaultGdsUrls(LocalDiscoveryServerClient lds)
        {
            return GetDefaultGdsUrlsAsync(lds).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns list of GDS servers known to the LDS.
        /// </summary>
        /// <param name="lds">The LDS to use.</param>
        /// <param name="ct"> The cancellationToken.</param>
        /// <returns>
        /// Whatever urls were found.
        /// </returns>
        public async Task<List<string>> GetDefaultGdsUrlsAsync(
            LocalDiscoveryServerClient lds,
            CancellationToken ct = default)
        {
            var gdsUrls = new List<string>();

            try
            {
                lds ??= new LocalDiscoveryServerClient(Configuration);

                (List<ServerOnNetwork> servers, DateTime _) = await lds.FindServersOnNetworkAsync(
                    0,
                    1000,
                    ct).ConfigureAwait(false);

                foreach (ServerOnNetwork server in servers)
                {
                    if (server.ServerCapabilities != null &&
                        server.ServerCapabilities.Contains(ServerCapability.GlobalDiscoveryServer))
                    {
                        gdsUrls.Add(server.DiscoveryUrl);
                    }
                }
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Unexpected error connecting to LDS");
            }

            return gdsUrls;
        }

        /// <summary>
        /// Connects using the default endpoint.
        /// </summary>
        [Obsolete("Use ConnectAsync instead.")]
        public void Connect()
        {
            ConnectAsync(m_endpoint).Wait();
        }

        /// <summary>
        /// Connects using the default endpoint.
        /// </summary>
        public Task ConnectAsync(CancellationToken ct = default)
        {
            return ConnectAsync(m_endpoint, ct);
        }

        /// <summary>
        /// Connects the specified endpoint URL.
        /// </summary>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <exception cref="ArgumentNullException">endpointUrl</exception>
        /// <exception cref="ArgumentException">endpointUrl</exception>
        [Obsolete("Use ConnectAsync instead.")]
        public Task Connect(string endpointUrl)
        {
            return ConnectAsync(endpointUrl);
        }

        /// <summary>
        /// Connects the specified endpoint URL.
        /// </summary>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <exception cref="ArgumentNullException">endpointUrl</exception>
        /// <exception cref="ArgumentException">endpointUrl</exception>
        public async Task ConnectAsync(string endpointUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(endpointUrl))
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            if (!Uri.IsWellFormedUriString(endpointUrl, UriKind.Absolute))
            {
                throw new ArgumentException(
                    endpointUrl + " is not a valid URL.",
                    nameof(endpointUrl));
            }

            const int maxAttempts = 5;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    EndpointDescription endpointDescription =
                        await CoreClientUtils.SelectEndpointAsync(
                            Configuration,
                            endpointUrl,
                            true,
                            MessageContext.Telemetry,
                            ct).ConfigureAwait(false);
                    var endpointConfiguration = EndpointConfiguration.Create(Configuration);
                    var endpoint = new ConfiguredEndpoint(
                        null,
                        endpointDescription,
                        endpointConfiguration);

                    await ConnectInternalAsync(endpoint, false, ct).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException e) when ((e.StatusCode is
                    StatusCodes.BadServerHalted or
                    StatusCodes.BadSecureChannelClosed or
                    StatusCodes.BadNoCommunication) &&
                    attempt < maxAttempts)
                {
                    m_logger.LogError(e, "Failed to connect {Attempt}. Retrying in 1 second...", attempt + 1);
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Connects the specified endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        [Obsolete("Use ConnectAsync instead.")]
        public Task Connect(ConfiguredEndpoint endpoint)
        {
            return ConnectAsync(endpoint);
        }

        /// <summary>
        /// Connects the specified endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        public async Task ConnectAsync(ConfiguredEndpoint endpoint, CancellationToken ct = default)
        {
            if (endpoint != null &&
                m_endpoint != null &&
                endpoint.EndpointUrl != m_endpoint.EndpointUrl)
            {
                AdminCredentials = null;
            }

            if (endpoint == null)
            {
                endpoint = m_endpoint;

                if (endpoint == null)
                {
                    throw new ArgumentNullException(nameof(endpoint));
                }
            }

            const int maxAttempts = 5;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await ConnectInternalAsync(endpoint, true, ct).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException e) when ((e.StatusCode is
                    StatusCodes.BadServerHalted or
                    StatusCodes.BadSecureChannelClosed or
                    StatusCodes.BadNoCommunication) &&
                    attempt < maxAttempts)
                {
                    m_logger.LogError(e, "Failed to connect {Attempt}. Retrying in 1 second...", attempt + 1);
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Disconnect the client connection.
        /// </summary>
        [Obsolete("Use DisconnectAsync instead.")]
        public void Disconnect()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Disconnect the client connection.
        /// </summary>
        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ISession session = Session;
                Session = null;

                try
                {
                    KeepAlive?.Invoke(session, null);
                    await session.CloseAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    session?.Dispose();
                }
            }
            finally
            {
                m_lock.Release();
            }
        }

        private async void Session_KeepAliveAsync(ISession session, KeepAliveEventArgs e)
        {
            if (ServiceResult.IsBad(e.Status) && !m_disposed)
            {
                await m_lock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (session == Session)
                    {
                        Session.Dispose();
                        Session = null;
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Error during KeepAlive handling.");
                }
                finally
                {
                    m_lock.Release();
                }
            }
        }

        private void Session_SessionClosing(object sender, EventArgs e)
        {
            m_logger.LogInformation("The GDS Client session is closing.");
        }

        /// <summary>
        /// Occurs when keep alive occurs.
        /// </summary>
        public event KeepAliveEventHandler KeepAlive;

        /// <summary>
        /// Occurs when the server status changes.
        /// </summary>
        public event MonitoredItemNotificationEventHandler ServerStatusChanged;

        /// <summary>
        /// Finds the applications with the specified application uri.
        /// </summary>
        /// <param name="applicationUri">The application URI.</param>
        /// <returns>The matching application.</returns>
        [Obsolete("Use FindApplicationAsync instead.")]
        public ApplicationRecordDataType[] FindApplication(string applicationUri)
        {
            return FindApplicationAsync(applicationUri).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the applications with the specified application uri.
        /// </summary>
        /// <param name="applicationUri">The application URI.</param>
        /// <param name="ct"> The cancellationToken.</param>
        /// <returns>The matching application.</returns>
        public async Task<ApplicationRecordDataType[]> FindApplicationAsync(
            string applicationUri,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_FindApplications,
                    session.NamespaceUris),
                ct,
                applicationUri).ConfigureAwait(false);

            ApplicationRecordDataType[] applications = null;

            if (outputArguments.Count > 0)
            {
                applications = (ApplicationRecordDataType[])
                    ExtensionObject.ToArray(
                        outputArguments[0] as ExtensionObject[],
                        typeof(ApplicationRecordDataType));
            }

            return applications;
        }

        /// <summary>
        /// Queries the GDS for any servers matching the criteria.
        /// </summary>
        /// <param name="maxRecordsToReturn">The max records to return.</param>
        /// <param name="applicationName">The filter applied to the application name.</param>
        /// <param name="applicationUri">The filter applied to the application uri.</param>
        /// <param name="productUri">The filter applied to the product uri.</param>
        /// <param name="serverCapabilities">The filter applied to the server capabilities.</param>
        /// <returns>A enumarator used to access the results.</returns>
        [Obsolete("Use QueryServersAsync instead.")]
        public IList<ServerOnNetwork> QueryServers(
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities)
        {
            return QueryServersAsync(
                0,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities).GetAwaiter().GetResult().servers;
        }

        /// <summary>
        /// Queries the GDS for any servers matching the criteria.
        /// </summary>
        /// <param name="maxRecordsToReturn">The max records to return.</param>
        /// <param name="applicationName">The filter applied to the application name.</param>
        /// <param name="applicationUri">The filter applied to the application uri.</param>
        /// <param name="productUri">The filter applied to the product uri.</param>
        /// <param name="serverCapabilities">The filter applied to the server capabilities.</param>
        /// <param name="ct"> The cancellationToken.</param>
        /// <returns>A enumarator used to access the results.</returns>
        public async Task<IList<ServerOnNetwork>> QueryServersAsync(
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities,
            CancellationToken ct = default)
        {
            return (await QueryServersAsync(
                0,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities,
                ct).ConfigureAwait(false)).servers;
        }

        /// <summary>
        /// Queries the GDS for any servers matching the criteria.
        /// </summary>
        /// <param name="startingRecordId">The id of the first record to return.</param>
        /// <param name="maxRecordsToReturn">The max records to return.</param>
        /// <param name="applicationName">The filter applied to the application name.</param>
        /// <param name="applicationUri">The filter applied to the application uri.</param>
        /// <param name="productUri">The filter applied to the product uri.</param>
        /// <param name="serverCapabilities">The filter applied to the server capabilities.</param>
        /// <returns>A enumerator used to access the results.</returns>
        [Obsolete("Use QueryServersAsync instead.")]
        public IList<ServerOnNetwork> QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities)
        {
            (IList<ServerOnNetwork> servers, _) = QueryServersAsync(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities)
                .GetAwaiter().GetResult();

            return servers;
        }

        /// <summary>
        /// Queries the GDS for any servers matching the criteria.
        /// </summary>
        /// <param name="startingRecordId">The id of the first record to return.</param>
        /// <param name="maxRecordsToReturn">The max records to return.</param>
        /// <param name="applicationName">The filter applied to the application name.</param>
        /// <param name="applicationUri">The filter applied to the application uri.</param>
        /// <param name="productUri">The filter applied to the product uri.</param>
        /// <param name="serverCapabilities">The filter applied to the server capabilities.</param>
        /// <param name="lastCounterResetTime">The time when the counter was last changed.</param>
        /// <returns>A enumerator used to access the results.</returns>
        [Obsolete("Use QueryServersAsync instead.")]
        public IList<ServerOnNetwork> QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities,
            out DateTime lastCounterResetTime)
        {
            (IList<ServerOnNetwork> servers, lastCounterResetTime) = QueryServersAsync(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities)
                .GetAwaiter().GetResult();

            return servers;
        }

        /// <summary>
        /// Queries the GDS for any servers matching the criteria.
        /// </summary>
        /// <param name="startingRecordId">The id of the first record to return.</param>
        /// <param name="maxRecordsToReturn">The max records to return.</param>
        /// <param name="applicationName">The filter applied to the application name.</param>
        /// <param name="applicationUri">The filter applied to the application uri.</param>
        /// <param name="productUri">The filter applied to the product uri.</param>
        /// <param name="serverCapabilities">The filter applied to the server capabilities.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>A enumerator used to access the results.
        /// The time when the counter was last changed.</returns>
        public async Task<(IList<ServerOnNetwork> servers, DateTime lastCounterResetTime)> QueryServersAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities,
            CancellationToken ct = default)
        {
            DateTime lastCounterResetTime = DateTime.MinValue;

            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(MethodIds.Directory_QueryServers, session.NamespaceUris),
                ct,
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities).ConfigureAwait(false);

            ServerOnNetwork[] servers = null;

            if (outputArguments.Count >= 2)
            {
                lastCounterResetTime = (DateTime)outputArguments[0];
                servers = (ServerOnNetwork[])
                    ExtensionObject.ToArray(
                        outputArguments[1] as ExtensionObject[],
                        typeof(ServerOnNetwork));
            }

            return (servers, lastCounterResetTime);
        }

        /// <summary>
        /// Queries the GDS for any servers matching the criteria.
        /// </summary>
        /// <param name="startingRecordId">The id of the first record to return.</param>
        /// <param name="maxRecordsToReturn">The max records to return.</param>
        /// <param name="applicationName">The filter applied to the application name.</param>
        /// <param name="applicationUri">The filter applied to the application uri.</param>
        /// <param name="applicationType">The filter applied to the application uri.</param>
        /// <param name="productUri">The filter applied to the product uri.</param>
        /// <param name="serverCapabilities">The filter applied to the server capabilities.</param>
        /// <param name="lastCounterResetTime">The time when the counter was last changed.</param>
        /// <param name="nextRecordId">The id of the next record.</param>
        /// <returns>A enumerator used to access the results.</returns>
        [Obsolete("Use QueryApplicationsAsync instead.")]
        public IList<ApplicationDescription> QueryApplications(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            IList<string> serverCapabilities,
            out DateTime lastCounterResetTime,
            out uint nextRecordId)
        {
            (IList<ApplicationDescription> applications, lastCounterResetTime, nextRecordId) = QueryApplicationsAsync(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                applicationType,
                productUri,
                serverCapabilities)
                .GetAwaiter().GetResult();

            return applications;
        }

        /// <summary>
        /// Queries the GDS for any servers matching the criteria.
        /// </summary>
        /// <param name="startingRecordId">The id of the first record to return.</param>
        /// <param name="maxRecordsToReturn">The max records to return.</param>
        /// <param name="applicationName">The filter applied to the application name.</param>
        /// <param name="applicationUri">The filter applied to the application uri.</param>
        /// <param name="applicationType">The filter applied to the application uri.</param>
        /// <param name="productUri">The filter applied to the product uri.</param>
        /// <param name="serverCapabilities">The filter applied to the server capabilities.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>A enumerator used to access the results.
        /// The time when the counter was last changed.
        /// The id of the next record.</returns>
        public async Task<(IList<ApplicationDescription> applications, DateTime lastCounterResetTime, uint nextRecordId)>
            QueryApplicationsAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            IList<string> serverCapabilities,
            CancellationToken ct = default)
        {
            DateTime lastCounterResetTime = DateTime.MinValue;
            uint nextRecordId = 0;

            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_QueryApplications,
                    session.NamespaceUris),
                ct,
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                applicationType,
                productUri,
                serverCapabilities).ConfigureAwait(false);

            ApplicationDescription[] applications = null;

            if (outputArguments.Count >= 3)
            {
                lastCounterResetTime = (DateTime)outputArguments[0];
                nextRecordId = (uint)outputArguments[1];
                applications = (ApplicationDescription[])
                    ExtensionObject.ToArray(
                        outputArguments[2] as ExtensionObject[],
                        typeof(ApplicationDescription));
            }

            return (applications, lastCounterResetTime, nextRecordId);
        }

        /// <summary>
        /// Get the application record.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <returns>The application record for the specified application id.</returns>
        [Obsolete("Use GetApplicationAsync instead.")]
        public ApplicationRecordDataType GetApplication(NodeId applicationId)
        {
            return GetApplicationAsync(applicationId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get the application record.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>The application record for the specified application id.</returns>
        public async Task<ApplicationRecordDataType> GetApplicationAsync(
            NodeId applicationId,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(MethodIds.Directory_GetApplication, session.NamespaceUris),
                ct,
                applicationId).ConfigureAwait(false);

            if (outputArguments.Count >= 1)
            {
                return ExtensionObject.ToEncodeable(
                    outputArguments[0] as ExtensionObject) as ApplicationRecordDataType;
            }

            return null;
        }

        /// <summary>
        /// Registers the application.
        /// </summary>
        /// <param name="application">The application.</param>
        /// <returns>The application id assigned to the application.</returns>
        [Obsolete("Use RegisterApplicationAsync instead.")]
        public NodeId RegisterApplication(ApplicationRecordDataType application)
        {
            return RegisterApplicationAsync(application).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Registers the application.
        /// </summary>
        /// <param name="application">The application.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>The application id assigned to the application.</returns>
        public async Task<NodeId> RegisterApplicationAsync(
            ApplicationRecordDataType application,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_RegisterApplication,
                    session.NamespaceUris),
                ct,
                application).ConfigureAwait(false);

            if (outputArguments.Count >= 1)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Returns the Certificates assigned to Application and associated with the CertificateGroup.
        /// </summary>
        /// <param name="applicationId">The identifier assigned to the Application by the GDS.</param>
        /// <param name="certificateGroupId">An identifier for the CertificateGroup that the Certificates belong to.
        ///If null, the CertificateManager shall return the Certificates for all CertificateGroups assigned to the Application.</param>
        /// <param name="certificateTypeIds">The CertificateTypes that currently have a Certificate assigned.
        /// The length of this list is the same as the length as certificates list.</param>
        /// <param name="certificates">A list of DER encoded Certificates assigned to Application.
        /// This list only includes Certificates that are currently valid.</param>
        [Obsolete("Use GetCertificatesAsync instead")]
        public void GetCertificates(
            NodeId applicationId,
            NodeId certificateGroupId,
            out NodeId[] certificateTypeIds,
            out byte[][] certificates)
        {
            (certificateTypeIds, certificates) = GetCertificatesAsync(applicationId, certificateGroupId)
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns the Certificates assigned to Application and associated with the CertificateGroup.
        /// </summary>
        /// <param name="applicationId">The identifier assigned to the Application by the GDS.</param>
        /// <param name="certificateGroupId">An identifier for the CertificateGroup that the Certificates belong to.
        /// ///If null, the CertificateManager shall return the Certificates for all CertificateGroups assigned to the Application.</param>
        /// <param name="ct"> The cancellationToken</param>
        /// <returns>The CertificateTypes that currently have a Certificate assigned.
        /// The length of this list is the same as the length as certificates list.
        /// A list of DER encoded Certificates assigned to Application.
        /// This list only includes Certificates that are currently valid.</returns>
        public async Task<(NodeId[] certificateTypeIds, byte[][] certificates)> GetCertificatesAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken ct = default)
        {
            NodeId[] certificateTypeIds = [];
            byte[][] certificates = [];

            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.CertificateDirectoryType_GetCertificates,
                    session.NamespaceUris),
                ct,
                applicationId,
                certificateGroupId).ConfigureAwait(false);

            if (outputArguments.Count >= 2)
            {
                certificateTypeIds = outputArguments[0] as NodeId[];
                certificates = outputArguments[1] as byte[][];
            }

            return (certificateTypeIds, certificates);
        }

        /// <summary>
        /// Checks the provided certificate for validity
        /// </summary>
        /// <param name="certificate">The DER encoded form of the Certificate to check.</param>
        /// <param name="certificateStatus">The first error encountered when validating the Certificate.</param>
        /// <param name="validityTime">When the result expires and should be rechecked. DateTime.MinValue if this is unknown.</param>
        [Obsolete("Use CheckRevocationStatusAsync instead")]
        public void CheckRevocationStatus(
            byte[] certificate,
            out StatusCode certificateStatus,
            out DateTime validityTime)
        {
            (certificateStatus, validityTime) = CheckRevocationStatusAsync(certificate).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Checks the provided certificate for validity
        /// </summary>
        /// <param name="certificate">The DER encoded form of the Certificate to check.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>The first error encountered when validating the Certificate.
        /// When the result expires and should be rechecked. DateTime.MinValue if this is unknown.</returns>
        public async Task<(StatusCode certificateStatus, DateTime validityTime)> CheckRevocationStatusAsync(
            byte[] certificate,
            CancellationToken ct = default)
        {
            StatusCode certificateStatus = StatusCodes.Good;
            DateTime validityTime = DateTime.MinValue;

            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.CertificateDirectoryType_CheckRevocationStatus,
                    session.NamespaceUris
                ),
                ct,
                certificate).ConfigureAwait(false);

            if (outputArguments.Count >= 2)
            {
                certificateStatus = (StatusCode)outputArguments[0];
                validityTime = (DateTime)outputArguments[1];
            }
            return (certificateStatus, validityTime);
        }

        /// <summary>
        /// Updates the application.
        /// </summary>
        /// <param name="application">The application.</param>
        [Obsolete("Use UpdateApplicationAsync instead.")]
        public void UpdateApplication(ApplicationRecordDataType application)
        {
            UpdateApplicationAsync(application).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the application.
        /// </summary>
        /// <param name="application">The application.</param>
        /// <param name="ct">The cancellationToken</param>
        public async Task UpdateApplicationAsync(ApplicationRecordDataType application, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_UpdateApplication,
                    session.NamespaceUris),
                ct,
                application).ConfigureAwait(false);
        }

        /// <summary>
        /// Unregisters the application.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        [Obsolete("Use UnregisterApplicationAsync instead.")]
        public void UnregisterApplication(NodeId applicationId)
        {
            UnregisterApplicationAsync(applicationId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Unregisters the application.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="ct">The cancellationToken</param>
        public async Task UnregisterApplicationAsync(NodeId applicationId, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_UnregisterApplication,
                    session.NamespaceUris),
                ct,
                applicationId).ConfigureAwait(false);
        }

        /// <summary>
        /// Revokes a Certificate issued to the Application by the CertificateManager
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificate">The certificate to revoke</param>
        [Obsolete("Use RevokeCertificateAsync instead.")]
        public void RevokeCertificate(NodeId applicationId, byte[] certificate)
        {
            RevokeCertificateAsync(applicationId, certificate).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Revokes a Certificate issued to the Application by the CertificateManager
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificate">The certificate to revoke</param>
        /// <param name="ct">The cancellationToken</param>
        public async Task RevokeCertificateAsync(NodeId applicationId, byte[] certificate, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.CertificateDirectoryType_RevokeCertificate,
                    session.NamespaceUris),
                ct,
                applicationId,
                certificate).ConfigureAwait(false);
        }

        /// <summary>
        /// Requests a new certificate.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">The authority.</param>
        /// <param name="certificateTypeId">Type of the certificate.</param>
        /// <param name="subjectName">Name of the subject.</param>
        /// <param name="domainNames">The domain names.</param>
        /// <param name="privateKeyFormat">The private key format (PEM or PFX).</param>
        /// <param name="privateKeyPassword">The private key password.</param>
        /// <returns>
        /// The id for the request which is used to check when it is approved.
        /// </returns>
        [Obsolete("Use StartNewKeyPairRequestAsync instead.")]
        public NodeId StartNewKeyPairRequest(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            IList<string> domainNames,
            string privateKeyFormat,
            char[] privateKeyPassword)
        {
            return StartNewKeyPairRequestAsync(
                applicationId,
                certificateGroupId,
                certificateTypeId,
                subjectName,
                domainNames,
                privateKeyFormat,
                privateKeyPassword).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Requests a new certificate.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">The authority.</param>
        /// <param name="certificateTypeId">Type of the certificate.</param>
        /// <param name="subjectName">Name of the subject.</param>
        /// <param name="domainNames">The domain names.</param>
        /// <param name="privateKeyFormat">The private key format (PEM or PFX).</param>
        /// <param name="privateKeyPassword">The private key password.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>
        /// The id for the request which is used to check when it is approved.
        /// </returns>
        public async Task<NodeId> StartNewKeyPairRequestAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            IList<string> domainNames,
            string privateKeyFormat,
            char[] privateKeyPassword,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_StartNewKeyPairRequest,
                    session.NamespaceUris),
                ct,
                applicationId,
                certificateGroupId,
                certificateTypeId,
                subjectName,
                domainNames,
                privateKeyFormat,
                new string(privateKeyPassword)).ConfigureAwait(false);

            if (outputArguments.Count >= 1)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Signs the certificate.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">The group of the trust list.</param>
        /// <param name="certificateTypeId">The type of the trust list.</param>
        /// <param name="certificateRequest">The certificate signing request (CSR).</param>
        /// <returns>The id for the request which is used to check when it is approved.</returns>
        [Obsolete("Use StartSigningRequestAsync instead.")]
        public NodeId StartSigningRequest(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificateRequest)
        {
            return StartSigningRequestAsync(
                applicationId,
                certificateGroupId,
                certificateTypeId,
                certificateRequest).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Signs the certificate.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">The group of the trust list.</param>
        /// <param name="certificateTypeId">The type of the trust list.</param>
        /// <param name="certificateRequest">The certificate signing request (CSR).</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>The id for the request which is used to check when it is approved.</returns>
        public async Task<NodeId> StartSigningRequestAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificateRequest,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_StartSigningRequest,
                    session.NamespaceUris),
                ct,
                applicationId,
                certificateGroupId,
                certificateTypeId,
                certificateRequest).ConfigureAwait(false);

            if (outputArguments.Count >= 1)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Checks the request status.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="requestId">The request id.</param>
        /// <param name="privateKey">The private key.</param>
        /// <param name="issuerCertificates">The issuer certificates.</param>
        /// <returns>The public key.</returns>
        [Obsolete("Use FinishRequestAsync instead.")]
        public byte[] FinishRequest(
            NodeId applicationId,
            NodeId requestId,
            out byte[] privateKey,
            out byte[][] issuerCertificates)
        {
            (byte[] publicKey, byte[] privateKeyResult, byte[][] issuerCertificatesResult) =
                FinishRequestAsync(applicationId, requestId).GetAwaiter().GetResult();
            privateKey = privateKeyResult;
            issuerCertificates = issuerCertificatesResult;
            return publicKey;
        }

        /// <summary>
        /// Checks the request status.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="requestId">The request id.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>The public key.The private key.The issuer certificates.</returns>
        public async Task<(byte[] publicKey, byte[] privateKey, byte[][] issuerCertificates)> FinishRequestAsync(
            NodeId applicationId,
            NodeId requestId,
            CancellationToken ct = default)
        {
            byte[] privateKey = null;
            byte[][] issuerCertificates = null;

            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(MethodIds.Directory_FinishRequest, session.NamespaceUris),
                ct,
                applicationId,
                requestId).ConfigureAwait(false);

            byte[] certificate = null;

            if (outputArguments.Count >= 1)
            {
                certificate = outputArguments[0] as byte[];
            }

            if (outputArguments.Count >= 2)
            {
                privateKey = outputArguments[1] as byte[];
            }

            if (outputArguments.Count >= 3)
            {
                issuerCertificates = outputArguments[2] as byte[][];
            }

            return (certificate, privateKey, issuerCertificates);
        }

        /// <summary>
        /// Gets the certificate groups.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        [Obsolete("Use GetCertificateGroupsAsync instead.")]
        public NodeId[] GetCertificateGroups(NodeId applicationId)
        {
            return GetCertificateGroupsAsync(applicationId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gets the certificate groups.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="ct">The cancellationToken</param>
        public async Task<NodeId[]> GetCertificateGroupsAsync(NodeId applicationId, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_GetCertificateGroups,
                    session.NamespaceUris),
                ct,
                applicationId).ConfigureAwait(false);

            if (outputArguments.Count >= 1)
            {
                return outputArguments[0] as NodeId[];
            }

            return null;
        }

        /// <summary>
        /// Gets the trust lists method.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">Type of the trust list.</param>
        [Obsolete("Use GetTrustListAsync instead.")]
        public NodeId GetTrustList(NodeId applicationId, NodeId certificateGroupId)
        {
            return GetTrustListAsync(applicationId, certificateGroupId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gets the trust lists method.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">Type of the trust list.</param>
        /// <param name="ct">The cancellationToken</param>
        public async Task<NodeId> GetTrustListAsync(NodeId applicationId, NodeId certificateGroupId, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(MethodIds.Directory_GetTrustList, session.NamespaceUris),
                ct,
                applicationId,
                certificateGroupId).ConfigureAwait(false);

            if (outputArguments.Count >= 1)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Gets the certificate status.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">Group of the trust list.</param>
        /// <param name="certificateTypeId">Type of the trust list.</param>
        [Obsolete("Use GetCertificateStatusAsync instead.")]
        public bool GetCertificateStatus(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId)
        {
            return GetCertificateStatusAsync(applicationId, certificateGroupId, certificateTypeId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gets the certificate status.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">Group of the trust list.</param>
        /// <param name="certificateTypeId">Type of the trust list.</param>
        /// <param name="ct">The cancellationToken</param>
        public async Task<bool> GetCertificateStatusAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    MethodIds.Directory_GetCertificateStatus,
                    session.NamespaceUris),
                ct,
                applicationId,
                certificateGroupId,
                certificateTypeId).ConfigureAwait(false);

            if (outputArguments.Count >= 1 && outputArguments[0] != null)
            {
                bool? result = outputArguments[0] as bool?;
                if (result != null)
                {
                    return (bool)result;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the trust list.
        /// </summary>
        [Obsolete("Use ReadTrustListAsync instead.")]
        public TrustListDataType ReadTrustList(NodeId trustListId)
        {
            return ReadTrustListAsync(trustListId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the trust list.
        /// </summary>
        public async Task<TrustListDataType> ReadTrustListAsync(NodeId trustListId, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            IList<object> outputArguments = await session.CallAsync(
                trustListId,
                Ua.MethodIds.FileType_Open,
                ct,
                (byte)OpenFileMode.Read).ConfigureAwait(false);

            uint fileHandle = (uint)outputArguments[0];
            using var ostrm = new MemoryStream();
            try
            {
                while (true)
                {
                    const int length = 4096;

                    outputArguments = await session.CallAsync(
                        trustListId,
                        Ua.MethodIds.FileType_Read,
                        ct,
                        fileHandle,
                        length).ConfigureAwait(false);

                    byte[] bytes = (byte[])outputArguments[0];
                    ostrm.Write(bytes, 0, bytes.Length);

                    if (length != bytes.Length)
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                await session.CallAsync(trustListId, Ua.MethodIds.FileType_Close, ct, fileHandle).ConfigureAwait(false);
            }

            ostrm.Position = 0;

            var trustList = new TrustListDataType();
            using (var decoder = new BinaryDecoder(ostrm, session.MessageContext))
            {
                trustList.Decode(decoder);
            }
            return trustList;
        }

        /// <summary>
        /// Connect the session
        /// </summary>
        private async Task ConnectInternalAsync(
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            CancellationToken ct)
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Session != null)
                {
                    Session.Dispose();
                    Session = null;
                }

                Session = await m_sessionFactory.CreateAsync(
                    Configuration,
                    endpoint,
                    updateBeforeConnect,
                    false,
                    Configuration.ApplicationName,
                    60000,
                    AdminCredentials,
                    PreferredLocales,
                    ct)
                .ConfigureAwait(false);

                Session.SessionClosing += Session_SessionClosing;
                Session.KeepAlive += Session_KeepAliveAsync;
                Session.KeepAlive += KeepAlive;

                // TODO: implement, suppress warning/error
                if (ServerStatusChanged != null)
                {
                }

                if (Session.Factory.GetSystemType(DataTypeIds.ApplicationRecordDataType) == null)
                {
                    Session.Factory.AddEncodeableTypes(typeof(ObjectIds).GetTypeInfo().Assembly);
                }

                m_endpoint = Session.ConfiguredEndpoint;
                m_logger.LogInformation("Connected to {EndpointUrl}.", EndpointUrl);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Connect the client if not connected
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task<ISession> ConnectIfNeededAsync(CancellationToken ct)
        {
            // Either connect or ct will throw or Session will be valid
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await m_lock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (Session != null && Session.Connected)
                    {
                        return Session;
                    }
                }
                finally
                {
                    m_lock.Release();
                }
                await ConnectAsync(ct).ConfigureAwait(false);
            }
        }

        private readonly SemaphoreSlim m_lock = new(1, 1);
        private readonly ISessionFactory m_sessionFactory;
        private readonly ILogger m_logger;
        private ConfiguredEndpoint m_endpoint;
        private bool m_disposed;
    }
}
