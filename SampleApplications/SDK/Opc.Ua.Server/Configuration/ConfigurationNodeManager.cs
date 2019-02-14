/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Special identity only for the system configuration.
    /// </summary>
    public class SystemConfigurationIdentity : IUserIdentity
    {
        private IUserIdentity m_identity;

        public SystemConfigurationIdentity(IUserIdentity identity)
        {
            m_identity = identity;
        }
        #region IUserIdentity
        public string DisplayName
        {
            get { return m_identity.DisplayName; }
        }

        /// <summary>
        /// The user token policy.
        /// </summary>
        /// <value>The user token policy.</value>
        public string PolicyId
        {
            get { return m_identity.PolicyId; }
        }

        public UserTokenType TokenType
        {
            get { return m_identity.TokenType; }
        }

        public XmlQualifiedName IssuedTokenType
        {
            get { return m_identity.IssuedTokenType; }
        }

        public bool SupportsSignatures
        {
            get { return m_identity.SupportsSignatures; }
        }

        public UserIdentityToken GetIdentityToken()
        {
            return m_identity.GetIdentityToken();
        }
        #endregion
    }

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
            m_configuration = configuration;
            // TODO: configure cert groups in configuration
            ServerCertificateGroup defaultApplicationGroup = new ServerCertificateGroup
            {
                BrowseName = Opc.Ua.BrowseNames.DefaultApplicationGroup,
                CertificateTypes = new NodeId[] { ObjectTypeIds.RsaSha256ApplicationCertificateType },
                ApplicationCertificate = configuration.SecurityConfiguration.ApplicationCertificate,
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
        protected override NodeState AddBehaviourToPredefinedNode(
            ISystemContext context, 
            NodeState predefinedNode)
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
                    certGroup.IssuerStorePath,
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess)
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

            privateKeyFormat = privateKeyFormat?.ToUpper();
            if (!(String.IsNullOrEmpty(privateKeyFormat) || privateKeyFormat == "PEM" || privateKeyFormat == "PFX"))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The private key format is not supported.");
            }

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(certificateGroupId, certificateTypeId);
            certificateGroup.UpdateCertificate = null;

            X509Certificate2Collection newIssuerCollection = new X509Certificate2Collection();
            X509Certificate2 newCert;
            try
            {
                // build issuer chain
                if (issuerCertificates != null)
                {
                    foreach (byte[] issuerRawCert in issuerCertificates)
                    {
                        var newIssuerCert = new X509Certificate2(issuerRawCert);
                        newIssuerCollection.Add(newIssuerCert);
                    }
                }

                newCert = new X509Certificate2(certificate);
            }
            catch
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Certificate data is invalid.");
            }

            // load existing application cert and private key
            if (!Utils.CompareDistinguishedName(certificateGroup.ApplicationCertificate.SubjectName, newCert.SubjectName.Name))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Subject Name of new certificate doesn't match the application.");
            }

            // self signed
            bool selfSigned = Utils.CompareDistinguishedName(newCert.Subject, newCert.Issuer);
            if (selfSigned && newIssuerCollection.Count != 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Issuer list not empty for self signed certificate.");
            }

            if (!selfSigned)
            {
                try
                {
                    // verify cert with issuer chain
                    CertificateValidator certValidator = new CertificateValidator();
                    CertificateTrustList issuerStore = new CertificateTrustList();
                    CertificateIdentifierCollection issuerCollection = new CertificateIdentifierCollection();
                    foreach (var issuerCert in newIssuerCollection)
                    {
                        issuerCollection.Add(new CertificateIdentifier(issuerCert));
                    }
                    issuerStore.TrustedCertificates = issuerCollection;
                    certValidator.Update(issuerStore, issuerStore, null);
                    certValidator.Validate(newCert);
                }
                catch
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Failed to verify integrity of the new certificate and the issuer list.");
                }
            }

            var updateCertificate = new UpdateCertificateData();
            try
            {
                string password = String.Empty;
                switch (privateKeyFormat)
                {
                    case null:
                    case "":
                        {
                            X509Certificate2 certWithPrivateKey = certificateGroup.ApplicationCertificate.LoadPrivateKey(password).Result;
                            updateCertificate.CertificateWithPrivateKey = CertificateFactory.CreateCertificateWithPrivateKey(newCert, certWithPrivateKey);
                            break;
                        }
                    case "PFX":
                        {
                            X509Certificate2 certWithPrivateKey = CertificateFactory.CreateCertificateFromPKCS12(privateKey, password);
                            updateCertificate.CertificateWithPrivateKey = CertificateFactory.CreateCertificateWithPrivateKey(newCert, certWithPrivateKey);
                            break;
                        }
                    case "PEM":
                        {
                            updateCertificate.CertificateWithPrivateKey = CertificateFactory.CreateCertificateWithPEMPrivateKey(newCert, privateKey, password);
                            break;
                        }
                }
                updateCertificate.IssuerCollection = newIssuerCollection;
                updateCertificate.SessionId = context.SessionId;
            }
            catch
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Failed to verify integrity of the new certificate and the private key.");
            }

            certificateGroup.UpdateCertificate = updateCertificate;
            applyChangesRequired = true;

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

            if (!String.IsNullOrEmpty(subjectName))
            {
                throw new ArgumentException(nameof(subjectName));
            }

            // TODO: implement regeneratePrivateKey
            // TODO: use nonce for generating the private key

            string password = String.Empty;
            X509Certificate2 certWithPrivateKey = certificateGroup.ApplicationCertificate.LoadPrivateKey(password).Result;
            certificateRequest = CertificateFactory.CreateSigningRequest(certWithPrivateKey, Utils.GetDomainsFromCertficate(certWithPrivateKey));
            return ServiceResult.Good;
        }

        private ServiceResult ApplyChanges(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            HasApplicationSecureAdminAccess(context);

            bool disconnectSessions = false;

            foreach (var certificateGroup in m_certificateGroups)
            {
                try
                {
                    var updateCertificate = certificateGroup.UpdateCertificate;
                    if (updateCertificate != null)
                    {
                        if (certificateGroup.UpdateCertificate.SessionId == context.SessionId)
                        {
                            using (ICertificateStore appStore = CertificateStoreIdentifier.OpenStore(certificateGroup.ApplicationCertificate.StorePath))
                            {
                                appStore.Delete(certificateGroup.ApplicationCertificate.Thumbprint).Wait();
                                appStore.Add(updateCertificate.CertificateWithPrivateKey).Wait();
                                updateCertificate.CertificateWithPrivateKey = null;
                            }
                            using (ICertificateStore issuerStore = CertificateStoreIdentifier.OpenStore(certificateGroup.IssuerStorePath))
                            {
                                foreach (var issuer in updateCertificate.IssuerCollection)
                                {
                                    try
                                    {
                                        issuerStore.Add(issuer).Wait();
                                    }
                                    catch (ArgumentException)
                                    {
                                        // ignore error if issuer cert already exists
                                    }
                                }
                            }

                            disconnectSessions = true;
                        }
                    }

                }
                finally
                {
                    certificateGroup.UpdateCertificate = null;
                }
            }

            if (disconnectSessions)
            {
                Task.Run(async () =>
                    {
                        // give the client some time to receive the response
                        // before the certificate update may disconnect all sessions
                        await Task.Delay(1000);
                        await m_configuration.CertificateValidator.UpdateCertificate(m_configuration.SecurityConfiguration);
                    }
                );
            }

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

                // allow access to system configuration only through special identity
                SystemConfigurationIdentity user = context.UserIdentity as SystemConfigurationIdentity;
                if (user == null || user.TokenType == UserTokenType.Anonymous)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "System Configuration Administrator access required.");
                }

            }
        }

        private ServerCertificateGroup VerifyGroupAndTypeId(
            NodeId certificateGroupId,
            NodeId certificateTypeId
            )
        {
            // verify typeid must be set
            if (NodeId.IsNull(certificateTypeId))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Certificate type not specified.");
            }

            // verify requested certificate group
            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
            }

            ServerCertificateGroup certificateGroup = m_certificateGroups.FirstOrDefault(group => Utils.IsEqual(group.NodeId, certificateGroupId));
            if (certificateGroup == null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Certificate group invalid.");
            }

            // verify certificate type
            bool foundCertType = certificateGroup.CertificateTypes.Any(t => Utils.IsEqual(t, certificateTypeId));
            if (!foundCertType)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Certificate type not valid for certificate group.");
            }

            return certificateGroup;
        }
        #endregion

        #region Private Fields            
        private class UpdateCertificateData
        {
            public NodeId SessionId;
            public X509Certificate2 CertificateWithPrivateKey;
            public X509Certificate2Collection IssuerCollection;
        };
        private class ServerCertificateGroup
        {
            public string BrowseName;
            public NodeId NodeId;
            public CertificateGroupState Node;
            public NodeId[] CertificateTypes;
            public CertificateIdentifier ApplicationCertificate;
            public string IssuerStorePath;
            public string TrustedStorePath;
            public UpdateCertificateData UpdateCertificate;
        };

        private ServerConfigurationState m_serverConfigurationNode;
        private ApplicationConfiguration m_configuration;
        private IList<ServerCertificateGroup> m_certificateGroups;
        private readonly string m_rejectedStorePath;
        #endregion
    }
}
