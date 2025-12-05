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
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// A class used to access the Push Configuration information model.
    /// </summary>
    public class ServerPushConfigurationClient : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerPushConfigurationClient"/> class.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="sessionFactory">Used to create session to the server.</param>
        /// <param name="diagnosticsMasks">Return diagnostics to use for all requests</param>
        public ServerPushConfigurationClient(
            ApplicationConfiguration configuration,
            ISessionFactory sessionFactory = null,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None)
        {
            Configuration = configuration;
            MessageContext = configuration.CreateMessageContext();
            m_logger = MessageContext.Telemetry.CreateLogger<ServerPushConfigurationClient>();
            m_sessionFactory = sessionFactory ??
                new DefaultSessionFactory(MessageContext.Telemetry)
                {
                    ReturnDiagnostics = diagnosticsMasks
                };
        }

        public NodeId DefaultApplicationGroup { get; private set; }
        public NodeId DefaultHttpsGroup { get; private set; }
        public NodeId DefaultUserTokenGroup { get; private set; }

        // TODO: currently only sha256 cert is supported
        public NodeId ApplicationCertificateType
            => Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;

        /// <summary>
        /// Gets the application instance.
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
        /// Raised when admin credentials are required.
        /// </summary>
        public event EventHandler<AdminCredentialsRequiredEventArgs> AdminCredentialsRequired;

        /// <summary>
        /// Raised when the connection status changes.
        /// </summary>
        public event EventHandler ConnectionStatusChanged;

        /// <summary>
        /// Gets or sets the preferred locales.
        /// </summary>
        public string[] PreferredLocales { get; set; }

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
        /// Occurs when keep alive occurs.
        /// </summary>
        public event KeepAliveEventHandler KeepAlive;

        /// <summary>
        /// Occurs when the server status changes.
        /// </summary>
#pragma warning disable CS0067
        public event MonitoredItemNotificationEventHandler ServerStatusChanged;
