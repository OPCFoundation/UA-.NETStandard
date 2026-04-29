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
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// A class that provides access to a Global Discovery Server.
    /// </summary>
    public class GlobalDiscoveryServerClient
        : IGlobalDiscoveryServerClient, IAsyncDisposable, IDisposable
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
            : this(configuration, options: null, adminUserIdentity, sessionFactory, diagnosticsMasks)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalDiscoveryServerClient"/> class
        /// with the supplied <see cref="GdsClientOptions"/>.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">Client options. Defaults are used when null.</param>
        /// <param name="adminUserIdentity">The user identity for the administrator.</param>
        /// <param name="sessionFactory">Used to create session to the server.</param>
        /// <param name="diagnosticsMasks">Return diagnostics to use for all requests.</param>
        public GlobalDiscoveryServerClient(
            ApplicationConfiguration configuration,
            GdsClientOptions options,
            IUserIdentity adminUserIdentity = null,
            ISessionFactory sessionFactory = null,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            Configuration = configuration;
            m_options = options ?? new GdsClientOptions();
            MessageContext = configuration.CreateMessageContext();
            m_logger = MessageContext.Telemetry.CreateLogger<GlobalDiscoveryServerClient>();
            m_sessionFactory = sessionFactory ??
                new DefaultSessionFactory(MessageContext.Telemetry)
                {
                    ReturnDiagnostics = diagnosticsMasks
                };
            AdminCredentials = adminUserIdentity;
        }

        /// <inheritdoc/>
        public ApplicationConfiguration Configuration { get; }

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext { get; }

        /// <inheritdoc/>
        public IUserIdentity AdminCredentials { get; set; }

        /// <inheritdoc/>
        public string EndpointUrl => m_endpoint?.EndpointUrl.ToString();

        /// <inheritdoc/>
        public ArrayOf<string> PreferredLocales { get; set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                m_disposeCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            try
            {
                await m_lock.WaitAsync().ConfigureAwait(false);
                try
                {
                    Session?.Dispose();
                    Session = null;
                    m_directory = null;
                    m_certificateDirectory = null;
                }
                finally
                {
                    m_lock.Release();
                }
            }
            finally
            {
                m_lock.Dispose();
                m_disposeCts.Dispose();
                GC.SuppressFinalize(this);
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
                ISession session = Session;
                return session is { Connected: true };
            }
        }

        /// <inheritdoc/>
        public ISession Session { get; private set; }

        /// <summary>
        /// Typed proxy for methods declared on the DirectoryType ObjectType
        /// (Find/Get/Query/Register/Unregister/UpdateApplication). Constructed
        /// after a successful connect and cleared on disconnect/dispose.
        /// </summary>
        public DirectoryTypeClient Directory => m_directory;

        /// <summary>
        /// Typed proxy for methods declared on the CertificateDirectoryType
        /// ObjectType (StartNewKeyPairRequest/StartSigningRequest/FinishRequest/
        /// RevokeCertificate/GetCertificate*/GetTrustList/CheckRevocationStatus).
        /// Constructed after a successful connect and cleared on disconnect/dispose.
        /// </summary>
        public CertificateDirectoryTypeClient CertificateDirectory => m_certificateDirectory;

        private DirectoryTypeClient m_directory;

        private CertificateDirectoryTypeClient m_certificateDirectory;

        /// <summary>
        /// Gets the endpoint. The setter is write-once: an endpoint may be
        /// assigned only before the first connect and only when not already
        /// connected. Use <see cref="ResetCredentials"/> to clear the admin
        /// credentials after a successful disconnect.
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        public ConfiguredEndpoint Endpoint
        {
            get
            {
                ISession session = Session;
                return session?.ConfiguredEndpoint ?? m_endpoint;
            }
            set
            {
                if (IsConnected)
                {
                    throw new InvalidOperationException(
                        "Session must be closed before changing endpoint.");
                }

                m_endpoint = value;
            }
        }

        /// <inheritdoc/>
        public void ResetCredentials()
        {
            AdminCredentials = null;
        }
        /// <inheritdoc/>
        public async ValueTask<List<string>> GetDefaultServerUrlsAsync(
            LocalDiscoveryServerClient lds,
            CancellationToken ct = default)
        {
            var serverUrls = new List<string>();

            try
            {
                lds ??= new LocalDiscoveryServerClient(Configuration);

                (ArrayOf<ServerOnNetwork> servers, DateTimeUtc _) = await lds.FindServersOnNetworkAsync(
                    0,
                    1000,
                    ct).ConfigureAwait(false);

                foreach (ServerOnNetwork server in servers)
                {
                    // ignore GDS and LDS servers
                    var set = server.ServerCapabilities.ToList();
                    if (set.Contains(ServerCapability.GDS) ||
                        set.Contains(ServerCapability.LDS))
                    {
                        continue;
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
        /// <inheritdoc/>
        public async ValueTask<List<string>> GetDefaultGdsUrlsAsync(
            LocalDiscoveryServerClient lds,
            CancellationToken ct = default)
        {
            var gdsUrls = new List<string>();

            try
            {
                lds ??= new LocalDiscoveryServerClient(Configuration);

                (ArrayOf<ServerOnNetwork> servers, DateTimeUtc _) = await lds.FindServersOnNetworkAsync(
                    0,
                    1000,
                    ct).ConfigureAwait(false);

                foreach (ServerOnNetwork server in servers)
                {
                    if (server.ServerCapabilities.ToList().Contains(ServerCapability.GDS))
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
        /// <inheritdoc/>
        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            return ConnectAsync(m_endpoint, ct);
        }
        /// <inheritdoc/>
        public async ValueTask ConnectAsync(string endpointUrl, CancellationToken ct = default)
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

            int maxAttempts = m_options.MaxConnectAttempts;
            int backoffMs = (int)m_options.ConnectBackoff.TotalMilliseconds;
            ServiceResultException lastException = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
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
                catch (ServiceResultException e) when (
                    e.StatusCode == StatusCodes.BadServerHalted ||
                    e.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    e.StatusCode == StatusCodes.BadNoCommunication)
                {
                    lastException = e;
                    m_logger.LogError(e, "Failed to connect {Attempt}. Retrying...", attempt + 1);
                    if (attempt + 1 < maxAttempts)
                    {
                        await Task.Delay(backoffMs, ct).ConfigureAwait(false);
                    }
                }
            }
            throw lastException ?? ServiceResultException.Create(
                StatusCodes.BadNoCommunication,
                "Failed to connect after {0} attempts.",
                maxAttempts);
        }
        /// <inheritdoc/>
        public async ValueTask ConnectAsync(ConfiguredEndpoint endpoint, CancellationToken ct = default)
        {
            if (endpoint == null)
            {
                endpoint = m_endpoint;

                if (endpoint == null)
                {
                    throw new ArgumentNullException(nameof(endpoint));
                }
            }

            int maxAttempts = m_options.MaxConnectAttempts;
            int backoffMs = (int)m_options.ConnectBackoff.TotalMilliseconds;
            ServiceResultException lastException = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    await ConnectInternalAsync(endpoint, true, ct).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException e) when (
                    e.StatusCode == StatusCodes.BadServerHalted ||
                    e.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    e.StatusCode == StatusCodes.BadNoCommunication)
                {
                    lastException = e;
                    m_logger.LogError(e, "Failed to connect {Attempt}. Retrying...", attempt + 1);
                    if (attempt + 1 < maxAttempts)
                    {
                        await Task.Delay(backoffMs, ct).ConfigureAwait(false);
                    }
                }
            }
            throw lastException ?? ServiceResultException.Create(
                StatusCodes.BadNoCommunication,
                "Failed to connect after {0} attempts.",
                maxAttempts);
        }
        /// <inheritdoc/>
        public async ValueTask DisconnectAsync(CancellationToken ct = default)
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ISession session = Session;
                Session = null;
                m_directory = null;
                m_certificateDirectory = null;

                if (session == null)
                {
                    return;
                }
                try
                {
                    KeepAlive?.Invoke(session, null);
                    await session.CloseAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    session.Dispose();
                }
            }
            finally
            {
                m_lock.Release();
            }
        }

        private void Session_KeepAliveAsync(ISession session, KeepAliveEventArgs e)
        {
            if (m_disposed)
            {
                return;
            }

            // Re-raise the public KeepAlive event synchronously on the same callback
            // thread to preserve original ordering for subscribers.
            try
            {
                KeepAlive?.Invoke(session, e);
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Subscriber threw in KeepAlive handler.");
            }

            if (!ServiceResult.IsBad(e.Status))
            {
                return;
            }

            // Bad keep-alive: schedule async cleanup without blocking the keep-alive
            // callback thread. Errors are logged; we never throw out of fire-and-forget.
            _ = Task.Run(async () =>
            {
                try
                {
                    await m_lock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (ReferenceEquals(session, Session))
                        {
                            Session.Dispose();
                            Session = null;
                            m_directory = null;
                            m_certificateDirectory = null;
                        }
                    }
                    finally
                    {
                        m_lock.Release();
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Error during KeepAlive handling.");
                }
            });
        }

        private void Session_SessionClosing(object sender, EventArgs e)
        {
            m_logger.LogInformation("The GDS Client session is closing.");
        }

        /// <inheritdoc/>
        public event KeepAliveEventHandler KeepAlive;

        /// <summary>
        /// Occurs when the server status changes.
        /// </summary>
