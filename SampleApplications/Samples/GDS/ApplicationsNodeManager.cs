/* ========================================================================
 * Copyright (c) 2005-2011 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Server;
using System.Threading.Tasks;

namespace Opc.Ua.GdsServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class ApplicationsNodeManager : CustomNodeManager2
    {
        private NodeId DefaultApplicationGroupId;
        private NodeId DefaultHttpsGroupId;

        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ApplicationsNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration)
        {
            List<string> namespaceUris = new List<string>();
            namespaceUris.Add("http://opcfoundation.org/UA/GDS/applications/");
            namespaceUris.Add(Opc.Ua.Gds.Namespaces.OpcUaGds);
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
                if (m_configuration.DefaultSubjectNameContext[0] != '/')
                {
                    m_configuration.DefaultSubjectNameContext = "/" + m_configuration.DefaultSubjectNameContext;
                }
            }

            DefaultApplicationGroupId = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, Server.NamespaceUris);
            DefaultHttpsGroupId = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup, Server.NamespaceUris); 

            m_database = new ApplicationsDatabase();
            m_certificateGroups = new Dictionary<NodeId, CertificateGroup>();

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
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                return GenerateNodeId();
            }

            return node.NodeId;
        }
        #endregion

        private void HasGdsAdminAccess(ISystemContext context)
        {
            if (context != null)
            {
                RoleBasedIdentity identity = context.UserIdentity as RoleBasedIdentity;

                if (identity == null || identity.Role != GdsRole.GdsAdmin)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "GDS Administrator access required.");
                }
            }
        }

        private void HasApplicationAdminAccess(ISystemContext context)
        {
            if (context != null)
            {
                RoleBasedIdentity identity = context.UserIdentity as RoleBasedIdentity;

                if (identity == null || (identity.Role != GdsRole.GdsAdmin && identity.Role != GdsRole.ApplicationAdmin))
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
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Application Administrator access required.");
                }
            }
        }

        private class CertificateGroup
        {
            public NodeId Id;
            public CertificateGroupConfiguration Configuration;
            public string PublicKeyFilePath;
            public string PrivateKeyFilePath;
            public X509Certificate2 Certificate;
            public TrustListState DefaultTrustList;
            public NodeId CertificateType;
        }

        private NodeId GetTrustListId(NodeId certificateGroupId)
        {
            CertificateGroup certificateGroup = null;

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = DefaultApplicationGroupId;
            }

            if (m_certificateGroups.TryGetValue(certificateGroupId, out certificateGroup))
            {
                return (certificateGroup.DefaultTrustList != null) ? certificateGroup.DefaultTrustList.NodeId : null;
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
                    var x509 = new X509Certificate2(certificate);

                    try
                    {
                        CertificateAuthority.RevokeCertificate(
                            certificateGroup.DefaultTrustList + "\\trusted",
                            x509,
                            certificateGroup.PrivateKeyFilePath,
                            null);
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Unexpected error revoking certificate. {0} for Authority={1}", x509.Subject, certificateGroup.Id);
                    }
                }
            }
        }

        private async Task<CertificateGroup> InitializeCertificateGroup(CertificateGroupConfiguration certificateGroupConfiguration)
        {
            CertificateGroup certificateGroup = null;

            if (String.IsNullOrEmpty(certificateGroupConfiguration.SubjectName))
            {
                certificateGroupConfiguration.SubjectName = "DC=localhost/CN=System CA";
            }

            if (String.IsNullOrEmpty(certificateGroupConfiguration.BaseStorePath))
            {
                certificateGroupConfiguration.BaseStorePath = Assembly.GetExecutingAssembly().Location;
            }

            string sn = certificateGroupConfiguration.SubjectName.Replace("localhost", System.Net.Dns.GetHostName());

            using (DirectoryCertificateStore store = (DirectoryCertificateStore)CertificateStoreIdentifier.OpenStore(m_configuration.AuthoritiesStorePath))
            {
                X509Certificate2Collection certificates = await store.Enumerate();
                foreach (var certificate in certificates)
                {
                    if (Utils.CompareDistinguishedName(certificate.Subject, sn))
                    {
                        certificateGroup = new CertificateGroup()
                        {
                            Id = DefaultApplicationGroupId,
                            Configuration = certificateGroupConfiguration,
                            PublicKeyFilePath = store.GetPublicKeyFilePath(certificate.Thumbprint),
                            PrivateKeyFilePath = store.GetPrivateKeyFilePath(certificate.Thumbprint),
                            CertificateType = Opc.Ua.ObjectTypeIds.ApplicationCertificateType,
                            DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup_TrustList, Server.NamespaceUris), typeof(TrustListState))
                        };


                        if (certificateGroupConfiguration.Id.Contains("Https"))
                        {
                            certificateGroup.Id = DefaultHttpsGroupId;
                            certificateGroup.CertificateType = Opc.Ua.ObjectTypeIds.HttpsCertificateType;
                            certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup_TrustList, Server.NamespaceUris), typeof(TrustListState));
                        }

                        certificateGroup.DefaultTrustList.Handle = new TrustList(certificateGroup.DefaultTrustList, certificateGroupConfiguration.BaseStorePath);
                        break;
                    }
                }
            }

            if (certificateGroup == null)
            {
                DateTime now = DateTime.Now;
                now = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-1);

                var newCertificate = await CertificateAuthority.CreateCertificate(
                    CertificateStoreType.Directory,
                    m_configuration.AuthoritiesStorePath,
                    null,
                    null,
                    null,
                    sn,
                    null,
                    2048,
                    now,
                    60,
                    256,
                    true,
                    false,
                    null,
                    null);

                using (DirectoryCertificateStore store = (DirectoryCertificateStore)CertificateStoreIdentifier.OpenStore(m_configuration.AuthoritiesStorePath))
                {
                    certificateGroup = new CertificateGroup()
                    {
                        Configuration = certificateGroupConfiguration,
                        PublicKeyFilePath = store.GetPublicKeyFilePath(newCertificate.Thumbprint),
                        PrivateKeyFilePath = store.GetPrivateKeyFilePath(newCertificate.Thumbprint)
                    };

                    if (certificateGroupConfiguration.Id == "Default")
                    {
                        certificateGroup.Id = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, Server.NamespaceUris);
                        certificateGroup.CertificateType = Opc.Ua.ObjectTypeIds.ApplicationCertificateType;
                        certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup_TrustList, Server.NamespaceUris), typeof(TrustListState));
                    }
                    else if (certificateGroupConfiguration.Id == "Https")
                    {
                        certificateGroup.Id = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup, Server.NamespaceUris);
                        certificateGroup.CertificateType = Opc.Ua.ObjectTypeIds.HttpsCertificateType;
                        certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList, Server.NamespaceUris), typeof(TrustListState));
                    }
                    else
                    {
                        certificateGroup.Id = new NodeId(certificateGroupConfiguration.Id, NamespaceIndex);
                    }

                    if (certificateGroup.DefaultTrustList != null)
                    {
                        certificateGroup.DefaultTrustList.Handle = new TrustList(certificateGroup.DefaultTrustList, certificateGroupConfiguration.BaseStorePath);
                    }
                }

                X509Certificate2 revokedCertificate = null;

                try
                {
                    revokedCertificate = await CertificateAuthority.CreateCertificate(
                     CertificateStoreType.Directory,
                     m_configuration.ApplicationCertificatesStorePath,
                     null,
                     null,
                     null,
                     "CN=Need a Certificate in the CRL",
                     null,
                     1024,
                     now,
                     60,
                     256,
                     false,
                     false,
                     certificateGroup.PrivateKeyFilePath,
                     null);

                    CertificateAuthority.RevokeCertificate(m_configuration.AuthoritiesStorePath, revokedCertificate, certificateGroup.PrivateKeyFilePath, null);
                }
                finally
                {
                    if (revokedCertificate != null)
                    {
                        using (DirectoryCertificateStore store = (DirectoryCertificateStore)CertificateStoreIdentifier.OpenStore(m_configuration.ApplicationCertificatesStorePath))
                        {
                            await store.Delete(revokedCertificate.Thumbprint);
                        }
                    }
                }
            }

            certificateGroup.Certificate = new X509Certificate2(certificateGroup.PublicKeyFilePath);

            if (certificateGroup.Configuration.BaseStorePath != null)
            {
                string trustListPath = certificateGroup.Configuration.BaseStorePath + "\\trusted";
                trustListPath = Utils.GetAbsoluteDirectoryPath(trustListPath, true, false, true);

                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(trustListPath))
                {
                    var x509 = new X509Certificate2(certificateGroup.Certificate.RawData);

                    if (store.FindByThumbprint(x509.Thumbprint) == null)
                    {
                        await store.Add(x509);
                    }

                    using (ICertificateStore store2 = CertificateStoreIdentifier.OpenStore(m_configuration.AuthoritiesStorePath))
                    {
                        foreach (var crl in store2.EnumerateCRLs())
                        {
                            if (Utils.CompareDistinguishedName(crl.Issuer, certificateGroup.Certificate.Subject))
                            {
                                store.AddCRL(crl);
                            }
                        }
                    }
                }

                string issuerListPath = certificateGroup.Configuration.BaseStorePath + "\\issuers";
                issuerListPath = Utils.GetAbsoluteDirectoryPath(issuerListPath, true, false, true);
            }

            return certificateGroup;
        }

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
                        Task<CertificateGroup> task = InitializeCertificateGroup(certificateGroupConfiguration);
                        task.Wait();
                        CertificateGroup certificateGroup = task.Result;
                        m_certificateGroups[certificateGroup.Id] = certificateGroup;
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Unexpected error initializing certificateGroup: " + certificateGroupConfiguration.Id + "\r\n" + e.StackTrace);
                    }
                }
            }
        }

        #region TrustList Class
        private class TrustList
        {
            #region Constructors
            public TrustList(Opc.Ua.TrustListState node, string path)
            {
                m_node = node;
                m_path = path;

                node.Open.OnCall = new OpenMethodStateMethodCallHandler(Open);
                node.OpenWithMasks.OnCall = new OpenWithMasksMethodStateMethodCallHandler(OpenWithMasks);
                node.Read.OnCall = new ReadMethodStateMethodCallHandler(Read);
                node.Close.OnCall = new CloseMethodStateMethodCallHandler(Close);
                node.CloseAndUpdate.OnCall = new CloseAndUpdateMethodStateMethodCallHandler(CloseAndUpdate);
                node.AddCertificate.OnCall = new AddCertificateMethodStateMethodCallHandler(AddCertificate);
                node.RemoveCertificate.OnCall = new RemoveCertificateMethodStateMethodCallHandler(RemoveCertificate);
            }
            #endregion

            #region Private Fields
            private object m_lock = new object();
            private NodeId m_sessionId;
            private uint m_fileHandle;
            private string m_path;
            private TrustListState m_node;
            private Stream m_strm;
            #endregion

            #region Private Methods
            private ServiceResult Open(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                byte mode,
                ref uint fileHandle)
            {
                return Open(context, method, objectId, mode, 0xF, fileHandle);
            }

            private ServiceResult OpenWithMasks(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                uint masks,
                ref uint fileHandle)
            {
                return Open(context, method, objectId, 1, masks, fileHandle);
            }

            private ServiceResult Open(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                byte mode,
                uint masks,
                uint fileHandle)
            {
                if (mode != 1)
                {
                    return StatusCodes.BadNotWritable;
                }

                lock (m_lock)
                {
                    if (m_sessionId != null)
                    {
                        return StatusCodes.BadUserAccessDenied;
                    }

                    m_sessionId = context.SessionId;
                    fileHandle = ++m_fileHandle;

                    TrustListDataType trustList = new TrustListDataType();
                    trustList.SpecifiedLists = masks;

                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_path + "\\trusted"))
                    {
                        if ((masks & (uint)TrustListMasks.TrustedCertificates) != 0)
                        {
                            Task<X509Certificate2Collection> task = store.Enumerate();
                            task.Wait();
                            X509Certificate2Collection certificates = task.Result;
                            foreach (var certificate in certificates)
                            {
                                trustList.TrustedCertificates.Add(certificate.RawData);
                            }
                        }

                        if ((masks & (uint)TrustListMasks.TrustedCrls) != 0)
                        {
                            foreach (var crl in store.EnumerateCRLs())
                            {
                                trustList.TrustedCrls.Add(crl.RawData);
                            }
                        }
                    }

                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_path + "\\issuers"))
                    {
                        if ((masks & (uint)TrustListMasks.IssuerCertificates) != 0)
                        {
                            Task<X509Certificate2Collection> task = store.Enumerate();
                            task.Wait();
                            X509Certificate2Collection certificates = task.Result;
                            foreach (var certificate in certificates)
                            {
                                trustList.IssuerCertificates.Add(certificate.RawData);
                            }
                        }

                        if ((masks & (uint)TrustListMasks.IssuerCrls) != 0)
                        {
                            foreach (var crl in store.EnumerateCRLs())
                            {
                                trustList.IssuerCrls.Add(crl.RawData);
                            }
                        }
                    }

                    ServiceMessageContext messageContext = new ServiceMessageContext();

                    messageContext.NamespaceUris = context.NamespaceUris;
                    messageContext.ServerUris = context.ServerUris;
                    messageContext.Factory = context.EncodeableFactory;

                    MemoryStream strm = new MemoryStream();
                    BinaryEncoder encoder = new BinaryEncoder(strm, messageContext);
                    encoder.WriteEncodeable(null, trustList, null);
                    strm.Position = 0;
                    m_strm = strm;

                    m_node.OpenCount.Value = 1;
                }

                return ServiceResult.Good;
            }

            private ServiceResult Read(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                uint fileHandle,
                int length,
                ref byte[] data)
            {
                lock (m_lock)
                {
                    if (m_sessionId != context.SessionId)
                    {
                        return StatusCodes.BadUserAccessDenied;
                    }

                    if (m_fileHandle != fileHandle)
                    {
                        return StatusCodes.BadInvalidArgument;
                    }

                    data = new byte[length];

                    int bytesRead = m_strm.Read(data, 0, length);

                    if (bytesRead < 0)
                    {
                        return StatusCodes.BadUnexpectedError;
                    }

                    if (bytesRead < length)
                    {
                        byte[] bytes = new byte[bytesRead];
                        Array.Copy(data, bytes, bytesRead);
                        data = bytes;
                    }
                }

                return ServiceResult.Good;
            }

            private ServiceResult Close(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                uint fileHandle)
            {
                lock (m_lock)
                {
                    if (m_sessionId != context.SessionId)
                    {
                        return StatusCodes.BadUserAccessDenied;
                    }

                    if (m_fileHandle != fileHandle)
                    {
                        return StatusCodes.BadInvalidArgument;
                    }

                    m_sessionId = null;
                    m_strm = null;
                    m_node.OpenCount.Value = 0;
                }

                return ServiceResult.Good;
            }

            private ServiceResult CloseAndUpdate(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                uint fileHandle,
                ref bool restartRequired)
            {
                lock (m_lock)
                {
                    if (m_sessionId != context.SessionId)
                    {
                        return StatusCodes.BadUserAccessDenied;
                    }

                    if (m_fileHandle != fileHandle)
                    {
                        return StatusCodes.BadInvalidArgument;
                    }

                    m_sessionId = null;
                    m_strm = null;
                    m_node.OpenCount.Value = 0;
                }

                return ServiceResult.Good;
            }

            private ServiceResult AddCertificate(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                byte[] certificate,
                bool isTrustedCertificate)
            {
                if (isTrustedCertificate)
                {
                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_path + "\\trusted"))
                    {
                        store.Add(new X509Certificate2(certificate));
                    }
                }
                else
                {
                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_path + "\\issuers"))
                    {
                        store.Add(new X509Certificate2(certificate));
                    }
                }

                return ServiceResult.Good;
            }

            private ServiceResult RemoveCertificate(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string certificate,
                bool isTrustedCertificate)
            {
                if (isTrustedCertificate)
                {
                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_path + "\\trusted"))
                    {
                        store.Delete(new X509Certificate2(certificate).Thumbprint);
                    }
                }
                else
                {
                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_path + "\\issuers"))
                    {
                        store.Delete(new X509Certificate2(certificate).Thumbprint);
                    }
                }
                
                return ServiceResult.Good;
            }
            #endregion
        }
        #endregion
        
        /// <summary>
        /// Loads the schema from an embedded resource.
        /// </summary>
        public byte[] LoadSchemaFromResource(string resourcePath, Assembly assembly)
        {
            if (resourcePath == null) throw new ArgumentNullException("resourcePath");

            if (assembly == null)
            {
                assembly = Assembly.GetCallingAssembly();
            }

            Stream istrm = assembly.GetManifestResourceStream(resourcePath);

            if (istrm == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not load nodes from resource: {0}", resourcePath);
            }

            byte[] buffer = new byte[istrm.Length];
            istrm.Read(buffer, 0, (int)istrm.Length);
            return buffer;
        }

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Gds.Model.Opc.Ua.Gds.PredefinedNodes.uanodes", typeof(Opc.Ua.Gds.ObjectIds).Assembly, true);
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
                    activeNode.StartNewKeyPairRequest.OnCall = new StartNewKeyPairRequestMethodStateMethodCallHandler(OnStartNewKeyPairRequest);
                    activeNode.FinishRequest.OnCall = new FinishRequestMethodStateMethodCallHandler(OnFinishRequest);
                    activeNode.GetCertificateGroups.OnCall = new GetCertificateGroupsMethodStateMethodCallHandler(OnGetCertificateGroups);
                    activeNode.GetTrustList.OnCall = new GetTrustListMethodStateMethodCallHandler(OnGetTrustList);
                    activeNode.StartSigningRequest.OnCall = new StartSigningRequestMethodStateMethodCallHandler(OnStartSigningRequest);

                    activeNode.CertificateGroups.DefaultApplicationGroup.CertificateTypes.Value = new NodeId[] { Opc.Ua.ObjectTypeIds.ApplicationCertificateType };
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.LastUpdateTime.Value = DateTime.UtcNow;
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.Writable.Value = false;
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.UserWritable.Value = false;

                    activeNode.CertificateGroups.DefaultHttpsGroup.CertificateTypes.Value = new NodeId[] { Opc.Ua.ObjectTypeIds.HttpsCertificateType };
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.LastUpdateTime.Value = DateTime.UtcNow;
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.Writable.Value = false;
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.UserWritable.Value = false;

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

            byte[] certificate = null;
            byte[] httpsCertificate = null;

            m_database.UnregisterApplication(applicationId, out certificate, out httpsCertificate);

            RevokeCertificate(certificate);
            RevokeCertificate(httpsCertificate);

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
            applications = m_database.FindApplications(applicationUri);
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

        private string GetSubjectName(ApplicationRecordDataType application, CertificateGroup certificateGroup, string subjectName)
        {
            bool contextFound = false;

            var fields = Utils.ParseDistinguishedName(subjectName);

            StringBuilder builder = new StringBuilder();

            foreach (var field in fields)
            {
                if (builder.Length > 0)
                {
                    builder.Append("/");
                }

                if (field.StartsWith("CN="))
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

                if (field.StartsWith("DC=") || field.StartsWith("O="))
                {
                    contextFound = true;
                }

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
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId is does not refer to a valid application.");
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

                if (NodeId.IsNull(certificateGroup.Id) || certificateGroup.Id == DefaultApplicationGroupId)
                {
                    buffer.Append(application.ApplicationNames[0]);
                }

                else if (certificateGroup.Id == DefaultHttpsGroupId)
                {
                    buffer.Append(GetDefaultHttpsDomain(application));
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
                DateTime now = DateTime.UtcNow;
                now = new DateTime(now.Year, now.Month, now.Day).AddDays(-1);

                Task<X509Certificate2> task = CertificateAuthority.CreateCertificate(
                    CertificateStoreType.Directory,
                    m_configuration.ApplicationCertificatesStorePath,
                    privateKeyPassword,
                    application.ApplicationUri,
                    application.ApplicationNames[0].Text,
                    subjectName,
                    domainNames,
                    (certificateGroup.Configuration.DefaultCertificateKeySize != 0) ? certificateGroup.Configuration.DefaultCertificateKeySize : (ushort)1024,
                    now,
                    (certificateGroup.Configuration.DefaultCertificateLifetime != 0) ? certificateGroup.Configuration.DefaultCertificateLifetime : (ushort)60,
                    256,
                    false,
                    (privateKeyFormat == "PEM"),
                    certificateGroup.PrivateKeyFilePath,
                    null);
                task.Wait();
                newCertificate = task.Result;
            }
            catch (Exception e)
            {
                StringBuilder error = new StringBuilder();

                error.Append("Error Generating Certificate=" + e.Message);
                error.Append("\r\nApplicationId=" + applicationId.ToString());
                error.Append("\r\nApplicationUri=" + application.ApplicationUri);

                return new ServiceResult(StatusCodes.BadConfigurationError, error.ToString());
            }

            using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_configuration.ApplicationCertificatesStorePath))
            {
                byte[] privateKey = null;
                var privateKeyPath = store.GetPrivateKeyFilePath(newCertificate.Thumbprint);

                if (privateKeyPath != null)
                {
                    privateKey = File.ReadAllBytes(privateKeyPath);
                }

                Task<bool> task = store.Delete(newCertificate.Thumbprint);
                task.Wait();

                requestId = m_database.CreateCertificateRequest(
                    applicationId,
                    newCertificate.RawData,
                    privateKey,
                    certificateGroup.Id.Identifier as string);
            }

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
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId is does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, Server.NamespaceUris);
            }

            CertificateGroup certificateGroup = null;

            if (!m_certificateGroups.TryGetValue(certificateGroupId, out certificateGroup))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument, "The CertificateGroupId is does not refer to a supported certificateGroup.");
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
                DateTime now = DateTime.UtcNow;
                now = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-1);

                var newCertificate = CertificateAuthority.Sign(
                    Utils.ToHexString(certificateRequest),
                    application.ApplicationNames[0].Text,
                    application.ApplicationUri,
                    domainNames,
                    certificateGroup.PrivateKeyFilePath,
                    null,
                    now,
                    (certificateGroup.Configuration.DefaultCertificateLifetime != 0) ? certificateGroup.Configuration.DefaultCertificateLifetime : (ushort)60,
                    256,
                    m_configuration.ApplicationCertificatesStorePath);

                var bytes = Utils.FromHexString(newCertificate);
                certificate = Utils.ParseCertificateBlob(bytes);
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
                certificate.GetRawCertData(),
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
            issuerCertificates[0] = File.ReadAllBytes(certificateGroup.PublicKeyFilePath);

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
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId is does not refer to a valid application.");
            }

            if (application.ApplicationType == ApplicationType.Client)
            {
                certificateGroupIds = new NodeId[]
                { 
                    DefaultApplicationGroupId
                };
            }
            else
            {
                certificateGroupIds = new NodeId[]
                { 
                    DefaultApplicationGroupId,
                    DefaultHttpsGroupId
                };
            }

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
                return new ServiceResult(StatusCodes.BadNotFound, "The ApplicationId is does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = DefaultApplicationGroupId;
            }

            trustListId = GetTrustListId(certificateGroupId);

            if (trustListId == null)
            {
                return new ServiceResult(StatusCodes.BadNotFound, "The CertificateGroupId is does not refer to a group that is valid for the application.");
            }

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
        #endregion

        #region Private Fields
        private uint m_nextNodeId;
        private GlobalDiscoveryServerConfiguration m_configuration;
        private ApplicationsDatabase m_database;
        private Dictionary<NodeId, CertificateGroup> m_certificateGroups;
        #endregion
    }
}
