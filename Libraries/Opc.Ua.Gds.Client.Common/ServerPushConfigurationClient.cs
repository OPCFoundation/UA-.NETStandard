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
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// A class used to access the Push Configuration information model.
    /// </summary>
    public class ServerPushConfigurationClient
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerPushConfigurationClient"/> class.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="sessionFactory">Used to create session to the server.</param>
        public ServerPushConfigurationClient(
            ApplicationConfiguration configuration,
            ISessionFactory sessionFactory = null)
        {
            Configuration = configuration;
            m_sessionFactory = sessionFactory ?? DefaultSessionFactory.Instance;
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
        /// <value>
        /// The application instance.
        /// </value>
        public ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// Gets or sets the admin credentials.
        /// </summary>
        /// <value>
        /// The admin credentials.
        /// </value>
        public IUserIdentity AdminCredentials { get; set; }

        /// <summary>
        /// Gets or sets the endpoint URL.
        /// </summary>
        /// <value>
        /// The endpoint URL.
        /// </value>
        public string EndpointUrl { get; set; }

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
        /// <value>
        /// The preferred locales.
        /// </value>
        public string[] PreferredLocales { get; set; }

        /// <summary>
        /// Gets a value indicating whether the session is connected.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the session is connected; otherwise, <c>false</c>.
        /// </value>
        public bool IsConnected => Session != null && Session.Connected;

        /// <summary>
        /// Gets the session.
        /// </summary>
        /// <value>
        /// The session.
        /// </value>
        public ISession Session { get; private set; }

        /// <summary>
        /// Gets the endpoint.
        /// </summary>
        /// <value>
        /// The endpoint.
        /// </value>
        /// <exception cref="InvalidOperationException"></exception>
        public ConfiguredEndpoint Endpoint
        {
            get
            {
                if (Session != null && Session.ConfiguredEndpoint != null)
                {
                    return Session.ConfiguredEndpoint;
                }

                return m_endpoint;
            }
            set
            {
                if (Session != null)
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
        public Task ConnectAsync()
        {
            return ConnectAsync(m_endpoint);
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
        /// <exception cref="ArgumentNullException">endpointUrl</exception>
        /// <exception cref="ArgumentException">endpointUrl</exception>
        public async Task ConnectAsync(string endpointUrl)
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

            EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(
                Configuration,
                endpointUrl,
                true);
            var endpointConfiguration = EndpointConfiguration.Create(Configuration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            await ConnectAsync(endpoint).ConfigureAwait(false);
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
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        public async Task ConnectAsync(ConfiguredEndpoint endpoint)
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

            if (Session != null)
            {
                Session.Dispose();
                Session = null;
            }

            Session = await m_sessionFactory
                .CreateAsync(
                    Configuration,
                    endpoint,
                    false,
                    false,
                    Configuration.ApplicationName,
                    60000,
                    AdminCredentials,
                    PreferredLocales)
                .ConfigureAwait(false);

            m_endpoint = Session.ConfiguredEndpoint;

            if (Session.Factory.GetSystemType(Ua.DataTypeIds.TrustListDataType) == null)
            {
                Session.Factory.AddEncodeableTypes(typeof(Ua.DataTypeIds).GetTypeInfo().Assembly);
            }

            Session.KeepAlive += Session_KeepAlive;
            Session.KeepAlive += KeepAlive;

            RaiseConnectionStatusChangedEvent();

            Session.ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;

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
        }

        /// <summary>
        /// Disconnects this instance.
        /// </summary>
        public void Disconnect()
        {
            if (Session != null)
            {
                KeepAlive?.Invoke(Session, null);
                Session.Close();
                Session = null;
                RaiseConnectionStatusChangedEvent();
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
                    Utils.LogError(
                        exception,
                        "Unexpected error raising ConnectionStatusChanged event.");
                }
            }
        }

        /// <summary>
        /// Gets the supported key formats.
        /// </summary>
        /// <exception cref="InvalidOperationException">Connection to server is not active.</exception>
        public string[] GetSupportedKeyFormats()
        {
            if (AdminCredentials == null || Endpoint == null)
            {
                return null;
            }

            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                var nodesToRead = new ReadValueIdCollection
                {
                    new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(
                            Ua.VariableIds.ServerConfiguration_SupportedPrivateKeyFormats,
                            Session.NamespaceUris
                        ),
                        AttributeId = Attributes.Value
                    }
                };

                Session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    out DataValueCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                return results[0].GetValue<string[]>(null);
            }
            finally
            {
                RevertPermissions(oldUser);
            }
        }

        /// <summary>
        /// Reads the trust list.
        /// </summary>
        public TrustListDataType ReadTrustList(TrustListMasks masks = TrustListMasks.All)
        {
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                System.Collections.Generic.IList<object> outputArguments = Session.Call(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                        Session.NamespaceUris
                    ),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds
                            .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks,
                        Session.NamespaceUris
                    ),
                    (uint)masks);

                uint fileHandle = (uint)outputArguments[0];
                using var ostrm = new MemoryStream();
                try
                {
                    while (true)
                    {
                        const int length = 256;

                        outputArguments = Session.Call(
                            ExpandedNodeId.ToNodeId(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                                Session.NamespaceUris
                            ),
                            ExpandedNodeId.ToNodeId(
                                Ua.MethodIds
                                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Read,
                                Session.NamespaceUris
                            ),
                            fileHandle,
                            length);

                        byte[] bytes = (byte[])outputArguments[0];
                        ostrm.Write(bytes, 0, bytes.Length);

                        if (length != bytes.Length)
                        {
                            break;
                        }
                    }

                    Session.Call(
                        ExpandedNodeId.ToNodeId(
                            Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                            Session.NamespaceUris
                        ),
                        ExpandedNodeId.ToNodeId(
                            Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close,
                            Session.NamespaceUris
                        ),
                        fileHandle);
                }
                catch (Exception)
                {
                    if (IsConnected)
                    {
                        Session.Call(
                            ExpandedNodeId.ToNodeId(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                                Session.NamespaceUris
                            ),
                            ExpandedNodeId.ToNodeId(
                                Ua.MethodIds
                                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close,
                                Session.NamespaceUris
                            ),
                            fileHandle);
                    }

                    throw;
                }

                ostrm.Position = 0;

                var trustList = new TrustListDataType();
                using (var decoder = new BinaryDecoder(ostrm, Session.MessageContext))
                {
                    trustList.Decode(decoder);
                }

                return trustList;
            }
            finally
            {
                RevertPermissions(oldUser);
            }
        }

        /// <summary>
        /// Updates the trust list.
        /// </summary>
        public bool UpdateTrustList(TrustListDataType trustList)
        {
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                using var strm = new MemoryStream();
                using (var encoder = new BinaryEncoder(strm, Session.MessageContext, true))
                {
                    encoder.WriteEncodeable(null, trustList, null);
                }
                strm.Position = 0;

                System.Collections.Generic.IList<object> outputArguments = Session.Call(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                        Session.NamespaceUris
                    ),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Open,
                        Session.NamespaceUris
                    ),
                    (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting));

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

                        Session.Call(
                            ExpandedNodeId.ToNodeId(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                                Session.NamespaceUris
                            ),
                            ExpandedNodeId.ToNodeId(
                                Ua.MethodIds
                                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Write,
                                Session.NamespaceUris
                            ),
                            fileHandle,
                            buffer);
                    }

                    outputArguments = Session.Call(
                        ExpandedNodeId.ToNodeId(
                            Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                            Session.NamespaceUris
                        ),
                        ExpandedNodeId.ToNodeId(
                            Ua.MethodIds
                                .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate,
                            Session.NamespaceUris
                        ),
                        fileHandle);

                    return (bool)outputArguments[0];
                }
                catch (Exception)
                {
                    if (IsConnected)
                    {
                        Session.Call(
                            ExpandedNodeId.ToNodeId(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                                Session.NamespaceUris
                            ),
                            ExpandedNodeId.ToNodeId(
                                Ua.MethodIds
                                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close,
                                Session.NamespaceUris
                            ),
                            fileHandle);
                    }

                    throw;
                }
            }
            finally
            {
                RevertPermissions(oldUser);
            }
        }

        /// <summary>
        /// Add certificate.
        /// </summary>
        public void AddCertificate(X509Certificate2 certificate, bool isTrustedCertificate)
        {
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();
            try
            {
                Session.Call(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                        Session.NamespaceUris
                    ),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds
                            .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate,
                        Session.NamespaceUris
                    ),
                    certificate.RawData,
                    isTrustedCertificate);
            }
            finally
            {
                RevertPermissions(oldUser);
            }
        }

        /// <summary>
        /// Remove certificate.
        /// </summary>
        public void RemoveCertificate(string thumbprint, bool isTrustedCertificate)
        {
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();
            try
            {
                Session.Call(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                        Session.NamespaceUris
                    ),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds
                            .ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate,
                        Session.NamespaceUris
                    ),
                    thumbprint,
                    isTrustedCertificate);
            }
            finally
            {
                RevertPermissions(oldUser);
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
        public void GetCertificates(
            NodeId certificateGroupId,
            out NodeId[] certificateTypeIds,
            out byte[][] certificates)
        {
            certificateTypeIds = [];
            certificates = [];
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();
            try
            {
                System.Collections.Generic.IList<object> outputArguments = Session.Call(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration,
                        Session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfigurationType_GetCertificates,
                        Session.NamespaceUris
                    ),
                    certificateGroupId);
                if (outputArguments.Count >= 2)
                {
                    certificateTypeIds = outputArguments[0] as NodeId[];
                    certificates = outputArguments[1] as byte[][];
                }
            }
            finally
            {
                RevertPermissions(oldUser);
            }
        }

        /// <summary>
        /// Creates the CSR.
        /// </summary>
        /// <param name="certificateGroupId">The certificate group identifier.</param>
        /// <param name="certificateTypeId">The certificate type identifier.</param>
        /// <param name="subjectName">Name of the subject.</param>
        /// <param name="regeneratePrivateKey">if set to <c>true</c> [regenerate private key].</param>
        /// <param name="nonce">The nonce.</param>
        public byte[] CreateSigningRequest(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            byte[] nonce)
        {
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                System.Collections.Generic.IList<object> outputArguments = Session.Call(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration,
                        Session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfiguration_CreateSigningRequest,
                        Session.NamespaceUris
                    ),
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    regeneratePrivateKey,
                    nonce);

                if (outputArguments.Count > 0)
                {
                    return (byte[])outputArguments[0];
                }

                return null;
            }
            finally
            {
                RevertPermissions(oldUser);
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
        public bool UpdateCertificate(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificate,
            string privateKeyFormat,
            byte[] privateKey,
            byte[][] issuerCertificates)
        {
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                System.Collections.Generic.IList<object> outputArguments = Session.Call(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration,
                        Session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfiguration_UpdateCertificate,
                        Session.NamespaceUris),
                    certificateGroupId,
                    certificateTypeId,
                    certificate,
                    issuerCertificates,
                    privateKeyFormat,
                    privateKey);

                if (outputArguments.Count > 0)
                {
                    return (bool)outputArguments[0];
                }

                return false;
            }
            finally
            {
                RevertPermissions(oldUser);
            }
        }

        /// <summary>
        /// Reads the rejected  list.
        /// </summary>
        public X509Certificate2Collection GetRejectedList()
        {
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                System.Collections.Generic.IList<object> outputArguments = Session.Call(
                    ExpandedNodeId.ToNodeId(
                        Ua.ObjectIds.ServerConfiguration,
                        Session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(
                        Ua.MethodIds.ServerConfiguration_GetRejectedList,
                        Session.NamespaceUris));

                byte[][] rawCertificates = (byte[][])outputArguments[0];
                var collection = new X509Certificate2Collection();
                foreach (byte[] rawCertificate in rawCertificates)
                {
                    collection.Add(X509CertificateLoader.LoadCertificate(rawCertificate));
                }
                return collection;
            }
            finally
            {
                RevertPermissions(oldUser);
            }
        }

        /// <summary>
        /// Restarts this instance.
        /// </summary>
        public void ApplyChanges()
        {
            if (!IsConnected)
            {
                ConnectAsync().GetAwaiter().GetResult();
            }

            ElevatePermissions();

            Session.Call(
                ExpandedNodeId.ToNodeId(Ua.ObjectIds.ServerConfiguration, Session.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    Ua.MethodIds.ServerConfiguration_ApplyChanges,
                    Session.NamespaceUris));
        }

        private IUserIdentity ElevatePermissions()
        {
            IUserIdentity oldUser = Session.Identity;

            if (AdminCredentials == null || !ReferenceEquals(Session.Identity, AdminCredentials))
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
                    Session.UpdateSession(newCredentials, PreferredLocales);
                }
                catch (Exception)
                {
                    AdminCredentials = null;
                    throw;
                }
            }

            return oldUser;
        }

        private void RevertPermissions(IUserIdentity oldUser)
        {
            try
            {
                if (!ReferenceEquals(Session.Identity, oldUser))
                {
                    Session.UpdateSession(oldUser, PreferredLocales);
                }
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Error reverting to normal permissions.");
            }
        }

        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            if (!ReferenceEquals(session, Session))
            {
                return;
            }

            KeepAliveEventHandler callback = KeepAlive;

            if (callback != null)
            {
                try
                {
                    callback(session, e);
                }
                catch (Exception exception)
                {
                    Utils.LogError(exception, "Unexpected error raising KeepAlive event.");
                }
            }
        }

        private readonly ISessionFactory m_sessionFactory;
        private ConfiguredEndpoint m_endpoint;
    }
}
