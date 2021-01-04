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
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// A class used to access the Push Configuration information model.
    /// </summary>
    public class ServerPushConfigurationClient
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerPushConfigurationClient"/> class.
        /// </summary>
        /// <param name="application">The application.</param>
        public ServerPushConfigurationClient(ApplicationInstance application)
        {
            m_application = application;
        }
        #endregion

        #region Public Properties
        public NodeId DefaultApplicationGroup { get; private set; }
        public NodeId DefaultHttpsGroup { get; private set; }
        public NodeId DefaultUserTokenGroup { get; private set; }
        // TODO: currently only sha256 cert is supported
        public NodeId ApplicationCertificateType => Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;

        /// <summary>
        /// Gets the application instance.
        /// </summary>
        /// <value>
        /// The application instance.
        /// </value>
        public ApplicationInstance Application => m_application;

        /// <summary>
        /// Gets or sets the admin credentials.
        /// </summary>
        /// <value>
        /// The admin credentials.
        /// </value>
        public IUserIdentity AdminCredentials
        {
            get { return m_adminCredentials; }
            set { m_adminCredentials = value; }
        }

        /// <summary>
        /// Gets or sets the endpoint URL.
        /// </summary>
        /// <value>
        /// The endpoint URL.
        /// </value>
        public string EndpointUrl
        {
            get { return m_endpointUrl; }
            set { m_endpointUrl = value; }
        }

        /// <summary>
        /// Raised when admin credentials are required.
        /// </summary>
        public event AdminCredentialsRequiredEventHandler AdminCredentialsRequired;

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
        public string[] PreferredLocales
        {
            get { return m_preferredLocales; }
            set { m_preferredLocales = value; }
        }

        /// <summary>
        /// Gets a value indicating whether the session is connected.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the session is connected; otherwise, <c>false</c>.
        /// </value>
        public bool IsConnected => m_session != null && m_session.Connected;

        /// <summary>
        /// Gets the session.
        /// </summary>
        /// <value>
        /// The session.
        /// </value>
        public Session Session => m_session;

        /// <summary>
        /// Gets the endpoint.
        /// </summary>
        /// <value>
        /// The endpoint.
        /// </value>
        public ConfiguredEndpoint Endpoint
        {
            get
            {
                if (m_session != null && m_session.ConfiguredEndpoint != null)
                {
                    return m_session.ConfiguredEndpoint;
                }

                return m_endpoint;
            }

            set
            {
                if (m_session != null)
                {
                    throw new InvalidOperationException("Session must be closed before changing endpoint.");
                }

                if (value == null || m_endpoint == null || value.EndpointUrl != m_endpoint.EndpointUrl)
                {
                    m_adminCredentials = null;
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
        public event MonitoredItemNotificationEventHandler ServerStatusChanged;
        #endregion

        #region Public Methods
        /// <summary>
        /// Connects using the default endpoint.
        /// </summary>
        public void Connect()
        {
            Connect(m_endpoint).Wait();
        }

        /// <summary>
        /// Connects the specified endpoint URL.
        /// </summary>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <exception cref="System.ArgumentNullException">endpointUrl</exception>
        /// <exception cref="System.ArgumentException">endpointUrl</exception>
        public async Task Connect(string endpointUrl)
        {
            if (String.IsNullOrEmpty(endpointUrl))
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            if (!Uri.IsWellFormedUriString(endpointUrl, UriKind.Absolute))
            {
                throw new ArgumentException(endpointUrl + " is not a valid URL.", nameof(endpointUrl));
            }

            EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(endpointUrl, true);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_application.ApplicationConfiguration);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            await Connect(endpoint);
        }

        /// <summary>
        /// Connects the specified endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        public async Task Connect(ConfiguredEndpoint endpoint)
        {
            if (endpoint != null && m_endpoint != null && endpoint.EndpointUrl != m_endpoint.EndpointUrl)
            {
                m_adminCredentials = null;
            }

            if (endpoint == null)
            {
                endpoint = m_endpoint;

                if (endpoint == null)
                {
                    throw new ArgumentNullException(nameof(endpoint));
                }
            }

            if (m_session != null)
            {
                m_session.Dispose();
                m_session = null;
            }

            m_session = await Session.Create(
                m_application.ApplicationConfiguration,
                endpoint,
                false,
                false,
                m_application.ApplicationName,
                60000,
                m_adminCredentials,
                m_preferredLocales);

            m_endpoint = m_session.ConfiguredEndpoint;

            if (m_session.Factory.GetSystemType(Opc.Ua.DataTypeIds.TrustListDataType) == null)
            {
                m_session.Factory.AddEncodeableTypes(typeof(Opc.Ua.DataTypeIds).GetTypeInfo().Assembly);
            }

            m_session.KeepAlive += Session_KeepAlive;
            m_session.KeepAlive += KeepAlive;

            RaiseConnectionStatusChangedEvent();

            m_session.ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;

            // init some helpers
            DefaultApplicationGroup = ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup, m_session.NamespaceUris);
            DefaultHttpsGroup = ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup, m_session.NamespaceUris);
            DefaultUserTokenGroup = ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup, m_session.NamespaceUris);
        }

        /// <summary>
        /// Disconnects this instance.
        /// </summary>
        public void Disconnect()
        {
            if (m_session != null)
            {
                KeepAlive?.Invoke(m_session, null);
                m_session.Close();
                m_session = null;
                RaiseConnectionStatusChangedEvent();
            }
        }

        private void RaiseConnectionStatusChangedEvent()
        {
            var Callback = ConnectionStatusChanged;

            if (Callback != null)
            {
                try
                {
                    Callback(this, EventArgs.Empty);
                }
                catch (Exception exception)
                {
                    Utils.Trace(exception, "Unexpected error raising ConnectionStatusChanged event.");
                }
            }
        }

        /// <summary>
        /// Gets the supported key formats.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Connection to server is not active.</exception>
        public string[] GetSupportedKeyFormats()
        {
            if (AdminCredentials == null || Endpoint == null)
            {
                return null;
            }

            if (!IsConnected)
            {
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection
                {
                    new ReadValueId()
                    {
                        NodeId = ExpandedNodeId.ToNodeId(Opc.Ua.VariableIds.ServerConfiguration_SupportedPrivateKeyFormats, m_session.NamespaceUris),
                        AttributeId = Attributes.Value
                    }
                };

                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

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
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                var outputArguments = m_session.Call(
                    ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks, m_session.NamespaceUris),
                    (uint)masks);

                uint fileHandle = (uint)outputArguments[0];
                MemoryStream ostrm = new MemoryStream();

                try
                {
                    while (true)
                    {
                        int length = 256;

                        outputArguments = m_session.Call(
                            ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                            ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Read, m_session.NamespaceUris),
                            fileHandle,
                            length);

                        byte[] bytes = (byte[])outputArguments[0];
                        ostrm.Write(bytes, 0, bytes.Length);

                        if (length != bytes.Length)
                        {
                            break;
                        }
                    }

                    m_session.Call(
                        ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                        ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close, m_session.NamespaceUris),
                        fileHandle);
                }
                catch (Exception)
                {
                    if (IsConnected)
                    {
                        m_session.Call(
                            ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                            ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close, m_session.NamespaceUris),
                            fileHandle);
                    }

                    throw;
                }

                ostrm.Position = 0;

                BinaryDecoder decoder = new BinaryDecoder(ostrm, m_session.MessageContext);
                TrustListDataType trustList = new TrustListDataType();
                trustList.Decode(decoder);
                decoder.Close();
                ostrm.Close();

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
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                MemoryStream strm = new MemoryStream();
                BinaryEncoder encoder = new BinaryEncoder(strm, m_session.MessageContext);
                encoder.WriteEncodeable(null, trustList, null);
                strm.Position = 0;

                var outputArguments = m_session.Call(
                    ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Open, m_session.NamespaceUris),
                    (byte)(OpenFileMode.Write | OpenFileMode.EraseExisting));

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

                        m_session.Call(
                            ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                            ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Write, m_session.NamespaceUris),
                            fileHandle,
                            buffer);
                    }

                    outputArguments = m_session.Call(
                        ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                        ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate, m_session.NamespaceUris),
                        fileHandle);

                    return (bool)outputArguments[0];
                }
                catch (Exception)
                {
                    if (IsConnected)
                    {
                        m_session.Call(
                            ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                            ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_Close, m_session.NamespaceUris),
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
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();
            try
            {
                m_session.Call(
                    ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate, m_session.NamespaceUris),
                    certificate.RawData,
                    isTrustedCertificate
                    );
            }
            finally
            {
                RevertPermissions(oldUser);
            }
        }

        /// <summary>
        /// Add certificate.
        /// </summary>
        public void AddCrl(X509CRL crl, bool isTrustedCertificate)
        {
            if (!IsConnected)
            {
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();
            try
            {
                m_session.Call(
                    ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate, m_session.NamespaceUris),
                    crl.RawData,
                    isTrustedCertificate
                    );
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
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();
            try
            {
                m_session.Call(
                    ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList, m_session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate, m_session.NamespaceUris),
                    thumbprint,
                    isTrustedCertificate
                    );
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
        /// <returns></returns>
        public byte[] CreateSigningRequest(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            byte[] nonce)
        {
            if (!IsConnected)
            {
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                var outputArguments = m_session.Call(
                    ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration, m_session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_CreateSigningRequest, m_session.NamespaceUris),
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
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                var outputArguments = m_session.Call(
                    ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration, m_session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_UpdateCertificate, m_session.NamespaceUris),
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
                Connect();
            }

            IUserIdentity oldUser = ElevatePermissions();

            try
            {
                var outputArguments = m_session.Call(
                    ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration, m_session.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_GetRejectedList, m_session.NamespaceUris)
                    );

                byte[][] rawCertificates = (byte[][])outputArguments[0];
                X509Certificate2Collection collection = new X509Certificate2Collection();
                foreach (var rawCertificate in rawCertificates)
                {
                    collection.Add(new X509Certificate2(rawCertificate));
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
                Connect();
            }

            ElevatePermissions();

            m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.ObjectIds.ServerConfiguration, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.MethodIds.ServerConfiguration_ApplyChanges, m_session.NamespaceUris));
        }
        #endregion

        #region Private Methods
        private IUserIdentity ElevatePermissions()
        {
            IUserIdentity oldUser = m_session.Identity;

            if (m_adminCredentials == null || !Object.ReferenceEquals(m_session.Identity, m_adminCredentials))
            {
                IUserIdentity newCredentials = null;

                if (m_adminCredentials == null)
                {
                    var handle = AdminCredentialsRequired;

                    if (handle == null)
                    {
                        throw new InvalidOperationException("The operation requires administrator credentials.");
                    }

                    var args = new AdminCredentialsRequiredEventArgs();
                    handle(this, args);
                    newCredentials = args.Credentials;

                    if (args.CacheCredentials)
                    {
                        m_adminCredentials = args.Credentials;
                    }
                }
                else
                {
                    newCredentials = m_adminCredentials;
                }

                try
                {
                    m_session.UpdateSession(newCredentials, m_preferredLocales);
                }
                catch (Exception)
                {
                    m_adminCredentials = null;
                    throw;
                }
            }

            return oldUser;
        }

        private void RevertPermissions(IUserIdentity oldUser)
        {
            try
            {
                if (!Object.ReferenceEquals(m_session.Identity, oldUser))
                {
                    m_session.UpdateSession(oldUser, m_preferredLocales);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Error reverting to normal permissions.");
            }
        }

        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            if (!Object.ReferenceEquals(session, m_session))
            {
                return;
            }

            var Callback = KeepAlive;

            if (Callback != null)
            {
                try
                {
                    Callback(session, e);
                }
                catch (Exception exception)
                {
                    Utils.Trace(exception, "Unexpected error raising KeepAlive event.");
                }
            }
        }

        private void ServerStatus_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (!Object.ReferenceEquals(monitoredItem.Subscription.Session, m_session))
            {
                return;
            }

            var Callback = ServerStatusChanged;

            if (Callback != null)
            {
                try
                {
                    Callback(monitoredItem, e);
                }
                catch (Exception exception)
                {
                    Utils.Trace(exception, "Unexpected error raising KeepAlive event.");
                }
            }
        }
        #endregion

        #region Private Fields
        private ApplicationInstance m_application;
        private ConfiguredEndpoint m_endpoint;
        private string m_endpointUrl;
        private string[] m_preferredLocales;
        private Session m_session;
        private IUserIdentity m_adminCredentials;
        #endregion
    }
}
