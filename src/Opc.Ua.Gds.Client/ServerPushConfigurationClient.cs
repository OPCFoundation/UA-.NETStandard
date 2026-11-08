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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// A class used to access the Push Configuration information model.
    /// </summary>
    public sealed class ServerPushConfigurationClient : IServerPushConfigurationClient, IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerPushConfigurationClient"/> class.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="sessionFactory">Used to create session to the server.</param>
        /// <param name="diagnosticsMasks">Return diagnostics to use for all requests</param>
        public ServerPushConfigurationClient(
            ApplicationConfiguration configuration,
            ISessionFactory? sessionFactory = null,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None)
            : this(configuration, options: null, sessionFactory, diagnosticsMasks, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerPushConfigurationClient"/> class
        /// with the supplied <see cref="GdsClientOptions"/>.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">Client options. Defaults are used when null.</param>
        /// <param name="sessionFactory">Used to create session to the server.</param>
        /// <param name="diagnosticsMasks">Return diagnostics to use for all requests.</param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/> used for
        /// connect-retry backoff delays. Defaults to <see cref="TimeProvider.System"/>
        /// when <c>null</c>.</param>
        public ServerPushConfigurationClient(
            ApplicationConfiguration configuration,
            GdsClientOptions? options,
            ISessionFactory? sessionFactory = null,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None,
            TimeProvider? timeProvider = null)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_options = options ?? new GdsClientOptions();
            MessageContext = configuration.CreateMessageContext();
            m_logger = MessageContext.Telemetry.CreateLogger<ServerPushConfigurationClient>();
            m_sessionFactory = sessionFactory ??
                new DefaultSessionFactory(MessageContext.Telemetry)
                {
                    ReturnDiagnostics = diagnosticsMasks
                };
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Borrows an already authorized encrypted session generation without owning its connection.
        /// </summary>
        private ServerPushConfigurationClient(
            ApplicationConfiguration configuration,
            SessionRequestContext requestContext,
            GdsClientOptions? options)
            : this(configuration, options)
        {
            m_requestContext = requestContext;
            Session = requestContext.Session;
            InitializeSessionClients(Session);
        }

        /// <summary>
        /// Captures an already authorized encrypted session for push certificate and TrustList operations.
        /// The client never connects, reconnects, changes user identity, or closes the supplied session.
        /// Each dispatch checks the captured generation; callers must authenticate and validate the device first.
        /// Use DisposeAsync to release the local context, not DisconnectAsync.
        /// </summary>
        public static async ValueTask<ServerPushConfigurationClient> CaptureAsync(
            ApplicationConfiguration configuration,
            ISession session,
            GdsClientOptions? options = null,
            CancellationToken ct = default)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            SessionRequestContext context = await SessionRequestContext.CaptureConfidentialAsync(session, ct)
                .ConfigureAwait(false);
            try
            {
                return new ServerPushConfigurationClient(configuration, context, options);
            }
            catch
            {
                await context.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Creates and connects a push configuration client using a supplied session factory.
        /// </summary>
        /// <param name="sessionFactory">The session factory used to create the client session.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="endpoint">The configured endpoint to connect to.</param>
        /// <param name="options">Client options. Defaults are used when null.</param>
        /// <param name="adminCredentials">The administrator credentials.</param>
        /// <param name="diagnosticsMasks">Return diagnostics to use for all requests.</param>
        /// <param name="timeProvider">Optional time provider used for connect-retry backoff delays.</param>
        /// <param name="ct">A cancellation token to cancel the operation with.</param>
        /// <returns>A connected push configuration client.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sessionFactory"/>, <paramref name="configuration"/>
        /// or <paramref name="endpoint"/> is <c>null</c>.
        /// </exception>
        public static async Task<ServerPushConfigurationClient> CreateAsync(
            ISessionFactory sessionFactory,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            GdsClientOptions? options = null,
            IUserIdentity? adminCredentials = null,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None,
            TimeProvider? timeProvider = null,
            CancellationToken ct = default)
        {
            if (sessionFactory == null)
            {
                throw new ArgumentNullException(nameof(sessionFactory));
            }
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            sessionFactory.ReturnDiagnostics = diagnosticsMasks;
            var client = new ServerPushConfigurationClient(
                configuration,
                options,
                sessionFactory,
                diagnosticsMasks,
                timeProvider)
            {
                AdminCredentials = adminCredentials,
                Endpoint = endpoint
            };

            try
            {
                await client.ConnectAsync(endpoint, ct).ConfigureAwait(false);
                return client;
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Creates and connects a push configuration client using a shared channel manager.
        /// </summary>
        /// <param name="manager">The client channel manager used to acquire the session channel.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="endpoint">The configured endpoint to connect to.</param>
        /// <param name="options">Client options. Defaults are used when null.</param>
        /// <param name="adminCredentials">The administrator credentials.</param>
        /// <param name="diagnosticsMasks">Return diagnostics to use for all requests.</param>
        /// <param name="timeProvider">Optional time provider used for connect-retry backoff delays.</param>
        /// <param name="ct">A cancellation token to cancel the operation with.</param>
        /// <returns>A connected push configuration client.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="manager"/>, <paramref name="configuration"/> or <paramref name="endpoint"/> is <c>null</c>.
        /// </exception>
        public static Task<ServerPushConfigurationClient> CreateAsync(
            IClientChannelManager manager,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            GdsClientOptions? options = null,
            IUserIdentity? adminCredentials = null,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None,
            TimeProvider? timeProvider = null,
            CancellationToken ct = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            ServiceMessageContext messageContext = configuration.CreateMessageContext();
            var sessionFactory = new ChannelManagerSessionFactory(
                manager,
                messageContext.Telemetry,
                diagnosticsMasks,
                timeProvider);

            return CreateAsync(
                sessionFactory,
                configuration,
                endpoint,
                options,
                adminCredentials,
                diagnosticsMasks,
                timeProvider,
                ct);
        }

        public NodeId DefaultApplicationGroup { get; private set; }
        public NodeId DefaultHttpsGroup { get; private set; }
        public NodeId DefaultUserTokenGroup { get; private set; }

        /// <summary>
        /// Gets or sets the application certificate type used when a caller
        /// does not supply one explicitly to certificate-management methods.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType"/>
        /// to preserve backwards compatibility. Callers that need to manage
        /// ECC or HTTPS certificates should set this property before
        /// invoking certificate-management methods, or pass an explicit
        /// certificate type to those methods (e.g.
        /// <see cref="CreateSigningRequestAsync"/>,
        /// <see cref="UpdateCertificateAsync"/>).
        /// </remarks>
        public NodeId ApplicationCertificateType { get; set; }
            = Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;

        /// <inheritdoc/>
        public ApplicationConfiguration Configuration { get; }

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext { get; }

        /// <inheritdoc/>
        public IUserIdentity? AdminCredentials { get; set; }

        /// <inheritdoc/>
        public string? EndpointUrl => m_endpoint?.EndpointUrl?.ToString();

        /// <inheritdoc/>
        public event EventHandler<AdminCredentialsRequiredEventArgs>? AdminCredentialsRequired;

        /// <inheritdoc/>
        public event EventHandler? ConnectionStatusChanged;

        /// <inheritdoc/>
        public ArrayOf<string> PreferredLocales { get; set; }

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
                ISession? session = Session;
                return session is { Connected: true };
            }
        }

        /// <inheritdoc/>
        public ISession? Session { get; private set; }

        /// <summary>
        /// Typed proxy for the ServerConfigurationType ObjectType
        /// (CreateSigningRequest/UpdateCertificate/GetCertificates/GetRejectedList/
        /// ApplyChanges). Constructed after a successful connect and cleared on
        /// disconnect/dispose.
        /// </summary>
        public ServerConfigurationTypeClient? ServerConfiguration => m_serverConfiguration;

        /// <summary>
        /// Gets the endpoint. The setter is write-once: an endpoint may be
        /// assigned only before the first connect and only when not already
        /// connected. Use <see cref="ResetCredentials"/> to clear cached
        /// admin credentials.
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        public ConfiguredEndpoint? Endpoint
        {
            get
            {
                ISession? session = Session;
                return session?.ConfiguredEndpoint ?? m_endpoint;
            }
            set
            {
                RequireOwnedConnection();
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
        public event KeepAliveEventHandler? KeepAlive;

        /// <inheritdoc/>
        public event EventHandler<ServerStatusChangedEventArgs>? ServerStatusChanged;

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
                await m_disposeCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            try
            {
                await m_lock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (m_requestContext != null)
                    {
                        await m_requestContext.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        Session?.Dispose();
                    }
                    Session = null;
                    m_serverConfiguration = null;
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

        /// <inheritdoc/>
        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            return ConnectAsync(m_endpoint, ct);
        }

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(string endpointUrl, CancellationToken ct = default)
        {
            RequireOwnedConnection();
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
            ServiceResultException? lastException = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    EndpointDescription? endpointDescription =
                        await CoreClientUtils.SelectEndpointAsync(
                            Configuration,
                            endpointUrl,
                            true,
                            MessageContext.Telemetry,
                            ct).ConfigureAwait(false) ??
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityPolicyRejected,
                            "The server offers no secure endpoint the client supports.");
                    var endpointConfiguration = EndpointConfiguration.Create(Configuration);
                    var endpoint = new ConfiguredEndpoint(
                        null,
                        endpointDescription,
                        endpointConfiguration);

                    await ConnectInternalAsync(endpoint, false, new Uri(endpointUrl), ct).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException e) when (
                    e.StatusCode == StatusCodes.BadServerHalted ||
                    e.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    e.StatusCode == StatusCodes.BadNoCommunication)
                {
                    lastException = e;
                    m_logger.FailedToConnect(e, attempt + 1);
                    if (attempt + 1 < maxAttempts)
                    {
                        await m_timeProvider.Delay(TimeSpan.FromMilliseconds(backoffMs), ct).ConfigureAwait(false);
                    }
                }
            }
            throw lastException ??
                ServiceResultException.Create(
                    StatusCodes.BadNoCommunication,
                    "Failed to connect after {0} attempts.",
                    maxAttempts);
        }

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(ConfiguredEndpoint? endpoint, CancellationToken ct = default)
        {
            RequireOwnedConnection();
            endpoint ??= m_endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            await ConnectWithIdentityAsync(endpoint, AdminCredentials, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisconnectAsync(CancellationToken ct = default)
        {
            RequireOwnedConnection();
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {

                ISession? session = Session;
                Session = null;
                m_serverConfiguration = null;

                if (session == null)
                {
                    return;
                }
                try
                {
                    KeepAlive?.Invoke(session, null!);
                    await session.CloseAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    session.Dispose();
                    RaiseConnectionStatusChangedEvent();
                }
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<string>> GetSupportedKeyFormatsAsync(CancellationToken ct = default)
        {
            if (m_requestContext == null && (AdminCredentials == null || Endpoint == null))
            {
                return default;
            }

            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                ArrayOf<ReadValueId> nodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(
                            Ua.VariableIds.ServerConfiguration_SupportedPrivateKeyFormats,
                            session.NamespaceUris
                        ),
                        AttributeId = Attributes.Value
                    }
                ];

                ReadResponse result = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    ct).ConfigureAwait(false);

                ClientBase.ValidateResponse(result.Results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(result.DiagnosticInfos, nodesToRead);
                return result.Results[0].WrappedValue.GetStringArray();
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<TrustListDataType> ReadTrustListAsync(NodeId certificateGroupId, TrustListMasks masks = TrustListMasks.All,
            long maxTrustListSize = 0, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                TrustListTypeClient trustListClient = await GetApplicationGroupTrustListClientAsync(session, certificateGroupId, ct).ConfigureAwait(false);

                long sizeLimit = maxTrustListSize == 0
                    ? m_options.MaxTrustListSize
                    : maxTrustListSize;
                int chunkSize = m_options.FileTransferChunkSize;

                uint fileHandle = await trustListClient.OpenWithMasksAsync((uint)masks, ct)
                    .ConfigureAwait(false);
                if (m_requestContext != null)
                {
                    return await TrustListFileTransferHelper.ReadAndCloseAsync(
                        trustListClient, fileHandle, session.MessageContext, sizeLimit, chunkSize, ct)
                        .ConfigureAwait(false);
                }
                try
                {
                    return await TrustListFileTransferHelper.ReadAsync(
                        trustListClient,
                        fileHandle,
                        session.MessageContext,
                        sizeLimit,
                        chunkSize,
                        ct).ConfigureAwait(false);
                }
                finally
                {
                    if (IsConnected)
                    {
                        try
                        {
                            await trustListClient.CloseAsync(fileHandle, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            m_logger.FailedToCloseTrustListFileHandle(ex);
                        }
                    }
                }
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ValueTask<TrustListDataType> ReadTrustListAsync(
            TrustListMasks masks = TrustListMasks.All,
            long maxTrustListSize = 0,
            CancellationToken ct = default)
        {
            return ReadTrustListAsync(DefaultApplicationGroup, masks, maxTrustListSize, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> UpdateTrustListAsync(TrustListDataType trustList, CancellationToken ct = default)
        {
            return UpdateTrustListAsync(trustList, 0, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> UpdateTrustListAsync(TrustListDataType trustList, long maxTrustListSize, CancellationToken ct = default)
        {
            return UpdateTrustListAsync(DefaultApplicationGroup, trustList, maxTrustListSize, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> UpdateTrustListAsync(NodeId certificateGroupId, TrustListDataType trustList, long maxTrustListSize,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                TrustListTypeClient trustListClient = await GetApplicationGroupTrustListClientAsync(session, certificateGroupId, ct).ConfigureAwait(false);

                long sizeLimit = maxTrustListSize == 0
                    ? m_options.MaxTrustListSize
                    : maxTrustListSize;

                return await TrustListFileTransferHelper.WriteAsync(
                    trustListClient,
                    trustList,
                    session.MessageContext,
                    sizeLimit,
                    m_options.FileTransferChunkSize,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Returns a <see cref="TrustListTypeClient"/> for the specified certificate group on the connected server.
        /// </summary>
        private async ValueTask<TrustListTypeClient> GetApplicationGroupTrustListClientAsync(
            ISession session, NodeId certificateGroupId, CancellationToken ct = default)
        {
            NodeId trustListId = await GetRelatedTrustListIdByCertificateGroupIdAsync(certificateGroupId, ct)
                    .ConfigureAwait(false);

            return new TrustListTypeClient(
                session,
                trustListId,
                MessageContext.Telemetry);
        }

        /// <inheritdoc/>
        public ValueTask AddCertificateAsync(Certificate certificate, bool isTrustedCertificate, CancellationToken ct = default)
        {
            return AddCertificateAsync(DefaultApplicationGroup, certificate, isTrustedCertificate, ct);
        }

        /// <inheritdoc/>
        public async ValueTask AddCertificateAsync(NodeId certificateGroupId, Certificate certificate, bool isTrustedCertificate,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);
            try
            {
                TrustListTypeClient trustListClient = await GetApplicationGroupTrustListClientAsync(session, certificateGroupId, ct).ConfigureAwait(false);
                await trustListClient.AddCertificateAsync(certificate.RawData.ToByteString(), isTrustedCertificate, ct).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ValueTask RemoveCertificateAsync(string thumbprint, bool isTrustedCertificate, CancellationToken ct = default)
        {
            return RemoveCertificateAsync(DefaultApplicationGroup, thumbprint, isTrustedCertificate, ct);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveCertificateAsync(NodeId certificateGroupId, string thumbprint, bool isTrustedCertificate,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);
            try
            {
                TrustListTypeClient trustListClient = await GetApplicationGroupTrustListClientAsync(session, certificateGroupId, ct).ConfigureAwait(false);
                await trustListClient.RemoveCertificateAsync(thumbprint, isTrustedCertificate, ct).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// returns the Certificates assigned to CertificateTypes associated with a CertificateGroup.
        /// </summary>
        /// <param name="certificateGroupId">
        /// The identifier for the CertificateGroup. <see cref="NodeId.Null"/> selects the
        /// default application group.
        /// </param>
        /// <param name="ct"> The cancellationToken.</param>
        /// <returns>The CertificateTypes that currently have a Certificate assigned.
        ///The length of this list is the same as the length as certificates list.
        ///An empty list if the CertificateGroup does not have any CertificateTypes.
        ///A list of DER encoded Certificates assigned to CertificateGroup.
        ///The certificateType for the Certificate is specified by the corresponding element in the certificateTypes parameter.
        /// </returns>
        public async ValueTask<(ArrayOf<NodeId> certificateTypeIds, ArrayOf<ByteString> certificates)> GetCertificatesAsync(
            NodeId certificateGroupId,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);
            try
            {
                return await m_serverConfiguration!.GetCertificatesAsync(certificateGroupId, ct).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> CreateSigningRequestAsync(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            ByteString nonce,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                return await m_serverConfiguration!.CreateSigningRequestAsync(
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    regeneratePrivateKey,
                    nonce,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> UpdateCertificateAsync(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificate,
            string privateKeyFormat,
            ByteString privateKey,
            ArrayOf<ByteString> issuerCertificates,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);
            SessionRequestContext? dispatch = null;

            try
            {
                dispatch = privateKey.IsEmpty
                    ? null
                    : await SessionRequestContext.CaptureConfidentialAsync(session, ct).ConfigureAwait(false);
                ServerConfigurationTypeClient serverConfiguration = dispatch == null
                    ? m_serverConfiguration!
                    : new ServerConfigurationTypeClient(
                        dispatch.Session,
                        ExpandedNodeId.ToNodeId(Ua.ObjectIds.ServerConfiguration, dispatch.Session.NamespaceUris),
                        MessageContext.Telemetry);

                return await serverConfiguration.UpdateCertificateAsync(
                    certificateGroupId,
                    certificateTypeId,
                    certificate,
                    issuerCertificates,
                    privateKeyFormat,
                    privateKey,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (dispatch != null)
                    {
                        await dispatch.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CertificateCollection> GetRejectedListAsync(CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                ArrayOf<ByteString> rawCertificates = await m_serverConfiguration!.GetRejectedListAsync(ct).ConfigureAwait(false);
                var collection = new CertificateCollection();
                foreach (ByteString rawCertificate in rawCertificates)
                {
                    collection.Add(Certificate.FromRawData(rawCertificate));
                }
                return collection;
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask ApplyChangesAsync(CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            await m_serverConfiguration!.ApplyChangesAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<ConfigurationFileUpdateResult> UpdateConfigurationFileAsync(
            ByteString configuration,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            double revertAfterTime,
            double restartDelayTime,
            CancellationToken ct = default)
        {
            return UpdateConfigurationFileAsync(
                ExpandedNodeId.Null, configuration, targets, revertAfterTime, restartDelayTime, ct);
        }

        /// <inheritdoc/>
        public ValueTask<ConfigurationFileUpdateResult> UpdateApplicationConfigurationFileAsync(
            ExpandedNodeId applicationConfigurationId,
            ByteString configuration,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            double revertAfterTime,
            double restartDelayTime,
            CancellationToken ct = default)
        {
            if (applicationConfigurationId.IsNull || applicationConfigurationId.ServerIndex != 0 ||
                string.IsNullOrEmpty(applicationConfigurationId.NamespaceUri) ||
                applicationConfigurationId.NamespaceUri == Namespaces.OpcUa)
            {
                throw new ArgumentException(
                    "A local application instance addressed by its own namespace URI is required.",
                    nameof(applicationConfigurationId));
            }
            return UpdateConfigurationFileAsync(
                applicationConfigurationId, configuration, targets, revertAfterTime, restartDelayTime, ct);
        }

        /// <summary>
        /// Retains the selected configuration owner throughout upload, reconnection and confirmation.
        /// </summary>
        private async ValueTask<ConfigurationFileUpdateResult> UpdateConfigurationFileAsync(
            ExpandedNodeId configurationId,
            ByteString configuration,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            double revertAfterTime,
            double restartDelayTime,
            CancellationToken ct)
        {
            RequireOwnedConnection();
            if (Session is not { Connected: true } session)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "A connected, owned session is required for a ConfigurationFile update.");
            }
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);
            try
            {
                return await UpdateConfigurationFileCoreAsync(
                    session,
                    configurationId,
                    configuration,
                    targets,
                    revertAfterTime,
                    restartDelayTime,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                if (Session is { Connected: true } activeSession)
                {
                    await RevertPermissionsAsync(activeSession, oldUser, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Uploads configuration content and completes the server's update and confirmation exchange.
        /// </summary>
        private async ValueTask<ConfigurationFileUpdateResult> UpdateConfigurationFileCoreAsync(
            ISession session,
            ExpandedNodeId configurationId,
            ByteString configuration,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            double revertAfterTime,
            double restartDelayTime,
            CancellationToken ct)
        {
            if (configuration.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Configuration content is null.");
            }
            if (targets.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "At least one update target is required.");
            }
            if (!revertAfterTime.IsFinite() || revertAfterTime < 0 ||
                !restartDelayTime.IsFinite() || restartDelayTime < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "ConfigurationFile update timings must be finite and non-negative.");
            }

            ServerConfigurationTypeClient serverConfiguration = GetConfigurationOwner(session, configurationId);
            ApplicationConfigurationFileTypeClient? file =
                await serverConfiguration.GetConfigurationFileAsync(MessageContext.Telemetry, ct)
                    .ConfigureAwait(false);
            if (file == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The selected configuration owner does not expose ConfigurationFile.");
            }

            uint versionToUpdate = await ReadConfigurationFileVersionAsync(session, file, ct).ConfigureAwait(false);
            byte mode = (byte)(OpenFileMode.Read | OpenFileMode.Write);
            uint fileHandle = await file.OpenAsync(mode, ct).ConfigureAwait(false);
            bool closeAndUpdateInvoked = false;
            try
            {
                byte[] content = configuration.ToArray();
                for (int offset = 0; offset < content.Length;)
                {
                    int count = Math.Min(4096, content.Length - offset);
                    await file.WriteAsync(
                        fileHandle,
                        ByteString.From(content.AsSpan(offset, count)),
                        ct).ConfigureAwait(false);
                    offset += count;
                }

                closeAndUpdateInvoked = true;
                (ArrayOf<StatusCode> updateResults, uint newVersion, Uuid updateId) response;
                try
                {
                    response = await file.CloseAndUpdateAsync(
                        fileHandle,
                        versionToUpdate,
                        targets,
                        revertAfterTime,
                        restartDelayTime,
                        ct).ConfigureAwait(false);
                }
                catch (ServiceResultException ex) when (IsCommunicationFailure(ex.StatusCode))
                {
                    m_logger.ConfigurationFileUpdateOutcomeUnknown(ex);
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.OutcomeUnknown,
                        0,
                        Uuid.Empty,
                        default,
                        ex.StatusCode);
                }
                catch (ServiceResultException ex)
                {
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.Rejected,
                        0,
                        Uuid.Empty,
                        default,
                        ex.StatusCode);
                }
                catch (OperationCanceledException ex)
                {
                    m_logger.ConfigurationFileUpdateOutcomeUnknown(ex);
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.OutcomeUnknown,
                        0,
                        Uuid.Empty,
                        default,
                        StatusCodes.BadRequestCancelledByClient);
                }
                catch (Exception ex)
                {
                    m_logger.ConfigurationFileUpdateOutcomeUnknown(ex);
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.OutcomeUnknown,
                        0,
                        Uuid.Empty,
                        default,
                        StatusCodes.BadUnexpectedError);
                }

                if (ContainsBadTargetResult(response.updateResults))
                {
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.Rejected,
                        response.newVersion,
                        response.updateId,
                        response.updateResults,
                        StatusCodes.Uncertain);
                }

                bool requiresConfirmation = response.updateId.Guid != Guid.Empty;
                if (!requiresConfirmation && restartDelayTime <= 0)
                {
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.Applied,
                        response.newVersion,
                        response.updateId,
                        response.updateResults,
                        StatusCodes.Good);
                }

                ConfiguredEndpoint endpoint = session.ConfiguredEndpoint;
                IUserIdentity? elevatedIdentity = session.Identity;
                try
                {
                    await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    if (restartDelayTime > 0)
                    {
                        await m_timeProvider.Delay(
                            TimeSpan.FromMilliseconds(restartDelayTime),
                            ct).ConfigureAwait(false);
                    }
                    await ConnectWithIdentityAsync(endpoint, elevatedIdentity, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    StatusCode statusCode = ex switch
                    {
                        ServiceResultException serviceResultException => serviceResultException.StatusCode,
                        OperationCanceledException => StatusCodes.BadRequestCancelledByClient,
                        _ => StatusCodes.BadNoCommunication
                    };
                    if (requiresConfirmation)
                    {
                        m_logger.ConfigurationFileConfirmationPending(ex);
                    }
                    else
                    {
                        m_logger.ConfigurationFileUpdateOutcomeUnknown(ex);
                    }
                    return new ConfigurationFileUpdateResult(
                        requiresConfirmation
                            ? ConfigurationFileUpdateOutcome.ConfirmationPending
                            : ConfigurationFileUpdateOutcome.OutcomeUnknown,
                        response.newVersion,
                        response.updateId,
                        response.updateResults,
                        statusCode);
                }

                if (!requiresConfirmation)
                {
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.Applied,
                        response.newVersion,
                        response.updateId,
                        response.updateResults,
                        StatusCodes.Good);
                }

                try
                {
                    ISession reconnectedSession = Session ??
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    ServerConfigurationTypeClient reconnectedServerConfiguration =
                        GetConfigurationOwner(reconnectedSession, configurationId);
                    ApplicationConfigurationFileTypeClient? reconnectedFile =
                        await reconnectedServerConfiguration.GetConfigurationFileAsync(MessageContext.Telemetry, ct)
                            .ConfigureAwait(false);
                    if (reconnectedFile == null)
                    {
                        return new ConfigurationFileUpdateResult(
                            ConfigurationFileUpdateOutcome.ConfirmationPending,
                            response.newVersion,
                            response.updateId,
                            response.updateResults,
                            StatusCodes.BadNotSupported);
                    }

                    await reconnectedFile.ConfirmUpdateAsync(response.updateId, ct).ConfigureAwait(false);
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.Confirmed,
                        response.newVersion,
                        response.updateId,
                        response.updateResults,
                        StatusCodes.Good);
                }
                catch (ServiceResultException ex) when (IsCommunicationFailure(ex.StatusCode))
                {
                    m_logger.ConfigurationFileUpdateOutcomeUnknown(ex);
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.OutcomeUnknown,
                        response.newVersion,
                        response.updateId,
                        response.updateResults,
                        ex.StatusCode);
                }
                catch (OperationCanceledException ex)
                {
                    m_logger.ConfigurationFileUpdateOutcomeUnknown(ex);
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.OutcomeUnknown,
                        response.newVersion,
                        response.updateId,
                        response.updateResults,
                        StatusCodes.BadRequestCancelledByClient);
                }
                catch (Exception ex)
                {
                    StatusCode statusCode = ex is ServiceResultException serviceResultException
                        ? serviceResultException.StatusCode
                        : StatusCodes.BadUnexpectedError;
                    m_logger.ConfigurationFileConfirmationPending(ex);
                    return new ConfigurationFileUpdateResult(
                        ConfigurationFileUpdateOutcome.ConfirmationPending,
                        response.newVersion,
                        response.updateId,
                        response.updateResults,
                        statusCode);
                }
            }
            catch
            {
                if (!closeAndUpdateInvoked)
                {
                    try
                    {
                        await file.CloseAsync(fileHandle, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.FailedToCloseConfigurationFileHandle(ex);
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Resolves an application address in the active namespace table without falling back to the host.
        /// </summary>
        private ServerConfigurationTypeClient GetConfigurationOwner(ISession session, ExpandedNodeId configurationId)
        {
            if (configurationId.IsNull)
            {
                return m_serverConfiguration ?? throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            NodeId nodeId = ExpandedNodeId.ToNodeId(configurationId, session.NamespaceUris);
            if (nodeId.IsNull || nodeId.NamespaceIndex == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    "The selected application's namespace is not present on this session.");
            }
            return new ServerConfigurationTypeClient(session, nodeId, MessageContext.Telemetry);
        }

        /// <inheritdoc/>
        public async ValueTask CancelChangesAsync(CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                await m_serverConfiguration!.CancelChangesAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DeleteCertificateAsync(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                await m_serverConfiguration!.DeleteCertificateAsync(
                    certificateGroupId,
                    certificateTypeId,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> CreateSelfSignedCertificateAsync(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> dnsNames,
            ArrayOf<string> ipAddresses,
            ushort lifetimeInDays,
            ushort keySizeInBits,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity? oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                return await m_serverConfiguration!.CreateSelfSignedCertificateAsync(
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    dnsNames,
                    ipAddresses,
                    lifetimeInDays,
                    keySizeInBits,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        private ValueTask<NodeId> GetRelatedTrustListIdByCertificateGroupIdAsync(
            NodeId certificateGroupId,
            CancellationToken ct = default)
        {
            // A null group means the DefaultApplicationGroup (Part 12 7.10). The
            // default-group overloads read DefaultApplicationGroup before the
            // first connect has assigned it, so Null must map here as well.
            // Only the well-known groups in namespace 0 have a fixed TrustList id;
            // a vendor group in another namespace with the same numeric identifier
            // is browsed like any other group.
            bool isNamespaceZero = certificateGroupId.NamespaceIndex == 0;
            if (certificateGroupId.IsNull ||
                (isNamespaceZero && certificateGroupId ==
                    Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup))
            {
                return new ValueTask<NodeId>(ExpandedNodeId.ToNodeId(Ua.ObjectIds
                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, Session!.NamespaceUris));
            }

            if (isNamespaceZero && certificateGroupId ==
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup)
            {
                return new ValueTask<NodeId>(ExpandedNodeId.ToNodeId(Ua.ObjectIds
                    .ServerConfiguration_CertificateGroups_DefaultHttpsGroup_TrustList, Session!.NamespaceUris));
            }

            if (isNamespaceZero && certificateGroupId ==
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup)
            {
                return new ValueTask<NodeId>(ExpandedNodeId.ToNodeId(Ua.ObjectIds
                    .ServerConfiguration_CertificateGroups_DefaultUserTokenGroup_TrustList, Session!.NamespaceUris));
            }

            return FindChildByTypeDefinitionAsync(
                ExpandedNodeId.ToNodeId(certificateGroupId, Session!.NamespaceUris),
                Ua.ObjectTypeIds.TrustListType,
                ct);
        }

        private async ValueTask<NodeId> FindChildByTypeDefinitionAsync(
            NodeId parentNodeId,
            NodeId targetTypeDefinitionId,
            CancellationToken ct = default)
        {
            var browseDescription = new BrowseDescription
            {
                NodeId = parentNodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            };

            BrowseResponse results = await Session!.BrowseAsync(
                null, null, 0, [browseDescription], ct).ConfigureAwait(false);

            ReferenceDescription? reference = results.Results.ToList()
                .Where(r => StatusCode.IsGood(r.StatusCode))
                .SelectMany(r => r.References.ToList())
                .FirstOrDefault(r => r.TypeDefinition == targetTypeDefinitionId);

            if (reference != null)
            {
                return ExpandedNodeId.ToNodeId(reference.NodeId, Session!.NamespaceUris);
            }

            throw new ServiceResultException(
                StatusCodes.BadNotFound,
                $"Could not find child with TypeDefinition {targetTypeDefinitionId} under {parentNodeId}");
        }

        private async Task<IUserIdentity?> ElevatePermissionsAsync(
            ISession session,
            CancellationToken ct = default)
        {
            if (m_requestContext != null)
            {
                ct.ThrowIfCancellationRequested();
                return session.Identity;
            }
            IUserIdentity? oldUser = session.Identity;

            if (AdminCredentials == null || !ReferenceEquals(session.Identity, AdminCredentials))
            {
                IUserIdentity? newCredentials = null;

                if (AdminCredentials == null)
                {
                    EventHandler<AdminCredentialsRequiredEventArgs> handle =
                        AdminCredentialsRequired
                        ?? throw new InvalidOperationException(
                            "The operation requires administrator credentials.");
                    var args = new AdminCredentialsRequiredEventArgs();
                    handle(this, args);
                    newCredentials = args.Credentials;

                    if (args.CacheCredentials)
                    {
                        AdminCredentials = args.Credentials;
                    }
                }
                else
                {
                    newCredentials = AdminCredentials;
                }

                try
                {
                    await session.UpdateSessionAsync(newCredentials!, PreferredLocales, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    AdminCredentials = null;
                    throw;
                }
            }

            return oldUser;
        }

        /// <summary>
        /// Returns whether any per-target update result is not Good.
        /// </summary>
        private static bool ContainsBadTargetResult(ArrayOf<StatusCode> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (!StatusCode.IsGood(results[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns whether a service status can leave a remote operation's outcome unknown.
        /// </summary>
        private static bool IsCommunicationFailure(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadTimeout ||
                statusCode == StatusCodes.BadRequestTimeout ||
                statusCode == StatusCodes.BadCommunicationError ||
                statusCode == StatusCodes.BadConnectionClosed ||
                statusCode == StatusCodes.BadSecureChannelClosed ||
                statusCode == StatusCodes.BadNoCommunication ||
                statusCode == StatusCodes.BadServerHalted;
        }

        private static async ValueTask<uint> ReadConfigurationFileVersionAsync(
            ISession session,
            ApplicationConfigurationFileTypeClient file,
            CancellationToken ct)
        {
            var paths = ArrayOf.Wrapped(
            [
                new BrowsePath
                {
                    StartingNode = file.ObjectId,
                    RelativePath = new RelativePath
                    {
                        Elements = ArrayOf.Wrapped(
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("CurrentVersion")
                            }
                        ])
                    }
                }
            ]);
            TranslateBrowsePathsToNodeIdsResponse translated =
                await session.TranslateBrowsePathsToNodeIdsAsync(null, paths, ct).ConfigureAwait(false);
            if (translated.Results.Count == 0 ||
                StatusCode.IsBad(translated.Results[0].StatusCode) ||
                translated.Results[0].Targets.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The ConfigurationFile node does not expose CurrentVersion.");
            }

            NodeId versionNodeId = ExpandedNodeId.ToNodeId(
                translated.Results[0].Targets[0].TargetId,
                session.NamespaceUris);
            if (versionNodeId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            DataValue value = await session.ReadValueAsync(versionNodeId, ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            if (!value.WrappedValue.TryGetValue(out uint version))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }
            return version;
        }

        private async Task RevertPermissionsAsync(
            ISession session,
            IUserIdentity? oldUser,
            CancellationToken ct = default)
        {
            if (m_requestContext != null)
            {
                return;
            }
            try
            {
                if (!ReferenceEquals(session.Identity, oldUser))
                {
                    await session.UpdateSessionAsync(oldUser!, PreferredLocales, ct)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                m_logger.ErrorRevertingToNormalPermissions(e);
            }
        }

        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            if (m_disposed)
            {
                return;
            }

            if (!ReferenceEquals(session, Session))
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
                m_logger.SubscriberThrewInKeepAliveHandler(exception);
            }

            RaiseServerStatusChanged(e);
        }

        /// <summary>
        /// Raises <see cref="ServerStatusChanged"/> when the server state
        /// changes, and once for the state the first keep-alive reports.
        /// </summary>
        /// <remarks>
        /// The session keep-alive already reads <c>Server_ServerStatus_State</c>
        /// from the server on every interval - on both subscription engines, and
        /// the first one is sent immediately after connect - so the state is
        /// available without this client creating a subscription of its own.
        /// </remarks>
        private void RaiseServerStatusChanged(KeepAliveEventArgs e)
        {
            if (e == null || m_lastServerState == e.CurrentState)
            {
                return;
            }
            m_lastServerState = e.CurrentState;

            try
            {
                ServerStatusChanged?.Invoke(
                    this,
                    new ServerStatusChangedEventArgs(e.Status, e.CurrentState, e.CurrentTime));
            }
            catch (Exception exception)
            {
                m_logger.SubscriberThrewInServerStatusChangedHandler(exception);
            }
        }

        private void RaiseConnectionStatusChangedEvent()
        {
            EventHandler? callback = ConnectionStatusChanged;

            if (callback != null)
            {
                try
                {
                    callback(this, EventArgs.Empty);
                }
                catch (Exception exception)
                {
                    m_logger.UnexpectedErrorRaisingConnectionStatusChangedEvent(exception);
                }
            }
        }

        /// <summary>
        /// Connect the session
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The advertised endpoint URL is missing, or discovery changed the requested transport.
        /// </exception>
        internal async Task ConnectInternalAsync(
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            Uri? expectedEndpoint,
            CancellationToken ct)
        {
            await ConnectInternalAsync(
                endpoint,
                updateBeforeConnect,
                expectedEndpoint,
                AdminCredentials,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Connects using the supplied identity instead of the client's cached administrator credentials.
        /// </summary>
        /// <param name="endpoint">The configured endpoint.</param>
        /// <param name="updateBeforeConnect">Whether to refresh endpoint discovery before connecting.</param>
        /// <param name="expectedEndpoint">The endpoint URI whose host and transport must be preserved.</param>
        /// <param name="userIdentity">The identity used to create the session.</param>
        /// <param name="ct">A token used to cancel the connection.</param>
        internal async Task ConnectInternalAsync(
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            Uri? expectedEndpoint,
            IUserIdentity? userIdentity,
            CancellationToken ct)
        {
            RequireOwnedConnection();
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Uri advertisedEndpoint = endpoint.EndpointUrl ??
                    throw new ServiceResultException(StatusCodes.BadTcpEndpointUrlInvalid);
                expectedEndpoint ??= advertisedEndpoint;
                if (!string.Equals(
                    expectedEndpoint.Scheme, advertisedEndpoint.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "The discovered endpoint changed the requested transport.");
                }
                endpoint = CoreUtils.Clone(endpoint)!;
                // Refresh discovery at the configured host, not a server-advertised replacement.
                endpoint.Description.EndpointUrl = new UriBuilder(advertisedEndpoint)
                {
                    Host = expectedEndpoint.IdnHost,
                    Port = expectedEndpoint.Port
                }.Uri.AbsoluteUri;
                endpoint.Description.Server.DiscoveryUrls = [expectedEndpoint.AbsoluteUri];

                Session?.Dispose();
                Session = null;

                Session = await m_sessionFactory.CreateAsync(
                    Configuration,
                    endpoint,
                    updateBeforeConnect,
                    checkDomain: true,
                    Configuration.ApplicationName!,
                    (uint)m_options.SessionTimeout.TotalMilliseconds,
                    userIdentity,
                    PreferredLocales,
                    ct)
                .ConfigureAwait(false);

                m_lastServerState = null;
                Session.KeepAlive += Session_KeepAlive;

                if (!Session.Factory.ContainsEncodeableType(Ua.DataTypeIds.TrustListDataType))
                {
                    Session.Factory.Builder.AddOpcUaGds().Commit();
                }

                m_endpoint = Session.ConfiguredEndpoint;

                RaiseConnectionStatusChangedEvent();

                InitializeSessionClients(Session);

                m_logger.ConnectedToEndpoint(EndpointUrl);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Reconnects with retry using the identity that performed the update.
        /// </summary>
        /// <param name="endpoint">The configured endpoint to reconnect to.</param>
        /// <param name="userIdentity">The identity used to create the session.</param>
        /// <param name="ct">A token used to cancel the connection.</param>
        private async ValueTask ConnectWithIdentityAsync(
            ConfiguredEndpoint endpoint,
            IUserIdentity? userIdentity,
            CancellationToken ct)
        {
            int maxAttempts = m_options.MaxConnectAttempts;
            int backoffMs = (int)m_options.ConnectBackoff.TotalMilliseconds;
            ServiceResultException? lastException = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    await ConnectInternalAsync(
                        endpoint,
                        updateBeforeConnect: true,
                        expectedEndpoint: null,
                        userIdentity: userIdentity,
                        ct: ct).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException e) when (
                    e.StatusCode == StatusCodes.BadServerHalted ||
                    e.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    e.StatusCode == StatusCodes.BadNoCommunication)
                {
                    lastException = e;
                    m_logger.FailedToConnect(e, attempt + 1);
                    if (attempt + 1 < maxAttempts)
                    {
                        await m_timeProvider.Delay(TimeSpan.FromMilliseconds(backoffMs), ct).ConfigureAwait(false);
                    }
                }
            }
            throw lastException ??
                ServiceResultException.Create(
                    StatusCodes.BadNoCommunication,
                    "Failed to connect after {0} attempts.",
                    maxAttempts);
        }

        /// <summary>
        /// Connect the client if not connected
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task<ISession> ConnectIfNeededAsync(CancellationToken ct)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ServerPushConfigurationClient));
            }
            if (m_requestContext != null)
            {
                ct.ThrowIfCancellationRequested();
                if (m_requestContext.Invalidated.IsCompleted)
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }
                return m_requestContext.Session;
            }
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

        /// <summary>
        /// Resolves standard group identifiers and the generated proxy on one selected session.
        /// </summary>
        private void InitializeSessionClients(ISession session)
        {
            DefaultApplicationGroup = ExpandedNodeId.ToNodeId(
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup, session.NamespaceUris);
            DefaultHttpsGroup = ExpandedNodeId.ToNodeId(
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup, session.NamespaceUris);
            DefaultUserTokenGroup = ExpandedNodeId.ToNodeId(
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup, session.NamespaceUris);
            m_serverConfiguration = new ServerConfigurationTypeClient(
                session, ExpandedNodeId.ToNodeId(Ua.ObjectIds.ServerConfiguration, session.NamespaceUris),
                MessageContext.Telemetry);
        }

        /// <summary>
        /// Rejects connection management on a borrowed generation before discovery or remote side effects.
        /// </summary>
        private void RequireOwnedConnection()
        {
            if (m_requestContext != null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "A borrowed push configuration client cannot manage the caller's connection.");
            }
        }

        /// <summary>The locally owned capture, when the caller owns the connection and authorized identity.</summary>
        private readonly SessionRequestContext? m_requestContext;

        private readonly SemaphoreSlim m_lock = new(1, 1);
        private readonly ISessionFactory m_sessionFactory;
        private readonly ILogger m_logger;
        private readonly GdsClientOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly CancellationTokenSource m_disposeCts = new();
        private ConfiguredEndpoint? m_endpoint;
        private ServerState? m_lastServerState;
        private ServerConfigurationTypeClient? m_serverConfiguration;
        private bool m_disposed;
    }

    internal static partial class ServerPushConfigurationClientLog
    {
        [LoggerMessage(EventId = GdsClientCommonEventIds.ServerPushConfigurationClient + 0, Level = LogLevel.Debug,
            Message = "Failed to close trust list file handle.")]
        public static partial void FailedToCloseTrustListFileHandle(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = GdsClientCommonEventIds.ServerPushConfigurationClient + 1, Level = LogLevel.Error,
            Message = "Error reverting to normal permissions.")]
        public static partial void ErrorRevertingToNormalPermissions(this ILogger logger, Exception e);

        [LoggerMessage(EventId = GdsClientCommonEventIds.ServerPushConfigurationClient + 2, Level = LogLevel.Error,
            Message = "Unexpected error raising ConnectionStatusChanged event.")]
        public static partial void UnexpectedErrorRaisingConnectionStatusChangedEvent(
            this ILogger logger,
            Exception exception);

        /// <summary>
        /// Logs that the client could not determine the update outcome.
        /// </summary>
        [LoggerMessage(
            EventId = GdsClientCommonEventIds.ServerPushConfigurationClient + 3,
            Level = LogLevel.Warning,
            Message = "The ConfigurationFile update outcome is unknown.")]
        public static partial void ConfigurationFileUpdateOutcomeUnknown(this ILogger logger, Exception exception);

        /// <summary>
        /// Logs that an applied update could not be confirmed by the client.
        /// </summary>
        [LoggerMessage(
            EventId = GdsClientCommonEventIds.ServerPushConfigurationClient + 4,
            Level = LogLevel.Warning,
            Message = "The ConfigurationFile update was applied but could not be confirmed.")]
        public static partial void ConfigurationFileConfirmationPending(this ILogger logger, Exception exception);

        /// <summary>
        /// Logs a failure to close a ConfigurationFile upload handle.
        /// </summary>
        [LoggerMessage(
            EventId = GdsClientCommonEventIds.ServerPushConfigurationClient + 5,
            Level = LogLevel.Debug,
            Message = "Failed to close the ConfigurationFile upload handle.")]
        public static partial void FailedToCloseConfigurationFileHandle(this ILogger logger, Exception exception);
    }
}
