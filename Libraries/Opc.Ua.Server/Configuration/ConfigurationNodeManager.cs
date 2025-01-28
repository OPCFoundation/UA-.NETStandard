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
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;


namespace Opc.Ua.Server
{

    /// <summary>
    /// Privileged identity which can access the system configuration.
    /// </summary>
    public class SystemConfigurationIdentity : RoleBasedIdentity
    {
        /// <summary>
        /// Create a user identity with the privilege
        /// to modify the system configuration.
        /// </summary>
        /// <param name="identity">The user identity.</param>
        public SystemConfigurationIdentity(IUserIdentity identity)
        : base(identity, new List<Role> { Role.SecurityAdmin, Role.ConfigureAdmin })
        {
        }
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
            string rejectedStorePath = configuration.SecurityConfiguration.RejectedCertificateStore?.StorePath;
            if (!string.IsNullOrEmpty(rejectedStorePath))
            {
                m_rejectedStore = new CertificateStoreIdentifier(rejectedStorePath);
            }
            m_certificateGroups = new List<ServerCertificateGroup>();
            m_configuration = configuration;
            // TODO: configure cert groups in configuration
            var defaultApplicationGroup = new ServerCertificateGroup {
                NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                BrowseName = BrowseNames.DefaultApplicationGroup,
                CertificateTypes = Array.Empty<NodeId>(),
                ApplicationCertificates = new CertificateIdentifierCollection(),
                IssuerStore = new CertificateStoreIdentifier(configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath),
                TrustedStore = new CertificateStoreIdentifier(configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath)
            };

            var defaultUserGroup = new ServerCertificateGroup {
                NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup,
                BrowseName = BrowseNames.DefaultUserTokenGroup,
                CertificateTypes = Array.Empty<NodeId>(),
                ApplicationCertificates = new CertificateIdentifierCollection(),
                IssuerStore = new CertificateStoreIdentifier(configuration.SecurityConfiguration.UserIssuerCertificates?.StorePath),
                TrustedStore = new CertificateStoreIdentifier(configuration.SecurityConfiguration.TrustedUserCertificates?.StorePath)
            };

            var defaultHttpsGroup = new ServerCertificateGroup {
                NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup,
                BrowseName = BrowseNames.DefaultHttpsGroup,
                CertificateTypes = Array.Empty<NodeId>(),
                ApplicationCertificates = new CertificateIdentifierCollection(),
                IssuerStore = new CertificateStoreIdentifier(configuration.SecurityConfiguration.HttpsIssuerCertificates?.StorePath),
                TrustedStore = new CertificateStoreIdentifier(configuration.SecurityConfiguration.TrustedHttpsCertificates?.StorePath)
            };

            // For each certificate in ApplicationCertificates, add the certificate type to ServerConfiguration_CertificateGroups_DefaultApplicationGroup
            // under the CertificateTypes field.
            foreach (CertificateIdentifier cert in configuration.SecurityConfiguration.ApplicationCertificates)
            {
                defaultApplicationGroup.CertificateTypes = defaultApplicationGroup.CertificateTypes.Concat(new NodeId[] { cert.CertificateType }).ToArray();
                defaultApplicationGroup.ApplicationCertificates.Add(cert);

                if (cert.CertificateType == ObjectTypeIds.HttpsCertificateType)
                {
                    defaultHttpsGroup.CertificateTypes = defaultHttpsGroup.CertificateTypes.Concat(new NodeId[] { cert.CertificateType }).ToArray();
                    defaultHttpsGroup.ApplicationCertificates.Add(cert);
                }
            }

