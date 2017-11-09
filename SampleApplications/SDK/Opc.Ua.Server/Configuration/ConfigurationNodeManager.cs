/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The Server Configuration Node Manager.
    /// </summary>
    public class ConfigurationNodeManager : DiagnosticsNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the configuration and diagnostics manager.
        /// </summary>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration
            )
            :
            base(server, configuration)
        {
            m_rejectedStorePath = configuration.SecurityConfiguration.RejectedCertificateStore.StorePath;
            m_certificateGroups = new List<ServerCertificateGroup>();
            // TODO: configure cert groups in configuration
            ServerCertificateGroup defaultApplicationGroup = new ServerCertificateGroup
            {
                BrowseName = Opc.Ua.BrowseNames.DefaultApplicationGroup,
                CertificateTypes = new NodeId[] { ObjectTypeIds.ApplicationCertificateType },
                ApplicationStorePath = configuration.SecurityConfiguration.ApplicationCertificate.StorePath,
                IssuerStorePath = configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath,
                TrustedStorePath = configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath
            };
            m_certificateGroups.Add(defaultApplicationGroup);
        }
        #endregion
        #region INodeManager Members
        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override NodeState AddBehaviourToPredefinedNode(ISystemContext context, NodeState predefinedNode)
        {
            BaseObjectState passiveNode = predefinedNode as BaseObjectState;

            if (passiveNode != null)
            {
                NodeId typeId = passiveNode.TypeDefinitionId;
                if (IsNodeIdInNamespace(typeId) && typeId.IdType == IdType.Numeric)
                {
                    switch ((uint)typeId.Identifier)
                    {

                        case ObjectTypes.ServerConfigurationType:
                            {
                                ServerConfigurationState activeNode = new ServerConfigurationState(passiveNode.Parent);
                                activeNode.Create(context, passiveNode);

                                m_serverConfigurationNode = activeNode;

                                // replace the node in the parent.
                                if (passiveNode.Parent != null)
                                {
                                    passiveNode.Parent.ReplaceChild(context, activeNode);
                                }
                                return activeNode;
                            }

                        case ObjectTypes.CertificateGroupFolderType:
                            {
                                CertificateGroupFolderState activeNode = new CertificateGroupFolderState(passiveNode.Parent);
                                activeNode.Create(context, passiveNode);

                                // delete unsupported groups
                                if (m_certificateGroups.FirstOrDefault(group => group.BrowseName == activeNode.DefaultHttpsGroup?.BrowseName) == null)
                                {
                                    activeNode.DefaultHttpsGroup = null;
                                }
                                if (m_certificateGroups.FirstOrDefault(group => group.BrowseName == activeNode.DefaultUserTokenGroup?.BrowseName) == null)
                                {
                                    activeNode.DefaultUserTokenGroup = null;
                                }
                                if (m_certificateGroups.FirstOrDefault(group => group.BrowseName == activeNode.DefaultApplicationGroup?.BrowseName) == null)
                                {
                                    activeNode.DefaultApplicationGroup = null;
                                }

                                // replace the node in the parent.
                                if (passiveNode.Parent != null)
                                {
                                    passiveNode.Parent.ReplaceChild(context, activeNode);
                                }
                                return activeNode;
                            }

                        case ObjectTypes.CertificateGroupType:
                            {
                                var result = m_certificateGroups.FirstOrDefault(group => group.BrowseName == passiveNode.BrowseName);
                                if (result != null)
                                {
                                    CertificateGroupState activeNode = new CertificateGroupState(passiveNode.Parent);
                                    activeNode.Create(context, passiveNode);

                                    result.NodeId = activeNode.NodeId;
                                    result.Node = activeNode;

                                    // replace the node in the parent.
                                    if (passiveNode.Parent != null)
                                    {
                                        passiveNode.Parent.ReplaceChild(context, activeNode);
                                    }
                                    return activeNode;
                                }
                            }
                            break;
                    }
                }
            }
            return base.AddBehaviourToPredefinedNode(context, predefinedNode);
        }
        #endregion
        #region Public methods
        /// <summary>
        /// Creates the configuration node for the server.
        /// </summary>
        public void CreateServerConfiguration(
            ServerSystemContext systemContext,
            ApplicationConfiguration configuration
            )
        {
            // setup server configuration node
            m_serverConfigurationNode.ServerCapabilities.Value = configuration.ServerConfiguration.ServerCapabilities.ToArray();
            m_serverConfigurationNode.ServerCapabilities.ValueRank = ValueRanks.OneDimension;
            m_serverConfigurationNode.ServerCapabilities.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            m_serverConfigurationNode.SupportedPrivateKeyFormats.Value = configuration.ServerConfiguration.SupportedPrivateKeyFormats.ToArray();
            m_serverConfigurationNode.SupportedPrivateKeyFormats.ValueRank = ValueRanks.OneDimension;
            m_serverConfigurationNode.SupportedPrivateKeyFormats.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            m_serverConfigurationNode.MaxTrustListSize.Value = (uint)configuration.ServerConfiguration.MaxTrustListSize;
            m_serverConfigurationNode.MulticastDnsEnabled.Value = configuration.ServerConfiguration.MultiCastDnsEnabled;

            m_serverConfigurationNode.UpdateCertificate.OnCall = new UpdateCertificateMethodStateMethodCallHandler(UpdateCertificate);
            m_serverConfigurationNode.CreateSigningRequest.OnCall = new CreateSigningRequestMethodStateMethodCallHandler(CreateSigningRequest);
            m_serverConfigurationNode.ApplyChanges.OnCallMethod = new GenericMethodCalledEventHandler(ApplyChanges);
            m_serverConfigurationNode.GetRejectedList.OnCall = new GetRejectedListMethodStateMethodCallHandler(GetRejectedList);
            m_serverConfigurationNode.ClearChangeMasks(systemContext, true);

            // setup certificate group trust list handlers
            foreach (var certGroup in m_certificateGroups)
            {
                certGroup.Node.CertificateTypes.Value =
                    certGroup.CertificateTypes;
                certGroup.Node.TrustList.Handle = new TrustList(
                    certGroup.Node.TrustList,
                    certGroup.TrustedStorePath,
                    certGroup.IssuerStorePath
                    );
                certGroup.Node.ClearChangeMasks(systemContext, true);
            }
        }
        #endregion
        #region Private Methods
        private ServiceResult UpdateCertificate(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                NodeId certificateGroupId,
                NodeId certificateTypeId,
                byte[] certificate,
                byte[][] issuerCertificates,
                string privateKeyFormat,
                byte[] privateKey,
                ref bool applyChangesRequired)
        {
            HasApplicationSecureAdminAccess(context);

            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(certificateGroupId, certificateTypeId);

            // Yet no support to replace private key
            if (!String.IsNullOrEmpty(privateKeyFormat))
            {
                throw new ArgumentException(nameof(privateKeyFormat));
            }

            if (privateKey != null)
            {
                throw new ArgumentException(nameof(privateKey));
            }

            // build issuer chain
            X509CertificateCollection newIssuerCollection = new X509CertificateCollection();
            foreach (byte[] issuerRawCert in issuerCertificates)
            {
                var newIssuerCert = new X509Certificate2(issuerRawCert);
                newIssuerCollection.Add(newIssuerCert);
            }

            // TODO: Verify the public key of the new certificate against the own private key
            // TODO: Verify the new cert chain
            // TODO: Add new cert to application store
            // TODO: Remove old cert from application store
            // TODO: Add issuer certs to issuer store

            return ServiceResult.Good;
        }


        private ServiceResult CreateSigningRequest(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                NodeId certificateGroupId,
                NodeId certificateTypeId,
                string subjectName,
                bool regeneratePrivateKey,
                byte[] nonce,
                ref byte[] certificateRequest)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(certificateGroupId, certificateTypeId);

            // TODO: use new subject
            if (!String.IsNullOrEmpty(subjectName))
            {
                throw new ArgumentException(nameof(subjectName));
            }

            // TODO: implement regeneratePrivateKey
            // TODO: use nonce for generating the private key

            var csrCertificate = CertificateFactory.CreateCertificateFromPKCS12(null, null);
            certificateRequest = CertificateFactory.CreateSigningRequest(csrCertificate);

            return ServiceResult.Good;
        }

        private ServiceResult ApplyChanges(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            HasApplicationSecureAdminAccess(context);

            // TODO: close all sessions, load new certificate

            return StatusCodes.Good;
        }

        private ServiceResult GetRejectedList(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref byte[][] certificates)
        {
            HasApplicationSecureAdminAccess(context);

            using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_rejectedStorePath))
            {
                X509Certificate2Collection collection = store.Enumerate().Result;
                List<byte[]> rawList = new List<byte[]>();
                foreach (var cert in collection)
                {
                    rawList.Add(cert.RawData);
                }
                certificates = rawList.ToArray();
            }
            return StatusCodes.Good;
        }

        private void HasApplicationSecureAdminAccess(ISystemContext context)
        {
            OperationContext operationContext = (context as SystemContext)?.OperationContext as OperationContext;
            if (operationContext != null)
            {
                if (operationContext.ChannelContext?.EndpointDescription?.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Secure Application Administrator access required.");
                }

                // TODO: role based access
                UserIdentity user = context.UserIdentity as UserIdentity;
                if (user?.TokenType == UserTokenType.Anonymous)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Secure Application Administrator access required.");
                }

            }
        }

        private ServerCertificateGroup VerifyGroupAndTypeId(
            NodeId certificateGroupId,
            NodeId certificateTypeId
            )
        {
            // verify requested certificate group
            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = ObjectIds.ServerConfigurationType_CertificateGroups_DefaultApplicationGroup;
            }

            ServerCertificateGroup certificateGroup = m_certificateGroups.FirstOrDefault(group => Utils.IsEqual(group.NodeId, certificateGroupId));
            if (certificateGroup == null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Certificate Group not found.");
            }

            // verify certificate type
            if (!NodeId.IsNull(certificateTypeId))
            {
                bool foundCertType = certificateGroup.CertificateTypes.Any(t => Utils.IsEqual(t, certificateTypeId));
                if (!foundCertType)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Certificate Type not supported.");
                }
            }
            return certificateGroup;
        }

        #endregion
        #region Private Fields
        private class ServerCertificateGroup
        {
            public string BrowseName;
            public NodeId NodeId;
            public CertificateGroupState Node;
            public NodeId[] CertificateTypes;
            public string ApplicationStorePath;
            public string IssuerStorePath;
            public string TrustedStorePath;
        };

        private ServerConfigurationState m_serverConfigurationNode;
        private IList<ServerCertificateGroup> m_certificateGroups;
        public readonly string m_rejectedStorePath;
        #endregion
    }
}