#pragma warning restore CS0067

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
        protected virtual void Dispose(bool disposing)
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
                    RaiseConnectionStatusChangedEvent();
                }
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Gets the supported key formats.
        /// </summary>
        /// <exception cref="InvalidOperationException">Connection to server is not active.</exception>
        [Obsolete("Use GetSupportedKeyFormatsAsync instead.")]
        public string[] GetSupportedKeyFormats()
        {
            return GetSupportedKeyFormatsAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gets the supported key formats.
        /// </summary>
        /// <exception cref="InvalidOperationException">Connection to server is not active.</exception>
        public async Task<string[]> GetSupportedKeyFormatsAsync(CancellationToken ct = default)
        {
            if (AdminCredentials == null || Endpoint == null)
            {
                return null;
            }

            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                var nodesToRead = new ReadValueIdCollection
                {
                    new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(
                            Ua.VariableIds.ServerConfiguration_SupportedPrivateKeyFormats,
                            session.NamespaceUris
                        ),
                        AttributeId = Attributes.Value
                    }
                };

                ReadResponse result = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    ct).ConfigureAwait(false);

                ClientBase.ValidateResponse(result.Results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(result.DiagnosticInfos, nodesToRead);
                return result.Results[0].GetValue<string[]>(null);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the trust list.
        /// </summary>
        [Obsolete("Use ReadTrustListAsync instead.")]
        public TrustListDataType ReadTrustList(TrustListMasks masks = TrustListMasks.All)
        {
            return ReadTrustListAsync(masks).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the trust list.
        /// </summary>
        public async Task<TrustListDataType> ReadTrustListAsync(
            TrustListMasks masks = TrustListMasks.All,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                System.Collections.Generic.IList<object> outputArguments = await session.CallAsync(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                        session.NamespaceUris
                    ),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds
                            .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks,
                        session.NamespaceUris
                    ),
                    ct,
                    (uint)masks)
                    .ConfigureAwait(false);

                uint fileHandle = (uint)outputArguments[0];
                using var ostrm = new MemoryStream();
                try
                {
                    while (true)
                    {
                        const int length = 256;

                        outputArguments = await session.CallAsync(
                            ExpandedNodeId.ToNodeId(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                                session.NamespaceUris
                            ),
                            ExpandedNodeId.ToNodeId(
                                Ua.MethodIds
                                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Read,
                                session.NamespaceUris
                            ),
                            ct,
                            fileHandle,
                            length
                            )
                            .ConfigureAwait(false);

                        byte[] bytes = (byte[])outputArguments[0];
                        ostrm.Write(bytes, 0, bytes.Length);

                        if (length != bytes.Length)
                        {
                            break;
                        }
                    }

                    await session.CallAsync(
                        ExpandedNodeId.ToNodeId(
                            Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                            session.NamespaceUris
                        ),
                        ExpandedNodeId.ToNodeId(
                            Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close,
                            session.NamespaceUris
                        ),
                        ct,
                        fileHandle)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    if (IsConnected)
                    {
                        await session.CallAsync(
                            ExpandedNodeId.ToNodeId(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                                session.NamespaceUris
                            ),
                            ExpandedNodeId.ToNodeId(
                                Ua.MethodIds
                                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close,
                                session.NamespaceUris
                            ),
                            ct,
                            fileHandle)
                            .ConfigureAwait(false);
                    }

                    throw;
                }

                ostrm.Position = 0;

                var trustList = new TrustListDataType();
                using (var decoder = new BinaryDecoder(ostrm, session.MessageContext))
                {
                    trustList.Decode(decoder);
                }

                return trustList;
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates the trust list.
        /// </summary>
        [Obsolete("Use UpdateTrustListAsync instead.")]
        public bool UpdateTrustList(TrustListDataType trustList)
        {
            return UpdateTrustListAsync(trustList).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the trust list.
        /// </summary>
        public async Task<bool> UpdateTrustListAsync(TrustListDataType trustList, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                using var strm = new MemoryStream();
                using (var encoder = new BinaryEncoder(strm, session.MessageContext, true))
                {
                    encoder.WriteEncodeable(null, trustList, null);
                }
                strm.Position = 0;

                System.Collections.Generic.IList<object> outputArguments = await session.CallAsync(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                        session.NamespaceUris
                    ),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Open,
                        session.NamespaceUris
                    ),
                    ct,
                    (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting)).ConfigureAwait(false);

                uint fileHandle = (uint)outputArguments[0];

                try
                {
                    bool writing = true;
                    byte[] buffer = new byte[256];

                    while (writing)
                    {
                        int bytesWritten = strm.Read(buffer, 0, buffer.Length);

                        if (bytesWritten != buffer.Length)
                        {
                            byte[] copy = new byte[bytesWritten];
                            Array.Copy(buffer, copy, bytesWritten);
                            buffer = copy;
                            writing = false;
                        }

                        await session.CallAsync(
                            ExpandedNodeId.ToNodeId(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                                session.NamespaceUris
                            ),
                            ExpandedNodeId.ToNodeId(
                                Ua.MethodIds
                                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Write,
                                session.NamespaceUris
                            ),
                            ct,
                            fileHandle,
                            buffer).ConfigureAwait(false);
                    }

                    outputArguments = await session.CallAsync(
                        ExpandedNodeId.ToNodeId(
                            Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                            session.NamespaceUris
                        ),
                        ExpandedNodeId.ToNodeId(
                            Ua.MethodIds
                                .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate,
                            session.NamespaceUris
                        ),
                        ct,
                        fileHandle).ConfigureAwait(false);

                    return (bool)outputArguments[0];
                }
                catch (Exception)
                {
                    if (IsConnected)
                    {
                        await session.CallAsync(
                            ExpandedNodeId.ToNodeId(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                                session.NamespaceUris
                            ),
                            ExpandedNodeId.ToNodeId(
                                Ua.MethodIds
                                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close,
                                session.NamespaceUris
                            ),
                            ct,
                            fileHandle).ConfigureAwait(false);
                    }

                    throw;
                }
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Add certificate.
        /// </summary>
        [Obsolete("Use AddCertificateAsync instead.")]
        public void AddCertificate(X509Certificate2 certificate, bool isTrustedCertificate)
        {
            AddCertificateAsync(certificate, isTrustedCertificate).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Add certificate.
        /// </summary>
        public async Task AddCertificateAsync(X509Certificate2 certificate, bool isTrustedCertificate, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);
            try
            {
                await session.CallAsync(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                        session.NamespaceUris
                    ),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds
                            .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate,
                        session.NamespaceUris
                    ),
                    ct,
                    certificate.RawData,
                    isTrustedCertificate).ConfigureAwait(false);
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Remove certificate.
        /// </summary>
        [Obsolete("Use RemoveCertificateAsync instead.")]
        public void RemoveCertificate(string thumbprint, bool isTrustedCertificate)
        {
            RemoveCertificateAsync(thumbprint, isTrustedCertificate).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Remove certificate.
        /// </summary>
        public async Task RemoveCertificateAsync(string thumbprint, bool isTrustedCertificate, CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);
            try
            {
                await session.CallAsync(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                        session.NamespaceUris
                    ),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds
                            .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate,
                        session.NamespaceUris
                    ),
                    ct,
                    thumbprint,
                    isTrustedCertificate).ConfigureAwait(false);
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
        /// <param name="certificateTypeIds">The CertificateTypes that currently have a Certificate assigned.
        ///The length of this list is the same as the length as certificates list.
        ///An empty list if the CertificateGroup does not have any CertificateTypes.</param>
        /// <param name="certificates">A list of DER encoded Certificates assigned to CertificateGroup.
        ///The certificateType for the Certificate is specified by the corresponding element in the certificateTypes parameter.</param>
        [Obsolete("Use GetCertificatesAsync instead.")]
        public void GetCertificates(
            NodeId certificateGroupId,
            out NodeId[] certificateTypeIds,
            out byte[][] certificates)
        {
            (certificateTypeIds, certificates) = GetCertificatesAsync(
                certificateGroupId,
                CancellationToken.None)
                .GetAwaiter().GetResult();
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
        public async Task<(NodeId[] certificateTypeIds, byte[][] certificates)> GetCertificatesAsync(
            NodeId certificateGroupId,
            CancellationToken ct = default)
        {
            NodeId[] certificateTypeIds = [];
            byte[][] certificates = [];

            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);
            try
            {
                System.Collections.Generic.IList<object> outputArguments = await session.CallAsync(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration,
                        session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfigurationType_GetCertificates,
                        session.NamespaceUris
                    ),
                    ct,
                    certificateGroupId).ConfigureAwait(false);
                if (outputArguments.Count >= 2)
                {
                    certificateTypeIds = outputArguments[0] as NodeId[];
                    certificates = outputArguments[1] as byte[][];
                }
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
            return (certificateTypeIds, certificates);
        }

        /// <summary>
        /// Creates the CSR.
        /// </summary>
        /// <param name="certificateGroupId">The certificate group identifier.</param>
        /// <param name="certificateTypeId">The certificate type identifier.</param>
        /// <param name="subjectName">Name of the subject.</param>
        /// <param name="regeneratePrivateKey">if set to <c>true</c> [regenerate private key].</param>
        /// <param name="nonce">The nonce.</param>
        [Obsolete("Use CreateSigningRequestAsync instead.")]
        public byte[] CreateSigningRequest(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            byte[] nonce)
        {
            return CreateSigningRequestAsync(
                certificateGroupId,
                certificateTypeId,
                subjectName,
                regeneratePrivateKey,
                nonce,
                CancellationToken.None).GetAwaiter().GetResult();
        }
        /// <summary>
        /// Creates the CSR.
        /// </summary>
        /// <param name="certificateGroupId">The certificate group identifier.</param>
        /// <param name="certificateTypeId">The certificate type identifier.</param>
        /// <param name="subjectName">Name of the subject.</param>
        /// <param name="regeneratePrivateKey">if set to <c>true</c> [regenerate private key].</param>
        /// <param name="nonce">The nonce.</param>
        /// <param name="ct">The cancellationtoken</param>
        public async Task<byte[]> CreateSigningRequestAsync(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            byte[] nonce,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                System.Collections.Generic.IList<object> outputArguments = await session.CallAsync(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration,
                        session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfiguration_CreateSigningRequest,
                        session.NamespaceUris
                    ),
                    ct,
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    regeneratePrivateKey,
                    nonce).ConfigureAwait(false);

                if (outputArguments.Count > 0)
                {
                    return (byte[])outputArguments[0];
                }

                return null;
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates the certificate.
        /// </summary>
        /// <param name="certificateGroupId">The group of the trust list.</param>
        /// <param name="certificateTypeId">The type of the trust list.</param>
        /// <param name="certificate">The certificate.</param>
        /// <param name="privateKeyFormat">The format of the private key, PFX or PEM.</param>
        /// <param name="privateKey">The private ky.</param>
        /// <param name="issuerCertificates">An array containing the chain of issuer certificates.</param>
        [Obsolete("Use UpdateCertificateAsync instead.")]
        public bool UpdateCertificate(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificate,
            string privateKeyFormat,
            byte[] privateKey,
            byte[][] issuerCertificates)
        {
            return UpdateCertificateAsync(
                certificateGroupId,
                certificateTypeId,
                certificate,
                privateKeyFormat,
                privateKey,
                issuerCertificates).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the certificate.
        /// </summary>
        /// <param name="certificateGroupId">The group of the trust list.</param>
        /// <param name="certificateTypeId">The type of the trust list.</param>
        /// <param name="certificate">The certificate.</param>
        /// <param name="privateKeyFormat">The format of the private key, PFX or PEM.</param>
        /// <param name="privateKey">The private ky.</param>
        /// <param name="issuerCertificates">An array containing the chain of issuer certificates.</param>
        /// <param name="ct">The cancellationToken</param>
        public async Task<bool> UpdateCertificateAsync(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificate,
            string privateKeyFormat,
            byte[] privateKey,
            byte[][] issuerCertificates,
            CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                System.Collections.Generic.IList<object> outputArguments = await session.CallAsync(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration,
                        session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfiguration_UpdateCertificate,
                        session.NamespaceUris),
                    ct,
                    certificateGroupId,
                    certificateTypeId,
                    certificate,
                    issuerCertificates,
                    privateKeyFormat,
                    privateKey).ConfigureAwait(false);

                if (outputArguments.Count > 0)
                {
                    return (bool)outputArguments[0];
                }

                return false;
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the rejected  list.
        /// </summary>
        [Obsolete("Use GetRejectedListAsync instead.")]
        public X509Certificate2Collection GetRejectedList()
        {
            return GetRejectedListAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the rejected  list.
        /// </summary>
        public async Task<X509Certificate2Collection> GetRejectedListAsync(CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            IUserIdentity oldUser = await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            try
            {
                System.Collections.Generic.IList<object> outputArguments = await session.CallAsync(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration,
                        session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfiguration_GetRejectedList,
                        session.NamespaceUris),
                    ct).ConfigureAwait(false);

                byte[][] rawCertificates = (byte[][])outputArguments[0];
                var collection = new X509Certificate2Collection();
                foreach (byte[] rawCertificate in rawCertificates)
                {
                    collection.Add(CertificateFactory.Create(rawCertificate));
                }
                return collection;
            }
            finally
            {
                await RevertPermissionsAsync(session, oldUser, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Restarts this instance.
        /// </summary>
        [Obsolete("Use ApplyChangesAsync instead.")]
        public void ApplyChanges()
        {
            ApplyChangesAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Restarts this instance.
        /// </summary>
        public async Task ApplyChangesAsync(CancellationToken ct = default)
        {
            ISession session = await ConnectIfNeededAsync(ct).ConfigureAwait(false);
            await ElevatePermissionsAsync(session, ct).ConfigureAwait(false);

            await session.CallAsync(
                ExpandedNodeId.ToNodeId(Ua.ObjectIds.ServerConfiguration, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    Ua.MethodIds.ServerConfiguration_ApplyChanges,
                    session.NamespaceUris),
                ct).ConfigureAwait(false);
        }

        private async Task<IUserIdentity> ElevatePermissionsAsync(
            ISession session,
            CancellationToken ct = default)
        {
            IUserIdentity oldUser = session.Identity;

            if (AdminCredentials == null || !ReferenceEquals(session.Identity, AdminCredentials))
            {
                IUserIdentity newCredentials = null;

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
                    await session.UpdateSessionAsync(newCredentials, PreferredLocales, ct)
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
            IUserIdentity oldUser,
            CancellationToken ct = default)
        {
            try
            {
                if (!ReferenceEquals(session.Identity, oldUser))
                {
                    await session.UpdateSessionAsync(oldUser, PreferredLocales, ct)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Error reverting to normal permissions.");
            }
        }

        private async void Session_KeepAliveAsync(ISession session, KeepAliveEventArgs e)
        {
            if (m_disposed)
            {
                return;
            }
            try
            {
                await m_lock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!ReferenceEquals(session, Session))
                    {
                        return;
                    }
                }
                finally
                {
                    m_lock.Release();
                }
                KeepAlive?.Invoke(session, e);
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Unexpected error in Session_KeepAlive.");
            }
        }

        private void RaiseConnectionStatusChangedEvent()
        {
            EventHandler callback = ConnectionStatusChanged;

            if (callback != null)
            {
                try
                {
                    callback(this, EventArgs.Empty);
                }
                catch (Exception exception)
                {
                    m_logger.LogError(
                        exception,
                        "Unexpected error raising ConnectionStatusChanged event.");
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

                Session.KeepAlive += Session_KeepAliveAsync;
                Session.KeepAlive += KeepAlive;

                if (Session.Factory.GetSystemType(Ua.DataTypeIds.TrustListDataType) == null)
                {
                    Session.Factory.AddEncodeableTypes(typeof(Ua.DataTypeIds).GetTypeInfo().Assembly);
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