            m_certificateGroups.Add(defaultApplicationGroup);
            m_certificateGroups.Add(defaultHttpsGroup);
            m_certificateGroups.Add(defaultUserGroup);
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
            if (predefinedNode is BaseObjectState passiveNode)
            {
                NodeId typeId = passiveNode.TypeDefinitionId;
                if (IsNodeIdInNamespace(typeId) && typeId.IdType == IdType.Numeric)
                {
                    switch ((uint)typeId.Identifier)
                    {

                        case ObjectTypes.ServerConfigurationType:
                        {
                            var activeNode = new ServerConfigurationState(passiveNode.Parent);

                            activeNode.GetCertificates = new GetCertificatesMethodState(activeNode);

                            activeNode.Create(context, passiveNode);

                            m_serverConfigurationNode = activeNode;

                            // replace the node in the parent.
                            if (passiveNode.Parent != null)
                            {
                                passiveNode.Parent.ReplaceChild(context, activeNode);
                            }
                            else
                            {
                                NodeState serverNode = FindNodeInAddressSpace(ObjectIds.Server);
                                serverNode?.ReplaceChild(context, activeNode);
                            }
                            // remove the reference to server node because it is set as parent
                            activeNode.RemoveReference(ReferenceTypeIds.HasComponent, true, ObjectIds.Server);

                            return activeNode;
                        }

                        case ObjectTypes.CertificateGroupFolderType:
                        {
                            var activeNode = new CertificateGroupFolderState(passiveNode.Parent);
                            activeNode.Create(context, passiveNode);

                            // delete unsupported groups
                            if (m_certificateGroups.All(group => group.BrowseName != activeNode.DefaultHttpsGroup?.BrowseName))
                            {
                                activeNode.DefaultHttpsGroup = null;
                            }
                            if (m_certificateGroups.All(group => group.BrowseName != activeNode.DefaultUserTokenGroup?.BrowseName))
                            {
                                activeNode.DefaultUserTokenGroup = null;
                            }
                            if (m_certificateGroups.All(group => group.BrowseName != activeNode.DefaultApplicationGroup?.BrowseName))
                            {
                                activeNode.DefaultApplicationGroup = null;
                            }

                            // replace the node in the parent.
                            passiveNode.Parent?.ReplaceChild(context, activeNode);
                            return activeNode;
                        }

                        case ObjectTypes.CertificateGroupType:
                        {
                            ServerCertificateGroup result = m_certificateGroups.FirstOrDefault(group => group.NodeId == passiveNode.NodeId);

                            if (result != null)
                            {
                                var activeNode = new CertificateGroupState(passiveNode.Parent);
                                activeNode.Create(context, passiveNode);

                                result.NodeId = activeNode.NodeId;
                                result.Node = activeNode;

                                // replace the node in the parent.
                                passiveNode.Parent?.ReplaceChild(context, activeNode);
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
            ApplicationConfiguration configuration)
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
            m_serverConfigurationNode.GetCertificates.OnCall = new GetCertificatesMethodStateMethodCallHandler(GetCertificates);
            m_serverConfigurationNode.ClearChangeMasks(systemContext, true);

            // setup certificate group trust list handlers
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                certGroup.Node.CertificateTypes.Value =
                    certGroup.CertificateTypes;
                certGroup.Node.TrustList.Handle = new TrustList(
                    certGroup.Node.TrustList,
                    certGroup.TrustedStore,
                    certGroup.IssuerStore,
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess));
                certGroup.Node.ClearChangeMasks(systemContext, true);
            }

            // find ServerNamespaces node and subscribe to StateChanged

            if (FindPredefinedNode(ObjectIds.Server_Namespaces, typeof(NamespacesState)) is NamespacesState serverNamespacesNode)
            {
                serverNamespacesNode.StateChanged += ServerNamespacesChanged;
            }
        }



        /// <summary>
        /// Gets and returns the <see cref="NamespaceMetadataState"/> node associated with the specified NamespaceUri
        /// </summary>
        /// <param name="namespaceUri"></param>
        /// <returns></returns>
        public NamespaceMetadataState GetNamespaceMetadataState(string namespaceUri)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            if (m_namespaceMetadataStates.TryGetValue(namespaceUri, out NamespaceMetadataState value))
            {
                return value;
            }

            NamespaceMetadataState namespaceMetadataState = FindNamespaceMetadataState(namespaceUri);

            lock (Lock)
            {
                // remember the result for faster access.
                m_namespaceMetadataStates[namespaceUri] = namespaceMetadataState;
            }

            return namespaceMetadataState;
        }

