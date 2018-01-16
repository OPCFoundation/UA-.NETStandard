/* ========================================================================
 * Copyright (c) 2005-2017 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Threading.Tasks;
using System.Reflection;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// A class that provides access to a Global Discovery Server.
    /// </summary>
    public class GlobalDiscoveryServerClient
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalDiscoveryServerClient"/> class.
        /// </summary>
        /// <param name="application">The application.</param>
        public GlobalDiscoveryServerClient(
            ApplicationInstance application, 
            string endpointUrl,
            IUserIdentity adminUserIdentity = null)
        {
            m_application = application;
            m_endpointUrl = endpointUrl;
            // preset admin 
            m_adminCredentials = adminUserIdentity;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the application.
        /// </summary>
        /// <value>
        /// The application.
        /// </value>
        public ApplicationInstance Application
        {
            get { return m_application; }
        }

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
        /// Raised when admin credentials are required.
        /// </summary>
        public event AdminCredentialsRequiredEventHandler AdminCredentialsRequired;

        /// <summary>
        /// Gets the session.
        /// </summary>
        /// <value>
        /// The session.
        /// </value>
        public Session Session
        {
            get { return m_session; }
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
        /// Gets a value indicating whether a session is connected.
        /// </summary>
        /// <value>
        ///   <c>true</c> if [is connected]; otherwise, <c>false</c>.
        /// </value>
        public bool IsConnected { get { return m_session != null && m_session.Connected; } }
        #endregion

        #region Public Methods
        /// <summary>
        /// Selects the default GDS.
        /// </summary>
        /// <param name="lds">The LDS to use.</param>
        /// <returns>
        /// TRUE if successful; FALSE otherwise.
        /// </returns>
        public List<string> GetDefaultGdsUrls(LocalDiscoveryServerClient lds)
        {
            List<string> gdsUrls = new List<string>();

            try
            {
                DateTime lastResetTime;

                if (lds == null)
                {
                    lds = new LocalDiscoveryServerClient(this.Application.ApplicationConfiguration);
                }

                var servers = lds.FindServersOnNetwork(0, 1000, out lastResetTime);

                foreach (var server in servers)
                {
                    if (server.ServerCapabilities != null && server.ServerCapabilities.Contains(ServerCapability.GlobalDiscoveryServer))
                    {
                        gdsUrls.Add(server.DiscoveryUrl);
                    }
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Unexpected error connecting to LDS");
            }

            return gdsUrls;
        }

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
                throw new ArgumentException(endpointUrl + " is not a valid URL.", "endpointUrl");
            }

            bool serverHalted = false;
            do
            {
                serverHalted = false;
                try
                {
                    EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(endpointUrl, true);
                    EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_application.ApplicationConfiguration);
                    ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

                    await Connect(endpoint);
                }
                catch (ServiceResultException e)
                {
                    if (e.StatusCode == StatusCodes.BadServerHalted)
                    {
                        serverHalted = true;
                        await Task.Delay(1000);
                    }
                    else
                    {
                        throw e;
                    }
                }
            } while (serverHalted);
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
                    throw new ArgumentNullException("endpoint");
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
                AdminCredentials,
                m_preferredLocales);

            m_endpoint = m_session.ConfiguredEndpoint;

            m_session.SessionClosing += Session_SessionClosing;
            m_session.KeepAlive += Session_KeepAlive;
            m_session.KeepAlive += KeepAlive;

            if (m_session.Factory.GetSystemType(Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType) == null)
            {
                m_session.Factory.AddEncodeableTypes(typeof(Opc.Ua.Gds.ObjectIds).GetTypeInfo().Assembly);
            }

            m_session.ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;
            m_endpointUrl = m_session.ConfiguredEndpoint.EndpointUrl.ToString();

        }

        public void Disconnect()
        {
            if (m_session != null)
            {
                KeepAlive?.Invoke(m_session, null);
                m_session.Close();
                m_session = null;
            }
        }

        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            if (ServiceResult.IsBad(e.Status))
            {
                m_session?.Dispose();
                m_session = null;
            }
        }

        private void Session_SessionClosing(object sender, EventArgs e)
        {
            m_session.Dispose();
            m_session = null;
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

        #region GDS Methods
        /// <summary>
        /// Finds the applications with the specified application uri.
        /// </summary>
        /// <param name="applicationUri">The application URI.</param>
        /// <returns>The matching application.</returns>
        public ApplicationRecordDataType[] FindApplication(string applicationUri)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_FindApplications, m_session.NamespaceUris),
                applicationUri);

            ApplicationRecordDataType[] applications = null;

            if (outputArguments.Count > 0)
            {
                applications = (ApplicationRecordDataType[])ExtensionObject.ToArray(outputArguments[0] as ExtensionObject[], typeof(ApplicationRecordDataType));
            }

            return applications;
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
        /// <returns>A enumarator used to access the results.</returns>
        public IList<ServerOnNetwork> QueryServers(
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities)
        {
            return QueryServers(
                0,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities);
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
        /// <returns>A enumarator used to access the results.</returns>
        public IList<ServerOnNetwork> QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_QueryServers, m_session.NamespaceUris),
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities);

            ServerOnNetwork[] servers = null;

            if (outputArguments.Count > 1)
            {
                servers = (ServerOnNetwork[])ExtensionObject.ToArray(outputArguments[1] as ExtensionObject[], typeof(ServerOnNetwork));
            }

            return servers;
        }

        /// <summary>
        /// Get the application record.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <returns>The application record for the specified application id.</returns>
        public ApplicationRecordDataType GetApplication(NodeId applicationId)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_GetApplication, m_session.NamespaceUris),
                applicationId);

            if (outputArguments.Count > 0)
            {
                return ExtensionObject.ToEncodeable(outputArguments[0] as ExtensionObject) as ApplicationRecordDataType;
            }

            return null;
        }

        /// <summary>
        /// Registers the application.
        /// </summary>
        /// <param name="application">The application.</param>
        /// <returns>The application id assigned to the application.</returns>
        public NodeId RegisterApplication(ApplicationRecordDataType application)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_RegisterApplication, m_session.NamespaceUris),
                application);

            if (outputArguments.Count > 0)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Updates the application.
        /// </summary>
        /// <param name="application">The application.</param>
        public void UpdateApplication(ApplicationRecordDataType application)
        {
            if (!IsConnected)
            {
                Connect();
            }

            m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_UpdateApplication, m_session.NamespaceUris),
                application);
        }

        /// <summary>
        /// Unregisters the application.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        public void UnregisterApplication(NodeId applicationId)
        {
            if (!IsConnected)
            {
                Connect();
            }

            m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_UnregisterApplication, m_session.NamespaceUris),
                applicationId);
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
        public NodeId StartNewKeyPairRequest(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            IList<string> domainNames,
            string privateKeyFormat,
            string privateKeyPassword)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_StartNewKeyPairRequest, m_session.NamespaceUris),
                applicationId,
                certificateGroupId,
                certificateTypeId,
                subjectName,
                domainNames,
                privateKeyFormat,
                privateKeyPassword);

            if (outputArguments.Count > 0)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Signs the certificate.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificate">The certificate to renew.</param>
        /// <returns>The id for the request which is used to check when it is approved.</returns>
        public NodeId StartSigningRequest(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificateRequest)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_StartSigningRequest, m_session.NamespaceUris),
                applicationId,
                certificateGroupId,
                certificateTypeId,
                certificateRequest);

            if (outputArguments.Count > 0)
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
        public byte[] FinishRequest(
            NodeId applicationId,
            NodeId requestId,
            out byte[] privateKey,
            out byte[][] issuerCertificates)
        {
            privateKey = null;
            issuerCertificates = null;

            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_FinishRequest, m_session.NamespaceUris),
                applicationId,
                requestId);

            byte[] certificate = null;

            if (outputArguments.Count > 0)
            {
                certificate = outputArguments[0] as byte[];
            }

            if (outputArguments.Count > 1)
            {
                privateKey = outputArguments[1] as byte[];
            }

            if (outputArguments.Count > 2)
            {
                issuerCertificates = outputArguments[2] as byte[][];
            }

            return certificate;
        }

        /// <summary>
        /// Gets the certificate groups.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <returns></returns>
        public NodeId[] GetCertificateGroups(
            NodeId applicationId)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_GetCertificateGroups, m_session.NamespaceUris),
                applicationId);

            if (outputArguments.Count > 0)
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
        /// <returns></returns>
        public NodeId GetTrustList(
            NodeId applicationId,
            NodeId certificateGroupId)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_GetTrustList, m_session.NamespaceUris),
                applicationId,
                certificateGroupId);

            if (outputArguments.Count > 0)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Gets the certificate status.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">Type of the trust list.</param>
        /// <returns></returns>
        public Boolean GetCertificateStatus(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_GetCertificateStatus, m_session.NamespaceUris),
                applicationId,
                certificateGroupId,
                certificateTypeId);

            if (outputArguments.Count > 0 && outputArguments[0] != null)
            {
                Boolean? result = outputArguments[0] as Boolean?;
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
        public TrustListDataType ReadTrustList(NodeId trustListId)
        {
            if (!IsConnected)
            {
                Connect();
            }

            var outputArguments = m_session.Call(
                trustListId,
                Opc.Ua.MethodIds.FileType_Open,
                (byte)OpenFileMode.Read);

            uint fileHandle = (uint)outputArguments[0];
            MemoryStream ostrm = new MemoryStream();

            try
            {
                while (true)
                {
                    int length = 4096;

                    outputArguments = m_session.Call(
                        trustListId,
                        Opc.Ua.MethodIds.FileType_Read,
                        fileHandle,
                        length);

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
                if (IsConnected)
                {
                    m_session.Call(
                        trustListId,
                        Opc.Ua.MethodIds.FileType_Close,
                        fileHandle);
                }
            }

            ostrm.Position = 0;

            BinaryDecoder decoder = new BinaryDecoder(ostrm, m_session.MessageContext);
            TrustListDataType trustList = new TrustListDataType();
            trustList.Decode(decoder);
            decoder.Close();
            ostrm.Close();

            return trustList;
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
                if (Object.ReferenceEquals(m_session.Identity, m_adminCredentials))
                {
                    m_session.UpdateSession(oldUser, m_preferredLocales);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Error reverting to normal permissions.");
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
