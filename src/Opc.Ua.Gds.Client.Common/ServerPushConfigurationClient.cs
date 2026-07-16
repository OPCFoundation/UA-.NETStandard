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

        /// <summary>
        /// Occurs when the server status changes.
        /// </summary>
#pragma warning disable CS0067
        public event MonitoredItemNotificationEventHandler? ServerStatusChanged;
#pragma warning restore CS0067

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
                    Session?.Dispose();
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
                            ct).ConfigureAwait(false);
                    var endpointConfiguration = EndpointConfiguration.Create(Configuration);
                    var endpoint = new ConfiguredEndpoint(
                        null,
                        endpointDescription!,
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
            endpoint ??= m_endpoint ?? throw new ArgumentNullException(nameof(endpoint));

            int maxAttempts = m_options.MaxConnectAttempts;
            int backoffMs = (int)m_options.ConnectBackoff.TotalMilliseconds;
            ServiceResultException? lastException = null;
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
        public async ValueTask DisconnectAsync(CancellationToken ct = default)
        {
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
            if (AdminCredentials == null || Endpoint == null)
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
        /// <param name="certificateGroupId">The identifier for the CertificateGroup.</param>
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

            try
            {
                return await m_serverConfiguration!.UpdateCertificateAsync(
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
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
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
            NodeId normalizedCertificateGroupId = certificateGroupId.WithNamespaceIndex(0);
            if (normalizedCertificateGroupId ==
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup)
            {
                return new ValueTask<NodeId>(ExpandedNodeId.ToNodeId(Ua.ObjectIds
                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, Session!.NamespaceUris));
            }

            if (normalizedCertificateGroupId ==
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup)
            {
                return new ValueTask<NodeId>(ExpandedNodeId.ToNodeId(Ua.ObjectIds
                    .ServerConfiguration_CertificateGroups_DefaultHttpsGroup_TrustList, Session!.NamespaceUris));
            }

            if (normalizedCertificateGroupId ==
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

        private async Task RevertPermissionsAsync(
            ISession session,
            IUserIdentity? oldUser,
            CancellationToken ct = default)
        {
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

            // Re-raise the public KeepAlive event synchronously on the same callback
            // thread to preserve original ordering for subscribers.
            try
            {
                if (ReferenceEquals(session, Session))
                {
                    KeepAlive?.Invoke(session, e);
                }
            }
            catch (Exception exception)
            {
                m_logger.SubscriberThrewInKeepAliveHandler(exception);
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

                Session = await m_sessionFactory.CreateAsync(
                    Configuration,
                    endpoint,
                    updateBeforeConnect,
                    false,
                    Configuration.ApplicationName!,
                    (uint)m_options.SessionTimeout.TotalMilliseconds,
                    AdminCredentials,
                    PreferredLocales,
                    ct)
                .ConfigureAwait(false);

                Session.KeepAlive += Session_KeepAlive;

                if (!Session.Factory.ContainsEncodeableType(Ua.DataTypeIds.TrustListDataType))
                {
                    Session.Factory.Builder.AddOpcUaGds().Commit();
                }

                m_endpoint = Session.ConfiguredEndpoint;

                RaiseConnectionStatusChangedEvent();

                // init some helpers
                DefaultApplicationGroup = ExpandedNodeId.ToNodeId(
                    Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    Session.NamespaceUris);
                DefaultHttpsGroup = ExpandedNodeId.ToNodeId(
                    Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup,
                    Session.NamespaceUris);
                DefaultUserTokenGroup = ExpandedNodeId.ToNodeId(
                    Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup,
                    Session.NamespaceUris);

                m_serverConfiguration = new ServerConfigurationTypeClient(
                    Session,
                    ExpandedNodeId.ToNodeId(Ua.ObjectIds.ServerConfiguration, Session.NamespaceUris),
                    MessageContext.Telemetry);

                m_logger.ConnectedToEndpoint(EndpointUrl);
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
        private readonly TimeProvider m_timeProvider;
        private readonly CancellationTokenSource m_disposeCts = new();
        private ConfiguredEndpoint? m_endpoint;
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
    }
}