        /// <summary>
        /// Gets or creates the <see cref="NamespaceMetadataState"/> node for the specified NamespaceUri.
        /// </summary>
        /// <param name="namespaceUri"></param>
        /// <returns></returns>
        public NamespaceMetadataState CreateNamespaceMetadataState(string namespaceUri)
        {
            NamespaceMetadataState namespaceMetadataState = FindNamespaceMetadataState(namespaceUri);

            if (namespaceMetadataState == null)
            {
                // find ServerNamespaces node
                if (FindPredefinedNode(ObjectIds.Server_Namespaces, typeof(NamespacesState)) is not NamespacesState serverNamespacesNode)
                {
                    Utils.LogError("Cannot create NamespaceMetadataState for namespace '{0}'.", namespaceUri);
                    return null;
                }

                // create the NamespaceMetadata node
                namespaceMetadataState = new NamespaceMetadataState(serverNamespacesNode);
                namespaceMetadataState.BrowseName = new QualifiedName(namespaceUri, NamespaceIndex);
                namespaceMetadataState.Create(SystemContext, null, namespaceMetadataState.BrowseName, null, true);
                namespaceMetadataState.DisplayName = namespaceUri;
                namespaceMetadataState.SymbolicName = namespaceUri;
                namespaceMetadataState.NamespaceUri.Value = namespaceUri;

                // add node as child of ServerNamespaces and in predefined nodes
                serverNamespacesNode.AddChild(namespaceMetadataState);
                serverNamespacesNode.ClearChangeMasks(Server.DefaultSystemContext, true);
                AddPredefinedNode(SystemContext, namespaceMetadataState);
            }

            return namespaceMetadataState;
        }

        /// <summary>
        /// Determine if the impersonated user has admin access.
        /// </summary>
        /// <param name="context"></param>
        /// <exception cref="ServiceResultException"/>
        /// <seealso cref="StatusCodes.BadUserAccessDenied"/>
        public void HasApplicationSecureAdminAccess(ISystemContext context)
        {
            HasApplicationSecureAdminAccess(context, null);
        }


        /// <summary>
        /// Determine if the impersonated user has admin access.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="_"></param>
        /// <exception cref="ServiceResultException"/>
        /// <seealso cref="StatusCodes.BadUserAccessDenied"/>
        public void HasApplicationSecureAdminAccess(ISystemContext context, CertificateStoreIdentifier _)
        {
            if ((context as SystemContext)?.OperationContext is OperationContext operationContext)
            {
                if (operationContext.ChannelContext?.EndpointDescription?.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Access to this item is only allowed with MessageSecurityMode SignAndEncrypt.");
                }
                IUserIdentity identity = operationContext.UserIdentity;
                // allow access to system configuration only with Role SecurityAdmin
                if (identity == null || identity.TokenType == UserTokenType.Anonymous ||
                    !identity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Security Admin Role required to access this item.");
                }

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

            object[] inputArguments = new object[] { certificateGroupId, certificateTypeId, certificate, issuerCertificates, privateKeyFormat, privateKey };
            X509Certificate2 newCert = null;

            Server.ReportCertificateUpdateRequestedAuditEvent(context, objectId, method, inputArguments);
            try
            {
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

                try
                {
                    newCert = X509CertificateLoader.LoadCertificate(certificate);
                }
                catch
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Certificate data is invalid.");
                }

                // validate certificate type of new certificate
                if (!CertificateIdentifier.ValidateCertificateType(newCert, certificateTypeId))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Certificate type of new certificate doesn't match the provided certificate type.");
                }

                // identify the existing certificate to be updated
                // it should be of the same type and same subject name as the new certificate
                CertificateIdentifier existingCertIdentifier = certificateGroup.ApplicationCertificates.FirstOrDefault(cert =>
                    X509Utils.CompareDistinguishedName(cert.Certificate.Subject, newCert.Subject) &&
                    cert.CertificateType == certificateTypeId);

                // if no cert was found search by ApplicationUri
                existingCertIdentifier ??= certificateGroup.ApplicationCertificates.FirstOrDefault(cert =>
                    m_configuration.ApplicationUri == X509Utils.GetApplicationUriFromCertificate(cert.Certificate) &&
                    cert.CertificateType == certificateTypeId);

                // if there is no such existing certificate then this is an error
                if (existingCertIdentifier == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "No existing certificate found for the specified certificate type and subject name.");
                }