#pragma warning disable CS0067
        public event MonitoredItemNotificationEventHandler ServerStatusChanged;
#pragma warning restore CS0067
        /// <inheritdoc/>
        public async ValueTask<ArrayOf<ApplicationRecordDataType>> FindApplicationAsync(
            string applicationUri,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_directory.FindApplicationsAsync(
                applicationUri ?? string.Empty,
                ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask<ArrayOf<ServerOnNetwork>> QueryServersAsync(
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            ArrayOf<string> serverCapabilities,
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
        /// <inheritdoc/>
        public async ValueTask<(ArrayOf<ServerOnNetwork> servers, DateTimeUtc lastCounterResetTime)> QueryServersAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            ArrayOf<string> serverCapabilities,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            (DateTimeUtc lastCounterResetTime, ArrayOf<ServerOnNetwork> servers) = await m_directory.QueryServersAsync(
                startingRecordId,
                maxRecordsToReturn,
                applicationName ?? string.Empty,
                applicationUri ?? string.Empty,
                productUri ?? string.Empty,
                serverCapabilities,
                ct).ConfigureAwait(false);
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
        /// <param name="ct">The cancellationToken</param>
        /// <returns>A enumerator used to access the results.
        /// The time when the counter was last changed.
        /// The id of the next record.</returns>
        public async ValueTask<(
            ArrayOf<ApplicationDescription> applications,
            DateTimeUtc lastCounterResetTime,
            uint nextRecordId)> QueryApplicationsAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            ArrayOf<string> serverCapabilities,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            (DateTimeUtc lastCounterResetTime, uint nextRecordId, ArrayOf<ApplicationDescription> applications) =
                await m_directory.QueryApplicationsAsync(
                    startingRecordId,
                    maxRecordsToReturn,
                    applicationName ?? string.Empty,
                    applicationUri ?? string.Empty,
                    applicationType,
                    productUri ?? string.Empty,
                    serverCapabilities,
                    ct).ConfigureAwait(false);
            return (applications, lastCounterResetTime, nextRecordId);
        }
        /// <inheritdoc/>
        public async ValueTask<ApplicationRecordDataType> GetApplicationAsync(
            NodeId applicationId,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_directory.GetApplicationAsync(applicationId, ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask<NodeId> RegisterApplicationAsync(
            ApplicationRecordDataType application,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_directory.RegisterApplicationAsync(application, ct).ConfigureAwait(false);
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
        public async ValueTask<(ArrayOf<NodeId> certificateTypeIds, ArrayOf<ByteString> certificates)> GetCertificatesAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_certificateDirectory.GetCertificatesAsync(
                applicationId,
                certificateGroupId,
                ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask<(StatusCode certificateStatus, DateTimeUtc validityTime)> CheckRevocationStatusAsync(
            ByteString certificate,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_certificateDirectory.CheckRevocationStatusAsync(certificate, ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask UpdateApplicationAsync(ApplicationRecordDataType application, CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            await m_directory.UpdateApplicationAsync(application, ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask UnregisterApplicationAsync(NodeId applicationId, CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            await m_directory.UnregisterApplicationAsync(applicationId, ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask RevokeCertificateAsync(NodeId applicationId, ByteString certificate, CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            await m_certificateDirectory.RevokeCertificateAsync(applicationId, certificate, ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask<NodeId> StartNewKeyPairRequestAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> domainNames,
            string privateKeyFormat,
            char[] privateKeyPassword,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_certificateDirectory.StartNewKeyPairRequestAsync(
                applicationId,
                certificateGroupId,
                certificateTypeId,
                subjectName,
                domainNames,
                privateKeyFormat,
                new string(privateKeyPassword),
                ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask<NodeId> StartSigningRequestAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificateRequest,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_certificateDirectory.StartSigningRequestAsync(
                applicationId,
                certificateGroupId,
                certificateTypeId,
                certificateRequest,
                ct).ConfigureAwait(false);
        }
        /// <summary>
        /// Checks the request status.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="requestId">The request id.</param>
        /// <param name="ct">The cancellationToken</param>
        /// <returns>The public key.The private key.The issuer certificates.</returns>
        public async ValueTask<(ByteString publicKey, ByteString privateKey, ArrayOf<ByteString> issuerCertificates)> FinishRequestAsync(
            NodeId applicationId,
            NodeId requestId,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            (ByteString certificate, ByteString privateKey, ArrayOf<ByteString> issuerCertificates) =
                await m_certificateDirectory.FinishRequestAsync(applicationId, requestId, ct).ConfigureAwait(false);
            return (certificate, privateKey, issuerCertificates);
        }
        /// <inheritdoc/>
        public async ValueTask<ArrayOf<NodeId>> GetCertificateGroupsAsync(NodeId applicationId, CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_certificateDirectory.GetCertificateGroupsAsync(applicationId, ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask<NodeId> GetTrustListAsync(NodeId applicationId, NodeId certificateGroupId, CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_certificateDirectory.GetTrustListAsync(
                applicationId,
                certificateGroupId,
                ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public async ValueTask<bool> GetCertificateStatusAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            CancellationToken ct = default)
        {
            _ = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            return await m_certificateDirectory.GetCertificateStatusAsync(
                applicationId,
                certificateGroupId,
                certificateTypeId,
                ct).ConfigureAwait(false);
        }
        /// <inheritdoc/>
        public ValueTask<TrustListDataType> ReadTrustListAsync(NodeId trustListId, CancellationToken ct = default)
        {
            return ReadTrustListAsync(trustListId, 0, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<TrustListDataType> ReadTrustListAsync(
            NodeId trustListId,
            long maxTrustListSize,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);

            long sizeLimit = maxTrustListSize == 0 ? m_options.MaxTrustListSize : maxTrustListSize;
            int chunkSize = Math.Max(m_options.FileTransferChunkSize, 4096);

            var file = new FileTypeClient(session, trustListId, MessageContext.Telemetry);
            uint fileHandle = await file.OpenAsync((byte)OpenFileMode.Read, ct).ConfigureAwait(false);
            try
            {
                return await TrustListFileTransferHelper.ReadAsync(
                    file,
                    fileHandle,
                    session.MessageContext,
                    sizeLimit,
                    chunkSize,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await file.CloseAsync(fileHandle, ct).ConfigureAwait(false);
            }
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
                Session?.Dispose();
                Session = null;
                m_directory = null;
                m_certificateDirectory = null;

                Session = await m_sessionFactory.CreateAsync(
                    Configuration,
                    endpoint,
                    updateBeforeConnect,
                    false,
                    Configuration.ApplicationName,
                    (uint)m_options.SessionTimeout.TotalMilliseconds,
                    AdminCredentials,
                    PreferredLocales,
                    ct)
                .ConfigureAwait(false);

                Session.SessionClosing += Session_SessionClosing;
                Session.KeepAlive += Session_KeepAliveAsync;

                if (!Session.Factory.ContainsEncodeableType(DataTypeIds.ApplicationRecordDataType))
                {
                    Session.Factory.Builder.AddOpcUaGds().Commit();
                }

                NodeId directoryNodeId = ExpandedNodeId.ToNodeId(
                    ObjectIds.Directory,
                    Session.NamespaceUris);
                m_directory = new DirectoryTypeClient(
                    Session,
                    directoryNodeId,
                    MessageContext.Telemetry);
                m_certificateDirectory = new CertificateDirectoryTypeClient(
                    Session,
                    directoryNodeId,
                    MessageContext.Telemetry);

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
        private readonly GdsClientOptions m_options;
        private readonly CancellationTokenSource m_disposeCts = new();
        private ConfiguredEndpoint m_endpoint;
        private bool m_disposed;
    }
}
