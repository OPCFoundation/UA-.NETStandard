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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Pkcs;


namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// A node manager for a global discovery server
    /// </summary>
    public class ApplicationsNodeManager : CustomNodeManager2
    {
        NodeId DefaultApplicationGroupId;
        NodeId DefaultHttpsGroupId;
        NodeId DefaultUserTokenGroupId;

        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ApplicationsNodeManager(IServerInternal server, ApplicationConfiguration configuration, IApplicationsDatabase database)
            : base(server, configuration)
        {
            List<string> namespaceUris = new List<string>
            {
                "http://opcfoundation.org/UA/GDS/applications/",
                Opc.Ua.Gds.Namespaces.OpcUaGds
            };
            NamespaceUris = namespaceUris;

            SystemContext.NodeIdFactory = this;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<GlobalDiscoveryServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new GlobalDiscoveryServerConfiguration();
            }

            if (!String.IsNullOrEmpty(m_configuration.DefaultSubjectNameContext))
            {
                if (m_configuration.DefaultSubjectNameContext[0] != ',')
                {
                    m_configuration.DefaultSubjectNameContext = "," + m_configuration.DefaultSubjectNameContext;
                }
            }

            DefaultApplicationGroupId = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, Server.NamespaceUris);
            DefaultHttpsGroupId = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup, Server.NamespaceUris);
            DefaultUserTokenGroupId = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup, Server.NamespaceUris);

            m_database = database;
            m_certificateGroups = new Dictionary<NodeId, CertificateGroup>();

            try
            {
                var results = m_database.QueryServers(0, 5, null, null, null, null, out DateTime lastResetTime);
                Utils.Trace("QueryServers Returned: {0} records", results.Length);

                foreach (var result in results)
                {
                    Utils.Trace("Server Found at {0}", result.DiscoveryUrl);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not connect to the Database!");

                var ie = e.InnerException;

                while (ie != null)
                {
                    Utils.Trace(ie, "");
                    ie = ie.InnerException;
                }

                Utils.Trace("Initialize Database tables!");
                m_database.Initialize();

                Utils.Trace("Database Initialized!");
            }

            Server.MessageContext.Factory.AddEncodeableTypes(typeof(Opc.Ua.Gds.ObjectIds).Assembly);
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD
            }
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            // generate a numeric node id if the node has a parent and no node id assigned.
            if (node is BaseInstanceState instance && instance.Parent != null)
            {
                return GenerateNodeId();
            }

            return node.NodeId;
        }
        #endregion

        #region Private Methods
        private void HasApplicationAdminAccess(ISystemContext context)
        {
            if (context != null)
            {
                RoleBasedIdentity identity = context.UserIdentity as RoleBasedIdentity;

                if ((identity == null) || (identity.Role != GdsRole.ApplicationAdmin))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Application Administrator access required.");
                }
            }
        }

        private void HasApplicationUserAccess(ISystemContext context)
        {
            if (context != null)
            {
                RoleBasedIdentity identity = context.UserIdentity as RoleBasedIdentity;

                if (identity == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Application User access required.");
                }
            }
        }

        private class CertificateGroup
        {
            public NodeId Id;
            public CertificateGroupConfiguration Configuration;
            public X509Certificate2 Certificate;
            public TrustListState DefaultTrustList;
            public NodeId CertificateType;
            public Boolean UpdateRequired = false;
        }

        private NodeId GetTrustListId(NodeId certificateGroupId)
        {

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = DefaultApplicationGroupId;
            }

            CertificateGroup certificateGroup = null;
            if (m_certificateGroups.TryGetValue(certificateGroupId, out certificateGroup))
            {
                return certificateGroup.DefaultTrustList?.NodeId;
            }

            return null;
        }

        private Boolean? GetCertificateStatus(
            NodeId certificateGroupId,
            NodeId certificateTypeId)
        {

            CertificateGroup certificateGroup = null;
            if (m_certificateGroups.TryGetValue(certificateGroupId, out certificateGroup))
            {
                if (!NodeId.IsNull(certificateTypeId))
                {
                    if (!Utils.IsEqual(certificateGroup.CertificateType, certificateTypeId))
                    {
                        return null;
                    }
                }
                return certificateGroup.UpdateRequired;
            }

            return null;
        }

        private CertificateGroup GetCertificateGroup(NodeId certificateGroupId)
        {
            foreach (var certificateGroup in m_certificateGroups.Values)
            {
                if (certificateGroupId == certificateGroup.Id)
                {
                    return certificateGroup;
                }
            }

            return null;
        }

        private CertificateGroup GetGroupForCertificate(byte[] certificate)
        {
            if (certificate != null && certificate.Length > 0)
            {
                var x509 = new X509Certificate2(certificate);

                foreach (var certificateGroup in m_certificateGroups.Values)
                {
                    if (Utils.CompareDistinguishedName(certificateGroup.Certificate.Subject, x509.Issuer))
                    {
                        return certificateGroup;
                    }
                }
            }

            return null;
        }

        private void RevokeCertificate(byte[] certificate)
        {
            if (certificate != null && certificate.Length > 0)
            {
                CertificateGroup certificateGroup = GetGroupForCertificate(certificate);

                if (certificateGroup != null)
                {
                    try
                    {
                        var x509 = new X509Certificate2(certificate);
                        CertificateFactory.RevokeCertificateAsync(
                            m_configuration.AuthoritiesStorePath,
                            x509,
                            m_certificatePassword).Wait();

                        UpdateAuthorityCertInTrustedList(certificateGroup.Certificate, certificateGroup.Configuration.TrustedListPath).Wait();
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Unexpected error revoking certificate. {0} for Authority={1}", new X509Certificate2(certificate).Subject, certificateGroup.Id);
                    }
                }
            }
        }

        private async Task<CertificateGroup> InitializeCertificateGroup(CertificateGroupConfiguration certificateGroupConfiguration)
        {
            CertificateGroup certificateGroup = null;
            DateTime yesterday = DateTime.UtcNow.AddDays(-1);

            if (String.IsNullOrEmpty(certificateGroupConfiguration.SubjectName))
            {
                throw new ArgumentNullException("SubjectName not specified");
            }

            if (String.IsNullOrEmpty(certificateGroupConfiguration.BaseStorePath))
            {
                throw new ArgumentNullException("BaseStorePath not specified");
            }

            string subjectName = certificateGroupConfiguration.SubjectName.Replace("localhost", Utils.GetHostName());

            Utils.Trace(Utils.TraceMasks.Information, "InitializeCertificateGroup: {0}", certificateGroupConfiguration.SubjectName);

            using (DirectoryCertificateStore store = (DirectoryCertificateStore)CertificateStoreIdentifier.OpenStore(m_configuration.AuthoritiesStorePath))
            {
                X509Certificate2Collection certificates = await store.Enumerate();
                foreach (var certificate in certificates)
                {
                    if (Utils.CompareDistinguishedName(certificate.Subject, subjectName))
                    {
                        certificateGroup = new CertificateGroup()
                        {
                            Id = DefaultApplicationGroupId,
                            Configuration = certificateGroupConfiguration,
                            Certificate = new X509Certificate2(certificate.RawData),
                            CertificateType = Opc.Ua.ObjectTypeIds.ApplicationCertificateType,
                            DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup_TrustList, Server.NamespaceUris), typeof(TrustListState))
                        };

                        if (certificateGroupConfiguration.Id.Contains("Https"))
                        {
                            certificateGroup.Id = DefaultHttpsGroupId;
                            certificateGroup.CertificateType = Opc.Ua.ObjectTypeIds.HttpsCertificateType;
                            certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup_TrustList, Server.NamespaceUris), typeof(TrustListState));
                        }

                        if (certificateGroupConfiguration.Id.Contains("UserToken"))
                        {
                            certificateGroup.Id = DefaultUserTokenGroupId;
                            certificateGroup.CertificateType = null;
                            certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList, Server.NamespaceUris), typeof(TrustListState));
                        }

                        certificateGroup.DefaultTrustList.Handle = new TrustList(
                            certificateGroup.DefaultTrustList,
                            certificateGroupConfiguration.TrustedListPath,
                            certificateGroupConfiguration.IssuerListPath,
                            new TrustList.SecureAccess(HasApplicationUserAccess),
                            new TrustList.SecureAccess(HasApplicationAdminAccess));
                        break;
                    }
                }
            }

            if (certificateGroup == null)
            {

                Utils.Trace(Utils.TraceMasks.Security,
                    "Create new CA Certificate: {0}, KeySize: {1}, HashSize: {2}, LifeTime: {3} months",
                    subjectName,
                    certificateGroupConfiguration.DefaultCertificateKeySize,
                    certificateGroupConfiguration.DefaultCertificateHashSize,
                    certificateGroupConfiguration.DefaultCertificateLifetime
                    );

                X509Certificate2 newCertificate = CertificateFactory.CreateCertificate(
                    CertificateStoreType.Directory,
                    m_configuration.AuthoritiesStorePath,
                    m_certificatePassword,
                    null,
                    null,
                    subjectName,
                    null,
                    certificateGroupConfiguration.DefaultCertificateKeySize,
                    yesterday,
                    certificateGroupConfiguration.DefaultCertificateLifetime,
                    certificateGroupConfiguration.DefaultCertificateHashSize,
                    true,
                    null,
                    null);

                using (DirectoryCertificateStore store = (DirectoryCertificateStore)CertificateStoreIdentifier.OpenStore(m_configuration.AuthoritiesStorePath))
                {
                    certificateGroup = new CertificateGroup()
                    {
                        Configuration = certificateGroupConfiguration,
                        Certificate = new X509Certificate2(newCertificate.RawData),
                    };
                    switch (certificateGroupConfiguration.Id)
                    {
                        case "Default":
                            certificateGroup.Id = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, Server.NamespaceUris);
                            certificateGroup.CertificateType = Opc.Ua.ObjectTypeIds.ApplicationCertificateType;
                            certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup_TrustList, Server.NamespaceUris), typeof(TrustListState));
                            break;
                        case "Https":
                            certificateGroup.Id = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup, Server.NamespaceUris);
                            certificateGroup.CertificateType = Opc.Ua.ObjectTypeIds.HttpsCertificateType;
                            certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList, Server.NamespaceUris), typeof(TrustListState));
                            break;
                        case "UserToken":
                            certificateGroup.Id = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup, Server.NamespaceUris);
                            certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList, Server.NamespaceUris), typeof(TrustListState));
                            break;
                        default:
                            certificateGroup.Id = new NodeId(certificateGroupConfiguration.Id, NamespaceIndex);
                            break;
                    }

                    if (certificateGroup.DefaultTrustList != null)
                    {
                        certificateGroup.DefaultTrustList.Handle = new TrustList(
                            certificateGroup.DefaultTrustList,
                            certificateGroupConfiguration.TrustedListPath,
                            certificateGroupConfiguration.IssuerListPath,
                            new TrustList.SecureAccess(HasApplicationUserAccess),
                            new TrustList.SecureAccess(HasApplicationAdminAccess));
                    }
                }

                // initialize cert revocation list (CRL)
                await CertificateFactory.RevokeCertificateAsync(m_configuration.AuthoritiesStorePath, newCertificate, m_certificatePassword);
            }

            // sync the authority store with the trusted list
            await UpdateAuthorityCertInTrustedList(certificateGroup.Certificate, certificateGroup.Configuration.TrustedListPath);

            return certificateGroup;
        }

        private async Task UpdateAuthorityCertInTrustedList(X509Certificate2 authorityCertificate, string listPath)
        {
            using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(listPath))
            {
                X509Certificate2Collection certs = await store.FindByThumbprint(authorityCertificate.Thumbprint);
                if (certs.Count == 0)
                {
                    await store.Add(authorityCertificate);
                }

                // delete existing CRL in trusted list
                foreach (var crl in store.EnumerateCRLs(authorityCertificate, false))
                {
                    if (crl.VerifySignature(authorityCertificate, false))
                    {
                        store.DeleteCRL(crl);
                    }
                }

                // copy latest CRL to trusted list
                using (ICertificateStore storeAuthority = CertificateStoreIdentifier.OpenStore(m_configuration.AuthoritiesStorePath))
                {
                    foreach (var crl in storeAuthority.EnumerateCRLs(authorityCertificate, true))
                    {
                        store.AddCRL(crl);
                    }
                }
            }
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);

                m_database.NamespaceIndex = NamespaceIndexes[0];

                foreach (var certificateGroupConfiguration in m_configuration.CertificateGroups)
                {
                    try
                    {
                        CertificateGroup certificateGroup = InitializeCertificateGroup(certificateGroupConfiguration).Result;
                        m_certificateGroups[certificateGroup.Id] = certificateGroup;
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Unexpected error initializing certificateGroup: " + certificateGroupConfiguration.Id + "\r\n" + e.StackTrace);
                        // make sure gds server doesn't start without cert groups!
                        throw e;
                    }
                }
            }
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Gds.Server.Model.Opc.Ua.Gds.PredefinedNodes.uanodes", typeof(ApplicationsNodeManager).Assembly, true);
            return predefinedNodes;
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override NodeState AddBehaviourToPredefinedNode(ISystemContext context, NodeState predefinedNode)
        {
            BaseObjectState passiveNode = predefinedNode as BaseObjectState;

            if (passiveNode == null)
            {
                return predefinedNode;
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || typeId.IdType != IdType.Numeric)
            {
                return predefinedNode;
            }

            switch ((uint)typeId.Identifier)
            {
                case Opc.Ua.Gds.ObjectTypes.CertificateDirectoryType:
                    {
                        if (passiveNode is Opc.Ua.Gds.CertificateDirectoryState)
                        {
                            break;
                        }

                        Opc.Ua.Gds.CertificateDirectoryState activeNode = new Opc.Ua.Gds.CertificateDirectoryState(passiveNode.Parent);

                        activeNode.Create(context, passiveNode);
                        activeNode.QueryServers.OnCall = new QueryServersMethodStateMethodCallHandler(OnQueryServers);
                        activeNode.RegisterApplication.OnCall = new RegisterApplicationMethodStateMethodCallHandler(OnRegisterApplication);
                        activeNode.UpdateApplication.OnCall = new UpdateApplicationMethodStateMethodCallHandler(OnUpdateApplication);
                        activeNode.UnregisterApplication.OnCall = new UnregisterApplicationMethodStateMethodCallHandler(OnUnregisterApplication);
                        activeNode.FindApplications.OnCall = new FindApplicationsMethodStateMethodCallHandler(OnFindApplications);
                        activeNode.GetApplication.OnCall = new GetApplicationMethodStateMethodCallHandler(OnGetApplication);
                        activeNode.StartNewKeyPairRequest.OnCall = new StartNewKeyPairRequestMethodStateMethodCallHandler(OnStartNewKeyPairRequest);
                        activeNode.FinishRequest.OnCall = new FinishRequestMethodStateMethodCallHandler(OnFinishRequest);
                        activeNode.GetCertificateGroups.OnCall = new GetCertificateGroupsMethodStateMethodCallHandler(OnGetCertificateGroups);
                        activeNode.GetTrustList.OnCall = new GetTrustListMethodStateMethodCallHandler(OnGetTrustList);
                        activeNode.GetCertificateStatus.OnCall = new GetCertificateStatusMethodStateMethodCallHandler(OnGetCertificateStatus);
                        activeNode.StartSigningRequest.OnCall = new StartSigningRequestMethodStateMethodCallHandler(OnStartSigningRequest);

                        activeNode.CertificateGroups.DefaultApplicationGroup.CertificateTypes.Value = new NodeId[] { Opc.Ua.ObjectTypeIds.ApplicationCertificateType };
                        activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.LastUpdateTime.Value = DateTime.UtcNow;
                        activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.Writable.Value = false;
                        activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.UserWritable.Value = false;

                        activeNode.CertificateGroups.DefaultHttpsGroup.CertificateTypes.Value = new NodeId[] { Opc.Ua.ObjectTypeIds.HttpsCertificateType };
                        activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.LastUpdateTime.Value = DateTime.UtcNow;
                        activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.Writable.Value = false;
                        activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.UserWritable.Value = false;

                        activeNode.CertificateGroups.DefaultUserTokenGroup.CertificateTypes.Value = new NodeId[] { };
                        activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList.LastUpdateTime.Value = DateTime.UtcNow;
                        activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList.Writable.Value = false;
                        activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList.UserWritable.Value = false;

                        // replace the node in the parent.
                        if (passiveNode.Parent != null)
                        {
                            passiveNode.Parent.ReplaceChild(context, activeNode);
                        }

                        return activeNode;
                    }
            }

            return predefinedNode;
        }

        private ServiceResult OnQueryServers(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            string[] serverCapabilities,
            ref DateTime lastCounterResetTime,
            ref ServerOnNetwork[] servers)
        {

            Utils.Trace(Utils.TraceMasks.Information, "QueryServers: {0} {1}", applicationUri, applicationName);

            servers = m_database.QueryServers(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities,
                out lastCounterResetTime);

            return ServiceResult.Good;
        }

        private ServiceResult OnRegisterApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ApplicationRecordDataType application,
            ref NodeId applicationId)
        {
            HasApplicationAdminAccess(context);

            Utils.Trace(Utils.TraceMasks.Information, "OnRegisterApplication: {0}", application.ApplicationUri);

            applicationId = m_database.RegisterApplication(application);

            return ServiceResult.Good;
        }

        private ServiceResult OnUpdateApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ApplicationRecordDataType application)
        {
            HasApplicationAdminAccess(context);

            Utils.Trace(Utils.TraceMasks.Information, "OnUpdateApplication: {0}", application.ApplicationUri);

            var record = m_database.GetApplication(application.ApplicationId);

            if (record == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The application id does not exist.");
            }

            m_database.RegisterApplication(application);

            return ServiceResult.Good;
        }

        private ServiceResult OnUnregisterApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId)
        {
            HasApplicationAdminAccess(context);

            Utils.Trace(Utils.TraceMasks.Information, "OnUnregisterApplication: {0}", applicationId.ToString());

            m_database.UnregisterApplication(applicationId, out byte[] certificate, out byte[] privateKey);

            if (certificate != null)
            {
                RevokeCertificate(certificate);
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnFindApplications(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string applicationUri,
            ref ApplicationRecordDataType[] applications)
        {
            HasApplicationUserAccess(context);
            Utils.Trace(Utils.TraceMasks.Information, "OnFindApplications: {0}", applicationUri);
            applications = m_database.FindApplications(applicationUri);
            return ServiceResult.Good;
        }

        private ServiceResult OnGetApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ref ApplicationRecordDataType application)
        {
            HasApplicationUserAccess(context);
            Utils.Trace(Utils.TraceMasks.Information, "OnGetApplication: {0}", applicationId);
            application = m_database.GetApplication(applicationId);
            return ServiceResult.Good;
        }

        private ServiceResult CheckHttpsDomain(ApplicationRecordDataType application, string commonName)
        {
            if (application.ApplicationType == ApplicationType.Client)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument, "Cannot issue HTTPS certificates to client applications.");
            }

            bool found = false;

            if (application.DiscoveryUrls != null)
            {
                foreach (var discoveryUrl in application.DiscoveryUrls)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        Uri url = new Uri(discoveryUrl);

                        if (url.Scheme == Utils.UriSchemeHttps)
                        {
                            if (Utils.AreDomainsEqual(commonName, url.DnsSafeHost))
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                }
            }

            if (!found)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument, "Cannot issue HTTPS certificates to server applications without a matching HTTPS discovery URL.");
            }

            return ServiceResult.Good;
        }

        private string GetDefaultHttpsDomain(ApplicationRecordDataType application)
        {
            if (application.DiscoveryUrls != null)
            {
                foreach (var discoveryUrl in application.DiscoveryUrls)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        Uri url = new Uri(discoveryUrl);

                        if (url.Scheme == Utils.UriSchemeHttps)
                        {
                            return url.DnsSafeHost;
                        }
                    }
                }
            }

            throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Cannot issue HTTPS certificates to server applications without a HTTPS discovery URL.");
        }

        private string GetDefaultUserToken()
        {
            return "USER";
        }

        private string GetSubjectName(ApplicationRecordDataType application, CertificateGroup certificateGroup, string subjectName)
        {
            bool contextFound = false;

            var fields = Utils.ParseDistinguishedName(subjectName);

            StringBuilder builder = new StringBuilder();

            foreach (var field in fields)
            {
                if (builder.Length > 0)
                {
                    builder.Append(",");
                }

                if (field.StartsWith("CN=", StringComparison.Ordinal))
                {
                    if (certificateGroup.Id == DefaultHttpsGroupId)
                    {
                        var error = CheckHttpsDomain(application, field.Substring(3));

                        if (StatusCode.IsBad(error.StatusCode))
                        {
                            builder.Append("CN=");
                            builder.Append(GetDefaultHttpsDomain(application));
                            continue;
                        }
                    }
                }

                contextFound |= (field.StartsWith("DC=", StringComparison.Ordinal) || field.StartsWith("O=", StringComparison.Ordinal));

                builder.Append(field);
            }

            if (!contextFound)
            {
                if (!String.IsNullOrEmpty(m_configuration.DefaultSubjectNameContext))
                {
                    builder.Append(m_configuration.DefaultSubjectNameContext);
                }
            }

            return builder.ToString();
        }

        private string[] GetDefaulDomainNames(ApplicationRecordDataType application)
        {
            List<string> names = new List<string>();

            if (application.DiscoveryUrls != null && application.DiscoveryUrls.Count > 0)
            {
                foreach (var discoveryUrl in application.DiscoveryUrls)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        Uri url = new Uri(discoveryUrl);

                        foreach (var name in names)
                        {
                            if (Utils.AreDomainsEqual(name, url.DnsSafeHost))
                            {
                                url = null;
                                break;
                            }
                        }

                        if (url != null)
                        {
                            names.Add(url.DnsSafeHost);
                        }
                    }
                }
            }

            return names.ToArray();
        }

        private ServiceResult OnStartNewKeyPairRequest(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword,
            ref NodeId requestId)
        {
            HasApplicationAdminAccess(context);

            var application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, Server.NamespaceUris);
            }

            CertificateGroup certificateGroup = null;
            if (!m_certificateGroups.TryGetValue(certificateGroupId, out certificateGroup))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument, "The certificateGroup is not supported.");
            }

            if (!NodeId.IsNull(certificateTypeId))
            {
                if (!Server.TypeTree.IsTypeOf(certificateGroup.CertificateType, certificateTypeId))
                {
                    return new ServiceResult(StatusCodes.BadInvalidArgument, "The CertificateType is not supported by the certificateGroup.");
                }
            }

            if (!String.IsNullOrEmpty(subjectName))
            {
                subjectName = GetSubjectName(application, certificateGroup, subjectName);
            }
            else
            {
                StringBuilder buffer = new StringBuilder();

                buffer.Append("CN=");

                if ((NodeId.IsNull(certificateGroup.Id) || (certificateGroup.Id == DefaultApplicationGroupId)) && (application.ApplicationNames.Count > 0))
                {
                    buffer.Append(application.ApplicationNames[0]);
                }
                else if (certificateGroup.Id == DefaultHttpsGroupId)
                {
                    buffer.Append(GetDefaultHttpsDomain(application));
                }
                else if (certificateGroup.Id == DefaultUserTokenGroupId)
                {
                    buffer.Append(GetDefaultUserToken());
                }

                if (!String.IsNullOrEmpty(m_configuration.DefaultSubjectNameContext))
                {
                    buffer.Append(m_configuration.DefaultSubjectNameContext);
                }

                subjectName = buffer.ToString();
            }

            if (domainNames != null && domainNames.Length > 0)
            {
                foreach (var domainName in domainNames)
                {
                    if (Uri.CheckHostName(domainName) == UriHostNameType.Unknown)
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument, "The domainName ({0}) is not a valid DNS Name or IPAddress.", domainName);
                    }
                }
            }
            else
            {
                domainNames = GetDefaulDomainNames(application);
            }

            X509Certificate2 newCertificate = null;

            try
            {
                newCertificate = CertificateFactory.CreateCertificate(
                     null,
                     null,
                     privateKeyPassword,
                     application.ApplicationUri ?? "urn:ApplicationURI",
                     application.ApplicationNames.Count > 0 ? application.ApplicationNames[0].Text : "ApplicationName",
                     subjectName,
                     domainNames,
                     certificateGroup.Configuration.DefaultCertificateKeySize,
                     DateTime.UtcNow.AddDays(-1),
                     certificateGroup.Configuration.DefaultCertificateLifetime,
                     certificateGroup.Configuration.DefaultCertificateHashSize,
                     false,
                     LoadSigningKeyAsync(certificateGroup.Certificate, string.Empty).Result,
                     null);
            }
            catch (Exception e)
            {
                StringBuilder error = new StringBuilder();

                error.Append("Error Generating Certificate=" + e.Message);
                error.Append("\r\nApplicationId=" + applicationId.ToString());
                error.Append("\r\nApplicationUri=" + application.ApplicationUri);

                return new ServiceResult(StatusCodes.BadConfigurationError, error.ToString());
            }

            byte[] privateKey;
            if (privateKeyFormat == "PFX")
            {
                privateKey = newCertificate.Export(X509ContentType.Pfx, privateKeyPassword);
            }
            else if (privateKeyFormat == "PEM")
            {
                privateKey = CertificateFactory.ExportPrivateKeyAsPEM(newCertificate);
            }
            else
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument, "Invalid private key format");
            }

            // store only app certificate
            using (DirectoryCertificateStore store = (DirectoryCertificateStore)CertificateStoreIdentifier.OpenStore(m_configuration.ApplicationCertificatesStorePath))
            {
                store.Add(new X509Certificate2(newCertificate.RawData)).Wait();
            }

            requestId = m_database.CreateCertificateRequest(
                applicationId,
                newCertificate.RawData,
                privateKey,
                certificateGroup.Id.Identifier as string);

            // immediately approve certificate for now.
            m_database.ApproveCertificateRequest(requestId, false);

            return ServiceResult.Good;
        }

        private ServiceResult OnStartSigningRequest(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificateRequest,
            ref NodeId requestId)
        {
            HasApplicationAdminAccess(context);

            var application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, Server.NamespaceUris);
            }

            CertificateGroup certificateGroup = null;
            if (!m_certificateGroups.TryGetValue(certificateGroupId, out certificateGroup))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument, "The CertificateGroupId does not refer to a supported certificateGroup.");
            }

            if (!NodeId.IsNull(certificateTypeId))
            {
                if (!Server.TypeTree.IsTypeOf(certificateGroup.CertificateType, certificateTypeId))
                {
                    return new ServiceResult(StatusCodes.BadInvalidArgument, "The CertificateTypeId is not supported by the certificateGroup.");
                }
            }

            X509Certificate2 certificate = null;
            string[] domainNames = GetDefaulDomainNames(application);

            try
            {
                Pkcs10CertificationRequest pkcs10CertificationRequest = new Pkcs10CertificationRequest(certificateRequest);
                CertificationRequestInfo info = pkcs10CertificationRequest.GetCertificationRequestInfo();

                certificate = CertificateFactory.CreateCertificate(
                    CertificateStoreType.Directory,
                    m_configuration.ApplicationCertificatesStorePath,
                    m_certificatePassword,
                    application.ApplicationUri ?? "urn:ApplicationURI",
                    application.ApplicationNames.Count > 0 ? application.ApplicationNames[0].Text : "ApplicationName",
                    info.Subject.ToString(),
                    domainNames,
                    certificateGroup.Configuration.DefaultCertificateKeySize,
                    DateTime.UtcNow.AddDays(-1),
                    certificateGroup.Configuration.DefaultCertificateLifetime,
                    certificateGroup.Configuration.DefaultCertificateHashSize,
                    false,
                    LoadSigningKeyAsync(certificateGroup.Certificate, string.Empty).Result,
                    info.SubjectPublicKeyInfo.GetEncoded());
            }
            catch (Exception e)
            {
                StringBuilder error = new StringBuilder();

                error.Append("Error Generating Certificate=" + e.Message);
                error.Append("\r\nApplicationId=" + applicationId.ToString());
                error.Append("\r\nApplicationUri=" + application.ApplicationUri);
                error.Append("\r\nApplicationName=" + application.ApplicationNames[0].Text);

                return new ServiceResult(StatusCodes.BadConfigurationError, error.ToString());
            }

            requestId = m_database.CreateCertificateRequest(
                applicationId,
                certificate.RawData,
                null,
                certificateGroup.Id.Identifier as string);

            // immediately approve certificate for now.
            m_database.ApproveCertificateRequest(requestId, false);

            return ServiceResult.Good;
        }

        private ServiceResult OnFinishRequest(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId requestId,
            ref byte[] certificate,
            ref byte[] privateKey,
            ref byte[][] issuerCertificates)
        {
            issuerCertificates = null;
            HasApplicationAdminAccess(context);

            var done = m_database.CompleteCertificateRequest(applicationId, requestId, out certificate, out privateKey);

            if (!done)
            {
                return new ServiceResult(StatusCodes.BadNothingToDo, "The request has not been approved by the administrator.");
            }

            CertificateGroup certificateGroup = GetGroupForCertificate(certificate);

            issuerCertificates = new byte[1][];
            issuerCertificates[0] = certificateGroup.Certificate.RawData;

            return ServiceResult.Good;
        }

        public ServiceResult OnGetCertificateGroups(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ref NodeId[] certificateGroupIds)
        {
            HasApplicationUserAccess(context);

            var application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId does not refer to a valid application.");
            }

            var certificateGroupIdList = new List<NodeId>();
            foreach (var certificateGroup in m_certificateGroups)
            {
                NodeId key = certificateGroup.Key;
                certificateGroupIdList.Add(key);
            }
            certificateGroupIds = certificateGroupIdList.ToArray();

            return ServiceResult.Good;
        }

        public ServiceResult OnGetTrustList(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            ref NodeId trustListId)
        {
            HasApplicationUserAccess(context);

            var application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = DefaultApplicationGroupId;
            }

            trustListId = GetTrustListId(certificateGroupId);

            if (trustListId == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The CertificateGroupId does not refer to a group that is valid for the application.");
            }

            return ServiceResult.Good;
        }

        public ServiceResult OnGetCertificateStatus(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ref Boolean updateRequired)
        {
            HasApplicationUserAccess(context);

            var application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = DefaultApplicationGroupId;
            }

            Boolean? updateRequiredResult = GetCertificateStatus(certificateGroupId, certificateTypeId);
            if (updateRequiredResult == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The CertificateGroupId and CertificateTypeId do not refer to a group and type that is valid for the application.");
            }

            updateRequired = (Boolean)updateRequiredResult;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace. 
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                NodeState node = null;

                // check cache (the cache is used because the same node id can appear many times in a single request).
                if (cache != null)
                {
                    if (cache.TryGetValue(nodeId, out node))
                    {
                        return new NodeHandle(nodeId, node);
                    }
                }

                // look up predefined node.
                if (PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    NodeHandle handle = new NodeHandle(nodeId, node);

                    if (cache != null)
                    {
                        cache.Add(nodeId, node);
                    }

                    return handle;
                }

                // node not found.
                return null;
            }
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }

            // lookup in operation cache.
            NodeState target = FindNodeInCache(context, handle, cache);

            if (target != null)
            {
                handle.Node = target;
                handle.Validated = true;
                return handle.Node;
            }

            // put root into operation cache.
            if (cache != null)
            {
                cache[handle.NodeId] = target;
            }

            handle.Node = target;
            handle.Validated = true;
            return handle.Node;
        }
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Methods
        /// <summary>
        /// Generates a new node id.
        /// </summary>
        private NodeId GenerateNodeId()
        {
            return new NodeId(++m_nextNodeId, NamespaceIndex);
        }

        /// <summary>
        /// load the authority signing key.
        /// </summary>
        private async Task<X509Certificate2> LoadSigningKeyAsync(X509Certificate2 signingCertificate, string signingKeyPassword)
        {
            CertificateIdentifier certIdentifier = new CertificateIdentifier(signingCertificate)
            {
                StorePath = m_configuration.AuthoritiesStorePath,
                StoreType = CertificateStoreIdentifier.DetermineStoreType(m_configuration.AuthoritiesStorePath)
            };
            return await certIdentifier.LoadPrivateKey(signingKeyPassword);
        }
        #endregion

        #region Private Fields
        private string m_certificatePassword = "";
        private uint m_nextNodeId;
        private GlobalDiscoveryServerConfiguration m_configuration;
        private IApplicationsDatabase m_database;
        private Dictionary<NodeId, CertificateGroup> m_certificateGroups;
        #endregion
    }
}