                var newIssuerCollection = new X509Certificate2Collection();

                try
                {
                    // build issuer chain
                    if (issuerCertificates != null)
                    {
                        foreach (byte[] issuerRawCert in issuerCertificates)
                        {
                            X509Certificate2 newIssuerCert = X509CertificateLoader.LoadCertificate(issuerRawCert);
                            newIssuerCollection.Add(newIssuerCert);
                        }
                    }

                }
                catch
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Certificate data is invalid.");
                }

                // self signed
                bool selfSigned = X509Utils.IsSelfSigned(newCert);
                if (selfSigned && newIssuerCollection.Count != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Issuer list not empty for self signed certificate.");
                }

                if (!selfSigned)
                {
                    try
                    {
                        // verify cert with issuer chain
                        var certValidator = new CertificateValidator();
                        var issuerStore = new CertificateTrustList();
                        var issuerCollection = new CertificateIdentifierCollection();
                        foreach (X509Certificate2 issuerCert in newIssuerCollection)
                        {
                            issuerCollection.Add(new CertificateIdentifier(issuerCert));
                        }
                        issuerStore.TrustedCertificates = issuerCollection;
                        certValidator.Update(issuerStore, issuerStore, null);
                        certValidator.Validate(newCert);
                    }
                    catch (Exception ex)
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Failed to verify integrity of the new certificate and the issuer list.", ex);
                    }
                }

                var updateCertificate = new UpdateCertificateData();
                try
                {
                    ICertificatePasswordProvider passwordProvider = m_configuration.SecurityConfiguration.CertificatePasswordProvider;
                    switch (privateKeyFormat)
                    {
                        case null:
                        case "":
                        {
                            X509Certificate2 exportableKey;
                            //use the new generated private key if one exists and matches the provided public key
                            if (certificateGroup.TemporaryApplicationCertificate != null && X509Utils.VerifyKeyPair(newCert, certificateGroup.TemporaryApplicationCertificate))
                            {
                                exportableKey = X509Utils.CreateCopyWithPrivateKey(certificateGroup.TemporaryApplicationCertificate, false);
                            }
                            else
                            {
                                X509Certificate2 certWithPrivateKey = existingCertIdentifier.LoadPrivateKeyEx(passwordProvider, m_configuration.ApplicationUri).Result;
                                exportableKey = X509Utils.CreateCopyWithPrivateKey(certWithPrivateKey, false);
                            }

                            updateCertificate.CertificateWithPrivateKey = CertificateFactory.CreateCertificateWithPrivateKey(newCert, exportableKey);
                            break;
                        }
                        case "PFX":
                        {
                            X509Certificate2 certWithPrivateKey = X509Utils.CreateCertificateFromPKCS12(privateKey, passwordProvider?.GetPassword(existingCertIdentifier), true);
                            updateCertificate.CertificateWithPrivateKey = CertificateFactory.CreateCertificateWithPrivateKey(newCert, certWithPrivateKey);
                            break;
                        }
                        case "PEM":
                        {
                            updateCertificate.CertificateWithPrivateKey = CertificateFactory.CreateCertificateWithPEMPrivateKey(newCert, privateKey, passwordProvider?.GetPassword(existingCertIdentifier));
                            break;
                        }
                    }
                    //dispose temporary new private key as it is no longer needed
                    certificateGroup.TemporaryApplicationCertificate?.Dispose();
                    certificateGroup.TemporaryApplicationCertificate = null;

                    updateCertificate.IssuerCollection = newIssuerCollection;
                    updateCertificate.SessionId = context.SessionId;
                }
                catch
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Failed to verify integrity of the new certificate and the private key.");
                }

                certificateGroup.UpdateCertificate = updateCertificate;
                applyChangesRequired = true;

                if (updateCertificate != null)
                {
                    try
                    {
                        using (ICertificateStore appStore = existingCertIdentifier.OpenStore())
                        {
                            Utils.LogCertificate(Utils.TraceMasks.Security, "Delete application certificate: ", existingCertIdentifier.Certificate);
                            appStore.Delete(existingCertIdentifier.Thumbprint).Wait();
                            Utils.LogCertificate(Utils.TraceMasks.Security, "Add new application certificate: ", updateCertificate.CertificateWithPrivateKey);
                            ICertificatePasswordProvider passwordProvider = m_configuration.SecurityConfiguration.CertificatePasswordProvider;
                            appStore.Add(updateCertificate.CertificateWithPrivateKey, passwordProvider?.GetPassword(existingCertIdentifier)).Wait();
                            // keep only track of cert without private key
                            X509Certificate2 certOnly = X509CertificateLoader.LoadCertificate(updateCertificate.CertificateWithPrivateKey.RawData);
                            updateCertificate.CertificateWithPrivateKey.Dispose();
                            updateCertificate.CertificateWithPrivateKey = certOnly;
                        }

                        ICertificateStore issuerStore = certificateGroup.IssuerStore.OpenStore();
                        try
                        {
                            foreach (X509Certificate2 issuer in updateCertificate.IssuerCollection)
                            {
                                try
                                {
                                    Utils.LogCertificate(Utils.TraceMasks.Security, "Add new issuer certificate: ", issuer);
                                    issuerStore.Add(issuer).Wait();
                                }
                                catch (ArgumentException)
                                {
                                    // ignore error if issuer cert already exists
                                }
                            }
                        }
                        finally
                        {
                            issuerStore?.Close();
                        }

                        Server.ReportCertificateUpdatedAuditEvent(context, objectId, method, inputArguments, certificateGroupId, certificateTypeId);
                    }
                    catch (Exception ex)
                    {
                        Utils.LogError(Utils.TraceMasks.Security, ServiceResult.BuildExceptionTrace(ex));
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Failed to update certificate.", ex);
                    }
                }
            }
            catch (Exception e)
            {
                // report the failure of UpdateCertificate via an audit event
                Server.ReportCertificateUpdatedAuditEvent(context, objectId, method, inputArguments, certificateGroupId, certificateTypeId, e);
                // Raise audit certificate event 
                Server.ReportAuditCertificateEvent(newCert, e);
                throw;
            }

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

            // identify the existing certificate for which to CreateSigningRequest
            // it should be of the same type
            CertificateIdentifier existingCertIdentifier = certificateGroup.ApplicationCertificates.FirstOrDefault(cert =>
                cert.CertificateType == certificateTypeId);

            if (string.IsNullOrEmpty(subjectName))
            {
                subjectName = existingCertIdentifier.Certificate.Subject;
            }

            certificateGroup.TemporaryApplicationCertificate?.Dispose();
            certificateGroup.TemporaryApplicationCertificate = null;

            X509Certificate2 certWithPrivateKey;
            if (regeneratePrivateKey)
            {
                certWithPrivateKey = GenerateTemporaryApplicationCertificate(certificateTypeId, certificateGroup, subjectName);
            }
            else
            {
                ICertificatePasswordProvider passwordProvider = m_configuration.SecurityConfiguration.CertificatePasswordProvider;
                certWithPrivateKey = existingCertIdentifier.LoadPrivateKeyEx(passwordProvider).Result;
            }

            Utils.LogCertificate(Utils.TraceMasks.Security, "Create signing request: ", certWithPrivateKey);
            certificateRequest = CertificateFactory.CreateSigningRequest(certWithPrivateKey, X509Utils.GetDomainsFromCertificate(certWithPrivateKey));

            return ServiceResult.Good;
        }


        private X509Certificate2 GenerateTemporaryApplicationCertificate(NodeId certificateTypeId, ServerCertificateGroup certificateGroup, string subjectName)
        {
            X509Certificate2 certificate;

            ICertificateBuilder certificateBuilder = CertificateFactory.CreateCertificate(
                    m_configuration.ApplicationUri,
                    null,
                    subjectName,
                    null)
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetNotAfter(DateTime.Today.AddDays(14));

            if (certificateTypeId == null ||
                certificateTypeId == ObjectTypeIds.ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                certificate = certificateBuilder
                    .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();
            }
            else
            {
#if !ECC_SUPPORT
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The Ecc certificate type is not supported.");
#else
                ECCurve? curve = EccUtils.GetCurveFromCertificateTypeId(certificateTypeId);

                if (curve == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "The Ecc certificate type is not supported.");
                }
                certificate = certificateBuilder
                   .SetECCurve(curve.Value)
                   .CreateForECDsa();
#endif
            }

            certificateGroup.TemporaryApplicationCertificate = certificate;

            return certificate;
        }
        private ServiceResult ApplyChanges(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            HasApplicationSecureAdminAccess(context);

            bool disconnectSessions = false;

            foreach (ServerCertificateGroup certificateGroup in m_certificateGroups)
            {
                try
                {
                    UpdateCertificateData updateCertificate = certificateGroup.UpdateCertificate;
                    if (updateCertificate != null)
                    {
                        disconnectSessions = true;
                        Utils.LogCertificate((int)Utils.TraceMasks.Security, "Apply Changes for certificate: ",
                            updateCertificate.CertificateWithPrivateKey);
                    }
                }
                finally
                {
                    certificateGroup.UpdateCertificate = null;
                }
            }

            if (disconnectSessions)
            {
                Task.Run(async () => {
                    Utils.LogInfo((int)Utils.TraceMasks.Security, "Apply Changes for application certificate update.");
                    // give the client some time to receive the response
                    // before the certificate update may disconnect all sessions
                    await Task.Delay(1000).ConfigureAwait(false);
                    try
                    {
                        await m_configuration.CertificateValidator.UpdateCertificateAsync(m_configuration.SecurityConfiguration, m_configuration.ApplicationUri).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Utils.LogCritical(ex, "Failed to sucessfully Apply Changes: Error updating application instance certificates. Server could be in faulted state.");
                        throw;
                    }
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

            // No rejected store configured
            if (m_rejectedStore == null)
            {
                certificates = Array.Empty<byte[]>();
                return StatusCodes.Good;
            }

            ICertificateStore store = m_rejectedStore.OpenStore();
            try
            {
                X509Certificate2Collection collection = store.Enumerate().Result;
                var rawList = new List<byte[]>();
                foreach (X509Certificate2 cert in collection)
                {
                    rawList.Add(cert.RawData);
                }
                certificates = rawList.ToArray();
            }
            finally
            {
                store?.Close();
            }

            return StatusCodes.Good;
        }

        private ServiceResult GetCertificates(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            ref NodeId[] certificateTypeIds,
            ref byte[][] certificates)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup = m_certificateGroups.FirstOrDefault(group => Utils.IsEqual(group.NodeId, certificateGroupId));
            if (certificateGroup == null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Certificate group invalid.");
            }

            certificateTypeIds = certificateGroup.CertificateTypes;
            certificates = certificateGroup.ApplicationCertificates.Select(s => s.Certificate.RawData).ToArray();

            return ServiceResult.Good;
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

        /// <summary>
        /// Finds the <see cref="NamespaceMetadataState"/> node for the specified NamespaceUri.
        /// </summary>
        /// <param name="namespaceUri"></param>
        private NamespaceMetadataState FindNamespaceMetadataState(string namespaceUri)
        {
            try
            {
                // find ServerNamespaces node
                if (FindPredefinedNode(ObjectIds.Server_Namespaces, typeof(NamespacesState)) is not NamespacesState serverNamespacesNode)
                {
                    Utils.LogError("Cannot find ObjectIds.Server_Namespaces node.");
                    return null;
                }

                IList<BaseInstanceState> serverNamespacesChildren = new List<BaseInstanceState>();
                serverNamespacesNode.GetChildren(SystemContext, serverNamespacesChildren);

                foreach (BaseInstanceState namespacesReference in serverNamespacesChildren)
                {
                    // Find NamespaceMetadata node of NamespaceUri in Namespaces children

                    if (namespacesReference is not NamespaceMetadataState namespaceMetadata)
                    {
                        continue;
                    }

                    if (namespaceMetadata.NamespaceUri.Value == namespaceUri)
                    {
                        return namespaceMetadata;
                    }
                    else
                    {
                        continue;
                    }
                }

                IList<IReference> serverNamespacesReferencs = new List<IReference>();
                serverNamespacesNode.GetReferences(SystemContext, serverNamespacesReferencs);

                foreach (IReference serverNamespacesReference in serverNamespacesReferencs)
                {
                    if (!serverNamespacesReference.IsInverse)
                    {
                        // Find NamespaceMetadata node of NamespaceUri in Namespaces references
                        var nameSpaceNodeId = ExpandedNodeId.ToNodeId(serverNamespacesReference.TargetId, Server.NamespaceUris);

                        if (FindNodeInAddressSpace(nameSpaceNodeId) is not NamespaceMetadataState namespaceMetadata)
                        {
                            continue;
                        }

                        if (namespaceMetadata.NamespaceUri.Value == namespaceUri)
                        {
                            return namespaceMetadata;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error searching NamespaceMetadata for namespaceUri {0}.", namespaceUri);
                return null;
            }
        }

        /// <summary>
        /// Clear NamespaceMetadata nodes cache in case nodes are added or deleted
        /// </summary>
        private void ServerNamespacesChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Children) != 0 ||
                (changes & NodeStateChangeMasks.References) != 0)
            {
                try
                {
                    lock (Lock)
                    {
                        m_namespaceMetadataStates.Clear();
                    }
                }
                catch
                {
                    // ignore errors
                }
            }
        }
        #endregion

        #region Private Fields
        private class UpdateCertificateData
        {
            public NodeId SessionId;
            public X509Certificate2 CertificateWithPrivateKey;
            public X509Certificate2Collection IssuerCollection;
        }

        private class ServerCertificateGroup
        {
            public string BrowseName;
            public NodeId NodeId;
            public CertificateGroupState Node;
            public NodeId[] CertificateTypes;
            public CertificateIdentifierCollection ApplicationCertificates;
            public CertificateStoreIdentifier IssuerStore;
            public CertificateStoreIdentifier TrustedStore;
            public UpdateCertificateData UpdateCertificate;
            public X509Certificate2 TemporaryApplicationCertificate;
        }

        private ServerConfigurationState m_serverConfigurationNode;
        private ApplicationConfiguration m_configuration;
        private IList<ServerCertificateGroup> m_certificateGroups;
        private CertificateStoreIdentifier m_rejectedStore;
        private Dictionary<string, NamespaceMetadataState> m_namespaceMetadataStates = new Dictionary<string, NamespaceMetadataState>();
        #endregion
    }
}
