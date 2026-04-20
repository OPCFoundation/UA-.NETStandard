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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
using System.Security.Cryptography;
using System.Diagnostics;
#if !NET9_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Opc.Ua.Server
{
    /// <summary>
    /// The Server Configuration Node Manager.
    /// </summary>
    public class ConfigurationNodeManager : DiagnosticsNodeManager, IConfigurationNodeManager
    {
        /// <summary>
        /// Initializes the configuration and diagnostics manager.
        /// </summary>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, server.Telemetry.CreateLogger<ConfigurationNodeManager>())
        {
        }

        /// <summary>
        /// Initializes the configuration and diagnostics manager.
        /// </summary>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger)
            : base(server, configuration, logger)
        {
            string rejectedStorePath = configuration.SecurityConfiguration.RejectedCertificateStore?
                .StorePath;
            if (!string.IsNullOrEmpty(rejectedStorePath))
            {
                m_rejectedStore = new CertificateStoreIdentifier(rejectedStorePath);
            }
            m_certificateGroups = [];
            m_configuration = configuration;
            // TODO: configure cert groups in configuration
            var defaultApplicationGroup = new ServerCertificateGroup
            {
                NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                BrowseName = BrowseNames.DefaultApplicationGroup,
                CertificateTypes = [],
                ApplicationCertificates = [],
                IssuerStore = new CertificateStoreIdentifier(
                    configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath
                ),
                TrustedStore = new CertificateStoreIdentifier(
                    configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath)
            };
            m_certificateGroups.Add(defaultApplicationGroup);

            if (configuration.SecurityConfiguration.UserIssuerCertificates != null &&
                configuration.SecurityConfiguration.TrustedUserCertificates != null)
            {
                var defaultUserGroup = new ServerCertificateGroup
                {
                    NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup,
                    BrowseName = BrowseNames.DefaultUserTokenGroup,
                    CertificateTypes = [],
                    ApplicationCertificates = [],
                    IssuerStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.UserIssuerCertificates.StorePath
                    ),
                    TrustedStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedUserCertificates.StorePath)
                };

                m_certificateGroups.Add(defaultUserGroup);
            }
            ServerCertificateGroup defaultHttpsGroup = null;
            if (configuration.SecurityConfiguration.HttpsIssuerCertificates != null &&
                configuration.SecurityConfiguration.TrustedHttpsCertificates != null)
            {
                defaultHttpsGroup = new ServerCertificateGroup
                {
                    NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup,
                    BrowseName = BrowseNames.DefaultHttpsGroup,
                    CertificateTypes = [],
                    ApplicationCertificates = [],
                    IssuerStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.HttpsIssuerCertificates.StorePath
                    ),
                    TrustedStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedHttpsCertificates.StorePath)
                };

                m_certificateGroups.Add(defaultHttpsGroup);
            }

            // For each certificate in ApplicationCertificates, add the certificate type to ServerConfiguration_CertificateGroups_DefaultApplicationGroup
            // under the CertificateTypes field.
            foreach (CertificateIdentifier cert in configuration.SecurityConfiguration
                .ApplicationCertificates)
            {
                defaultApplicationGroup.CertificateTypes =
                [
                    .. defaultApplicationGroup.CertificateTypes,
                    .. new NodeId[] { cert.CertificateType }
                ];
                defaultApplicationGroup.ApplicationCertificates =
                    defaultApplicationGroup.ApplicationCertificates.AddItem(cert);

                if (cert.CertificateType == ObjectTypeIds.HttpsCertificateType &&
                    defaultHttpsGroup != null)
                {
                    defaultHttpsGroup.CertificateTypes =
                    [
                        .. defaultHttpsGroup.CertificateTypes,
                        .. new NodeId[] { cert.CertificateType }
                    ];
                    defaultHttpsGroup.ApplicationCertificates =
                        defaultHttpsGroup.ApplicationCertificates.AddItem(cert);
                }
            }
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override async ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            if (predefinedNode is BaseObjectState passiveNode)
            {
                NodeId typeId = passiveNode.TypeDefinitionId;
                if (IsNodeIdInNamespace(typeId) && typeId.TryGetIdentifier(out uint numericId))
                {
                    switch (numericId)
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
                                NodeState serverNode = await Server.NodeManager.FindNodeInAddressSpaceAsync(ObjectIds.Server).ConfigureAwait(false);
                                serverNode?.ReplaceChild(context, activeNode);
                            }
                            // remove the reference to server node because it is set as parent
                            activeNode.RemoveReference(
                                ReferenceTypeIds.HasComponent,
                                true,
                                ObjectIds.Server);

                            return activeNode;
                        }
                        case ObjectTypes.CertificateGroupFolderType:
                        {
                            var activeNode = new CertificateGroupFolderState(passiveNode.Parent);
                            activeNode.Create(context, passiveNode);

                            // delete unsupported groups
                            if (m_certificateGroups.All(group =>
                                    activeNode.DefaultHttpsGroup == null ||
                                    activeNode.DefaultHttpsGroup.BrowseName != group.BrowseName))
                            {
                                activeNode.DefaultHttpsGroup = null;
                            }
                            if (m_certificateGroups.All(group =>
                                    activeNode.DefaultUserTokenGroup == null ||
                                    activeNode.DefaultUserTokenGroup.BrowseName != group.BrowseName))
                            {
                                activeNode.DefaultUserTokenGroup = null;
                            }
                            if (m_certificateGroups.All(group =>
                                    activeNode.DefaultApplicationGroup == null ||
                                    activeNode.DefaultApplicationGroup.BrowseName != group.BrowseName))
                            {
                                activeNode.DefaultApplicationGroup = null;
                            }

                            // replace the node in the parent.
                            passiveNode.Parent?.ReplaceChild(context, activeNode);
                            return activeNode;
                        }
                        case ObjectTypes.CertificateGroupType:
                        {
                            ServerCertificateGroup result = m_certificateGroups
                                .FirstOrDefault(group =>
                                    group.NodeId == passiveNode.NodeId);

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
            return await base.AddBehaviourToPredefinedNodeAsync(context, predefinedNode, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                    is NamespacesState serverNamespacesNode)
                {
                    serverNamespacesNode.StateChanged -= ServerNamespacesChanged;
                }

                foreach (NodeState node in PredefinedNodes.Values)
                {
                    if (node is NamespaceMetadataState metadataState)
                    {
                        metadataState.StateChanged -= OnNamespaceChildrenChanged;
                        metadataState.DefaultRolePermissions?.StateChanged -= OnNamespaceDefaultPermissionsChanged;

                        metadataState.DefaultUserRolePermissions?.StateChanged -= OnNamespaceDefaultPermissionsChanged;
                    }
                }

                // m_serverConfigurationNode is owned by the address space, not by this manager
#pragma warning disable CA2213
                m_serverConfigurationNode = null;
#pragma warning restore CA2213
            }

            base.Dispose(disposing);
        }

        ///<inheritdoc/>
        public void CreateServerConfiguration(
            ServerSystemContext systemContext,
            ApplicationConfiguration configuration)
        {
            // setup server configuration node
            m_serverConfigurationNode.ServerCapabilities.Value =
            [
                .. configuration.ServerConfiguration.ServerCapabilities
            ];
            m_serverConfigurationNode.ServerCapabilities.ValueRank = ValueRanks.OneDimension;
            m_serverConfigurationNode.SupportedPrivateKeyFormats.Value =
            [
                .. configuration.ServerConfiguration.SupportedPrivateKeyFormats
            ];
            m_serverConfigurationNode.SupportedPrivateKeyFormats.ValueRank = ValueRanks
                .OneDimension;
            m_serverConfigurationNode.MaxTrustListSize.Value = (uint)configuration
                .ServerConfiguration
                .MaxTrustListSize;
            m_serverConfigurationNode.MulticastDnsEnabled.Value = configuration.ServerConfiguration
                .MultiCastDnsEnabled;

            m_serverConfigurationNode.UpdateCertificate.OnCallAsync
                = new UpdateCertificateMethodStateMethodAsyncCallHandler(
                UpdateCertificateAsync);
            m_serverConfigurationNode.CreateSigningRequest.OnCallAsync =
                new CreateSigningRequestMethodStateMethodAsyncCallHandler(CreateSigningRequestAsync);
            m_serverConfigurationNode.ApplyChanges.OnCallMethod2
                = new GenericMethodCalledEventHandler2(ApplyChanges);
            m_serverConfigurationNode.GetRejectedList.OnCall
                = new GetRejectedListMethodStateMethodCallHandler(
                GetRejectedList);
            m_serverConfigurationNode.GetCertificates.OnCall
                = new GetCertificatesMethodStateMethodCallHandler(
                GetCertificates);
            m_serverConfigurationNode.ClearChangeMasks(systemContext, true);

            // setup certificate group trust list handlers
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                certGroup.Node.CertificateTypes.Value = certGroup.CertificateTypes;
                certGroup.Node.TrustList.Handle = new TrustList(
                    certGroup.Node.TrustList,
                    certGroup.TrustedStore,
                    certGroup.IssuerStore,
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    Server.Telemetry,
                    m_configuration.ServerConfiguration.MaxTrustListSize);
                certGroup.Node.ClearChangeMasks(systemContext, true);
            }

            // find ServerNamespaces node and subscribe to StateChanged

            if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                is NamespacesState serverNamespacesNode)
            {
                serverNamespacesNode.StateChanged += ServerNamespacesChanged;

                IList<BaseInstanceState> children = [];
                serverNamespacesNode.GetChildren(systemContext, children);

                foreach (BaseInstanceState child in children)
                {
                    if (child is NamespaceMetadataState metadataState)
                    {
                        SubscribeToNamespaceDefaultPermissions(metadataState);
                    }
                }
            }
        }

        ///<inheritdoc/>
        public NamespaceMetadataState GetNamespaceMetadataState(string namespaceUri)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            lock (m_namespaceMetadataStatesLock)
            {
                if (m_namespaceMetadataStates.TryGetValue(
                    namespaceUri,
                    out NamespaceMetadataState value))
                {
                    return value;
                }
            }

            NamespaceMetadataState namespaceMetadataState = FindNamespaceMetadataState(
                namespaceUri);

            lock (m_namespaceMetadataStatesLock)
            {
                // remember the result for faster access.
                m_namespaceMetadataStates[namespaceUri] = namespaceMetadataState;
            }

            return namespaceMetadataState;
        }

        ///<inheritdoc/>
        public NamespaceMetadataState GetNamespaceMetadataState(ushort namespaceIndex)
        {
            lock (m_namespaceMetadataStatesLock)
            {
                if (m_namespaceMetadataStatesByIndex.TryGetValue(
                    namespaceIndex,
                    out NamespaceMetadataState value))
                {
                    return value;
                }
            }

            string namespaceUri = Server.NamespaceUris.GetString(namespaceIndex);
            NamespaceMetadataState namespaceMetadataState = GetNamespaceMetadataState(namespaceUri);

            lock (m_namespaceMetadataStatesLock)
            {
                m_namespaceMetadataStatesByIndex[namespaceIndex] = namespaceMetadataState;
            }

            return namespaceMetadataState;
        }

        /// <inheritdoc/>
        public async ValueTask<NamespaceMetadataState> CreateNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            NamespaceMetadataState namespaceMetadataState = FindNamespaceMetadataState(
                namespaceUri);

            if (namespaceMetadataState == null)
            {
                // find ServerNamespaces node
                if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                    is not NamespacesState serverNamespacesNode)
                {
                    m_logger.LogError(
                        "Cannot create NamespaceMetadataState for namespace '{NamespaceUri}'.",
                        namespaceUri);
                    return null;
                }

                // create the NamespaceMetadata node
                namespaceMetadataState = new NamespaceMetadataState(serverNamespacesNode)
                {
                    BrowseName = new QualifiedName(namespaceUri, NamespaceIndex)
                };
                namespaceMetadataState.Create(
                    SystemContext,
                    default,
                    namespaceMetadataState.BrowseName,
                    default,
                    true);
                namespaceMetadataState.DisplayName = LocalizedText.From(namespaceUri);
                namespaceMetadataState.SymbolicName = namespaceUri;
                namespaceMetadataState.NamespaceUri.Value = namespaceUri;
                namespaceMetadataState.AddDefaultRolePermissions(SystemContext);
                namespaceMetadataState.AddDefaultUserRolePermissions(SystemContext);

                // add node as child of ServerNamespaces and in predefined nodes
                serverNamespacesNode.AddChild(namespaceMetadataState);
                serverNamespacesNode.ClearChangeMasks(SystemContext, true);
                await AddPredefinedNodeAsync(SystemContext, namespaceMetadataState, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Subscribe to the default permission properties so that any future changes
            // trigger a DefaultPermissionsChanged notification to allow caches to be invalidated.
            SubscribeToNamespaceDefaultPermissions(namespaceMetadataState);

            return namespaceMetadataState;
        }

        /// <inheritdoc/>
        public void HasApplicationSecureAdminAccess(ISystemContext context)
        {
            HasApplicationSecureAdminAccess(context, null);
        }

        /// <inheritdoc/>
        public void HasApplicationSecureAdminAccess(
            ISystemContext context,
            CertificateStoreIdentifier trustedStore)
        {
            if (context is SessionSystemContext { OperationContext: OperationContext operationContext })
            {
                if (operationContext.ChannelContext?.EndpointDescription?.SecurityMode !=
                    MessageSecurityMode.SignAndEncrypt)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUserAccessDenied,
                        "Access to this item is only allowed with MessageSecurityMode SignAndEncrypt.");
                }
                IUserIdentity identity = operationContext.UserIdentity;
                // allow access to system configuration only with Role SecurityAdmin
                if (identity == null ||
                    identity.TokenType == UserTokenType.Anonymous ||
                    !identity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUserAccessDenied,
                        "Security Admin Role required to access this item.");
                }
            }
        }

        private async ValueTask<UpdateCertificateMethodStateResult> UpdateCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificate,
            ArrayOf<ByteString> issuerCertificates,
            string privateKeyFormat,
            ByteString privateKey,
            CancellationToken ct)
        {
            bool applyChangesRequired = false;
            HasApplicationSecureAdminAccess(context);

            ArrayOf<Variant> inputArguments =
            [
                certificateGroupId,
                certificateTypeId,
                certificate,
                issuerCertificates,
                privateKeyFormat,
                privateKey
            ];
            Certificate newCert = null;
            Certificate certWithPrivateKey = null;

            Server.ReportCertificateUpdateRequestedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);
            try
            {
                if (certificate.IsEmpty)
                {
                    throw new ArgumentNullException(nameof(certificate));
                }

                privateKeyFormat = privateKeyFormat?.ToUpperInvariant();
                if (privateKeyFormat is not null and not "PEM" and not "PFX" and not "")
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        $"The private key format {privateKeyFormat} is not supported.");
                }

                ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                    certificateGroupId,
                    certificateTypeId);
                certificateGroup.UpdateCertificate = null;

                try
                {
                    newCert = CertificateFactory.Create(certificate);
                }
                catch
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate data is invalid.");
                }

                // validate certificate type of new certificate
                if (!CertificateIdentifier.ValidateCertificateType(newCert, certificateTypeId))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate type of new certificate doesn't match the provided certificate type.");
                }

                // identify the existing certificate to be updated
                // it should be of the same type and same subject name as the new certificate
                CertificateIdentifier existingCertIdentifier =
                    (
                        certificateGroup.ApplicationCertificates.ToList().FirstOrDefault(cert =>
                            X509Utils.CompareDistinguishedName(cert.SubjectName, newCert.Subject) &&
                            cert.CertificateType == certificateTypeId)
                        ?? certificateGroup.ApplicationCertificates.ToList().FirstOrDefault(cert =>
                            cert.Certificate != null &&
                            X509Utils.GetApplicationUrisFromCertificate(cert.Certificate)
                                .Any(uri => uri.Equals(m_configuration.ApplicationUri, StringComparison.Ordinal)) &&
                            cert.CertificateType == certificateTypeId))
                    ?? throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "No existing certificate found for the specified certificate type and subject name.");

                var newIssuerCollection = new CertificateCollection();

                try
                {
                    // build issuer chain
                    foreach (ByteString issuerRawCert in issuerCertificates)
                    {
                        newIssuerCollection.Add(CertificateFactory.Create(issuerRawCert));
                    }
                }
                catch
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Issuer certificate data is invalid.");
                }

                // self signed
                bool selfSigned = X509Utils.IsSelfSigned(newCert);
                if (selfSigned && newIssuerCollection.Count != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Issuer list not empty for self signed certificate.");
                }

                if (!selfSigned)
                {
                    try
                    {
                        // verify cert with issuer chain
                        var certValidator = new CertificateValidator(Server.Telemetry);
                        var issuerStore = new CertificateTrustList();
                        var issuerList = new List<CertificateIdentifier>();
                        foreach (Certificate issuerCert in newIssuerCollection)
                        {
                            issuerList.Add(new CertificateIdentifier(issuerCert));
                        }
                        issuerStore.TrustedCertificates = issuerList.ToArrayOf();
                        certValidator.Update(issuerStore, issuerStore, null);
                        await certValidator.ValidateAsync(newCert, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(
                            Utils.TraceMasks.Security,
                            ex,
                            "Failed to verify integrity of the new certificate {Certificate} and the issuer list.",
                            newCert.X509.AsLogSafeString());
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "Failed to verify integrity of the new certificate and the issuer list.",
                            ex);
                    }
                }

                var updateCertificate = new UpdateCertificateData
                {
                    IssuerCollection = newIssuerCollection,
                    SessionId = (context as ISessionSystemContext)?.SessionId ?? default
                };
                try
                {
                    ICertificatePasswordProvider passwordProvider = m_configuration
                        .SecurityConfiguration
                        .CertificatePasswordProvider;
                    switch (privateKeyFormat)
                    {
                        case null:
                        case "":
                            for (int attempt = 0; ; attempt++)
                            {
                                Certificate exportableKey;
                                // use the new generated private key if one exists and matches the provided public key
                                if (certificateGroup.TemporaryApplicationCertificate != null &&
                                    X509Utils.VerifyKeyPair(
                                        newCert,
                                        certificateGroup.TemporaryApplicationCertificate))
                                {
                                    exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                        certificateGroup.TemporaryApplicationCertificate,
                                        false);
                                }
                                else
                                {
                                    certWithPrivateKey = await existingCertIdentifier
                                        .LoadPrivateKeyExAsync(
                                            passwordProvider,
                                            m_configuration.ApplicationUri,
                                            Server.Telemetry,
                                            ct)
                                        .ConfigureAwait(false);
                                    if (certWithPrivateKey == null)
                                    {
                                        throw new ServiceResultException(
                                            StatusCodes.BadSecurityChecksFailed,
                                            "A private key was not found");
                                    }
                                    exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                        certWithPrivateKey,
                                        false);
                                }

                                updateCertificate.CertificateWithPrivateKey =
                                    CertificateFactory.CreateCertificateWithPrivateKey(
                                        newCert,
                                        exportableKey);
                                try
                                {
                                    await UpdateCertificateInternalAsync(
                                        certificateGroup,
                                        existingCertIdentifier,
                                        updateCertificate, ct).ConfigureAwait(false);
                                    break;
                                }
                                catch (Exception ex) when (ShouldRetry(attempt, ex))
                                {
                                    m_logger.LogDebug(
                                        Utils.TraceMasks.Security,
                                        ex,
                                        "Failed to update certificate {Certificate}. Retrying...",
                                        newCert.X509.AsLogSafeString());
                                }
                            }
                            break;
                        case "PFX":
                            for (int attempt = 0; ; attempt++)
                            {
                                certWithPrivateKey = X509Utils.CreateCertificateFromPKCS12(
                                    privateKey.ToArray(),
                                    passwordProvider?.GetPassword(existingCertIdentifier),
#if !NET9_0_OR_GREATER
                                    // https://github.com/OPCFoundation/UA-.NETStandard/commit/0b24d62b7c2bab2e5ed08e694103d49278e457af
                                    // CopyWithPrivateKey apparently does not support ephimeralkeysets on windows
                                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
#else // But it seems to work on .net 9 - and we prefer that over files
                                    false);
#endif
                                updateCertificate.CertificateWithPrivateKey =
                                    CertificateFactory.CreateCertificateWithPrivateKey(
                                        newCert,
                                        certWithPrivateKey);
                                try
                                {
                                    await UpdateCertificateInternalAsync(
                                        certificateGroup,
                                        existingCertIdentifier,
                                        updateCertificate, ct).ConfigureAwait(false);
                                    break;
                                }
                                catch (Exception ex) when (ShouldRetry(attempt, ex))
                                {
                                    m_logger.LogDebug(
                                        Utils.TraceMasks.Security,
                                        ex,
                                        "Failed to update certificate {Certificate} with PFX private key. Retrying...",
                                        newCert.X509.AsLogSafeString());
                                }
                            }
                            break;
                        case "PEM":
                            for (int attempt = 0; ; attempt++)
                            {
                                updateCertificate.CertificateWithPrivateKey =
                                    CertificateFactory.CreateCertificateWithPEMPrivateKey(
                                        newCert,
                                        privateKey.ToArray(),
                                        passwordProvider?.GetPassword(existingCertIdentifier));
                                try
                                {
                                    await UpdateCertificateInternalAsync(
                                        certificateGroup,
                                        existingCertIdentifier,
                                        updateCertificate, ct).ConfigureAwait(false);
                                    break;
                                }
                                catch (Exception ex) when (ShouldRetry(attempt, ex))
                                {
                                    m_logger.LogDebug(
                                        Utils.TraceMasks.Security,
                                        ex,
                                        "Failed to update certificate {Certificate} with PEM private key. Retrying...",
                                        newCert.X509.AsLogSafeString());
                                }
                            }
                            break;
                    }
                }
                catch (Exception ex) when (ex is not ServiceResultException)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "Failed to verify integrity of the new certificate and the private key.", ex);
                }
                finally
                {
                    // dispose temporary new private key as it is no longer needed
                    certificateGroup.TemporaryApplicationCertificate?.Dispose();
                    certificateGroup.TemporaryApplicationCertificate = null;
                }

                certificateGroup.UpdateCertificate = updateCertificate;
                applyChangesRequired = true;
            }
            catch (Exception e)
            {
                // report the failure of UpdateCertificate via an audit event
                Server.ReportCertificateUpdatedAuditEvent(
                    context,
                    objectId,
                    method,
                    inputArguments,
                    certificateGroupId,
                    certificateTypeId,
                    m_logger,
                    e);
                // Raise audit certificate event
                Server.ReportAuditCertificateEvent(newCert, e, m_logger);
                throw;
            }

            return new UpdateCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ApplyChangesRequired = applyChangesRequired
            };

            static bool ShouldRetry(int attempt, Exception ex)
            {
                if (ex is ServiceResultException sre && sre.StatusCode == StatusCodes.BadConfigurationError)
                {
                    return false;
                }
                const int maxAttempts = 3;
                return attempt < maxAttempts;
            }

            // Handle the store update
            async Task UpdateCertificateInternalAsync(
                ServerCertificateGroup certificateGroup,
                CertificateIdentifier existingCertIdentifier,
                UpdateCertificateData updateCertificate,
                CancellationToken ct)
            {
                try
                {
                    using (ICertificateStore appStore = existingCertIdentifier.OpenStore(Server.Telemetry))
                    {
                        if (appStore == null)
                        {
                            throw ServiceResultException.ConfigurationError(
                                "Failed to open application certificate store.");
                        }

                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "Delete application certificate {Certificate}",
                            existingCertIdentifier.Certificate.X509.AsLogSafeString());
                        await appStore.DeleteAsync(
                            existingCertIdentifier.Thumbprint,
                            ct)
                            .ConfigureAwait(false);
                        ICertificatePasswordProvider passwordProvider = m_configuration
                            .SecurityConfiguration
                            .CertificatePasswordProvider;
                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "Add new application certificate {Certificate}",
                            updateCertificate.CertificateWithPrivateKey.X509.AsLogSafeString());
                        Debug.Assert(updateCertificate.CertificateWithPrivateKey.HasPrivateKey);
                        await appStore.AddAsync(
                            updateCertificate.CertificateWithPrivateKey,
                            passwordProvider?.GetPassword(existingCertIdentifier),
                            ct)
                            .ConfigureAwait(false);
                        // keep only track of cert without private key
                        Certificate certOnly = CertificateFactory.Create(
                            updateCertificate.CertificateWithPrivateKey.RawData);
                        updateCertificate.CertificateWithPrivateKey.Dispose();
                        updateCertificate.CertificateWithPrivateKey = certOnly;
                        // update certificate identifier with new certificate
                        await existingCertIdentifier.FindAsync(
                            m_configuration.ApplicationUri,
                            Server.Telemetry,
                            ct)
                            .ConfigureAwait(false);
                    }

                    ICertificateStore issuerStore = certificateGroup.IssuerStore.OpenStore(Server.Telemetry);
                    try
                    {
                        if (issuerStore == null)
                        {
                            throw ServiceResultException.ConfigurationError(
                                "Failed to open issuer certificate store.");
                        }

                        foreach (Certificate issuer in updateCertificate.IssuerCollection)
                        {
                            try
                            {
                                m_logger.LogInformation(
                                    Utils.TraceMasks.Security,
                                    "Add new issuer certificate {Certificate}",
                                    issuer.X509.AsLogSafeString());
                                await issuerStore.AddAsync(issuer, ct: ct).ConfigureAwait(false);
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

                    Server.ReportCertificateUpdatedAuditEvent(
                        context,
                        objectId,
                        method,
                        inputArguments,
                        certificateGroupId,
                        certificateTypeId,
                        m_logger);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        Utils.TraceMasks.Security,
                        ex,
                        "Failed to update certificate {Certificate}.",
                        newCert.X509.AsLogSafeString());
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "Failed to update certificate.",
                        ex);
                }
            }
        }

        private async ValueTask<CreateSigningRequestMethodStateResult> CreateSigningRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            ByteString nonce,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            // identify the existing certificate for which to CreateSigningRequest
            // it should be of the same type
            CertificateIdentifier existingCertIdentifier = certificateGroup.ApplicationCertificates
                .ToList().FirstOrDefault(
                    cert => cert.CertificateType == certificateTypeId);

            if (string.IsNullOrEmpty(subjectName))
            {
                subjectName = existingCertIdentifier.Certificate.Subject;
            }

            certificateGroup.TemporaryApplicationCertificate?.Dispose();
            certificateGroup.TemporaryApplicationCertificate = null;

            Certificate certWithPrivateKey;
            if (regeneratePrivateKey)
            {
                ArrayOf<string> domainNames = X509Utils.GetDomainsFromCertificate(existingCertIdentifier.Certificate);

                certWithPrivateKey = GenerateTemporaryApplicationCertificate(
                    certificateTypeId,
                    certificateGroup,
                    subjectName,
                    domainNames);
            }
            else
            {
                ICertificatePasswordProvider passwordProvider = m_configuration
                    .SecurityConfiguration
                    .CertificatePasswordProvider;
                certWithPrivateKey = await existingCertIdentifier
                    .LoadPrivateKeyExAsync(passwordProvider,
                                           m_configuration.ApplicationUri,
                                           Server.Telemetry,
                                           cancellationToken)
                    .ConfigureAwait(false);

                if (certWithPrivateKey == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadInternalError, "Failed to load private key");
                }
            }

            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "Create signing request {Certificate}",
                certWithPrivateKey.X509.AsLogSafeString());
#pragma warning disable CS0618 // Type or member is obsolete - TODO: migrate to ICertificateFactory
            ByteString certificateRequest = ByteString.From(CertificateFactory.CreateSigningRequest(
                certWithPrivateKey,
                X509Utils.GetDomainsFromCertificate(certWithPrivateKey)));
#pragma warning restore CS0618

            return new CreateSigningRequestMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                CertificateRequest = certificateRequest
            };
        }

        private Certificate GenerateTemporaryApplicationCertificate(
            NodeId certificateTypeId,
            ServerCertificateGroup certificateGroup,
            string subjectName,
            ArrayOf<string> domainNames)
        {
            Certificate certificate;

#pragma warning disable CS0618 // Type or member is obsolete - TODO: migrate to ICertificateFactory
            ICertificateBuilder certificateBuilder = CertificateFactory
                .CreateCertificate(m_configuration.ApplicationUri, m_configuration.ApplicationName, subjectName, domainNames)
#pragma warning restore CS0618
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddDays(14));

            if (certificateTypeId.IsNull ||
                certificateTypeId == ObjectTypeIds.ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                certificate = certificateBuilder.SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();
            }
            else
            {
                ECCurve? curve =
                    CryptoUtils.GetCurveFromCertificateTypeId(certificateTypeId)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The Ecc certificate type is not supported.");
                certificate = certificateBuilder.SetECCurve(curve.Value).CreateForECDsa();
            }

            certificateGroup.TemporaryApplicationCertificate = certificate;

            return certificate;
        }

        private ServiceResult ApplyChanges(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
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
                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "Apply Changes for certificate {Certificate}",
                            updateCertificate.CertificateWithPrivateKey.X509.AsLogSafeString());
                    }
                }
                finally
                {
                    certificateGroup.UpdateCertificate = null;
                }
            }

            if (disconnectSessions)
            {
                // When a Server Certificate or TrustList changes active SecureChannels
                // are not immediately affected. This ensures the caller of ApplyChanges
                // can get a response to the Method call. Once the Method response is
                // returned the Server shall force existing SecureChannels affected by
                // the changes to renegotiate and use the new Server Certificate
                // and/or TrustLists.

                // TODO: This needs fixing, the 1 second might or might not work to give
                // Time to the client to receive the response.  Also, this needs to cut
                // all channels and reevaluate sessions, this needs to be implemented in
                // Transport side presumably.

                _ = Task.Run(async () =>
                {
                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "----- Apply Changes of application certificate starts in 1 second...");

                    // give the client some time to receive the response
                    // before the certificate update may disconnect all sessions
                    await Task.Delay(1000).ConfigureAwait(false);

                    try
                    {
                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "----- Apply Changes for application certificate update running...");

                        await m_configuration
                            .CertificateValidator.UpdateCertificateAsync(
                                m_configuration.SecurityConfiguration,
                                m_configuration.ApplicationUri)
                            .ConfigureAwait(false);

                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "----- Apply Changes for application certificate update completed.");
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogCritical(
                            ex,
                            "----- Apply Changes for application certificate update failed: " +
                            "Error updating application instance certificates. " +
                            "Server could be in faulted state.");

                        // Throws to nowhere since no one is listening ... // throw;
                    }
                });
            }

            return StatusCodes.Good;
        }

        private ServiceResult GetRejectedList(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref ArrayOf<ByteString> certificates)
        {
            HasApplicationSecureAdminAccess(context);

            // No rejected store configured
            if (m_rejectedStore == null)
            {
                certificates = [];
                return StatusCodes.Good;
            }

            ICertificateStore store = m_rejectedStore.OpenStore(Server.Telemetry);
            try
            {
                if (store != null)
                {
                    CertificateCollection collection = store.EnumerateAsync().Result;
                    var rawList = new List<ByteString>();
                    foreach (Certificate cert in collection)
                    {
                        rawList.Add(cert.RawData.ToByteString());
                    }
                    certificates = rawList.ToArrayOf();
                }
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
            ref ArrayOf<NodeId> certificateTypeIds,
            ref ArrayOf<ByteString> certificates)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup =
                m_certificateGroups.FirstOrDefault(
                    group => Utils.IsEqual(group.NodeId, certificateGroupId))
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate group invalid.");

            certificateTypeIds = certificateGroup.CertificateTypes;
            certificates = certificateGroup.ApplicationCertificates
                .ToList().Select(s => s.Certificate?.RawData.ToByteString() ?? default)
                .ToArrayOf();

            return ServiceResult.Good;
        }

        private ServerCertificateGroup VerifyGroupAndTypeId(
            NodeId certificateGroupId,
            NodeId certificateTypeId)
        {
            // verify typeid must be set
            if (certificateTypeId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate type not specified.");
            }

            // verify requested certificate group
            if (certificateGroupId.IsNull)
            {
                certificateGroupId = ObjectIds
                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
            }

            ServerCertificateGroup certificateGroup =
                m_certificateGroups.FirstOrDefault(
                    group => Utils.IsEqual(group.NodeId, certificateGroupId))
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate group invalid.");

            // verify certificate type
            bool foundCertType = certificateGroup.CertificateTypes
                .Any(t => Utils.IsEqual(t, certificateTypeId));
            if (!foundCertType)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate type not valid for certificate group.");
            }

            return certificateGroup;
        }

        /// <summary>
        /// Finds the <see cref="NamespaceMetadataState"/> node for the specified NamespaceUri.
        /// </summary>
        private NamespaceMetadataState FindNamespaceMetadataState(string namespaceUri)
        {
            try
            {
                // find ServerNamespaces node
                if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                    is not NamespacesState serverNamespacesNode)
                {
                    m_logger.LogError("Cannot find ObjectIds.Server_Namespaces node.");
                    return null;
                }

                IList<BaseInstanceState> serverNamespacesChildren = [];
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
                }

                IList<IReference> serverNamespacesReferencs = [];
                serverNamespacesNode.GetReferences(SystemContext, serverNamespacesReferencs);

                foreach (IReference serverNamespacesReference in serverNamespacesReferencs)
                {
                    if (!serverNamespacesReference.IsInverse)
                    {
                        // Find NamespaceMetadata node of NamespaceUri in Namespaces references
                        var nameSpaceNodeId = ExpandedNodeId.ToNodeId(
                            serverNamespacesReference.TargetId,
                            Server.NamespaceUris);
                        if (Server.NodeManager.FindNodeInAddressSpaceAsync(
                            nameSpaceNodeId).AsTask().GetAwaiter().GetResult() is not NamespaceMetadataState namespaceMetadata)
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
                m_logger.LogError(
                    ex,
                    "Error searching NamespaceMetadata for namespaceUri {NamespaceUri}.",
                    namespaceUri);
                return null;
            }
        }

        /// <summary>
        /// Clear NamespaceMetadata nodes cache in case nodes are added or deleted
        /// </summary>
        private void ServerNamespacesChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Children) != 0 ||
                (changes & NodeStateChangeMasks.References) != 0)
            {
                try
                {
                    lock (m_namespaceMetadataStatesLock)
                    {
                        m_namespaceMetadataStates.Clear();
                        m_namespaceMetadataStatesByIndex.Clear();
                    }

                    if (node is NamespacesState serverNamespacesNode)
                    {
                        IList<BaseInstanceState> children = [];
                        serverNamespacesNode.GetChildren(context, children);

                        foreach (BaseInstanceState child in children)
                        {
                            if (child is NamespaceMetadataState metadataState)
                            {
                                SubscribeToNamespaceDefaultPermissions(metadataState);
                            }
                        }
                    }
                }
                catch
                {
                    // ignore errors
                }
            }
        }

        /// <summary>
        /// Subscribes to the <c>StateChanged</c> events of the <c>DefaultRolePermissions</c>
        /// and <c>DefaultUserRolePermissions</c> child nodes of a <see cref="NamespaceMetadataState"/>
        /// to detect changes that require permission cache invalidation.
        /// </summary>
        private void SubscribeToNamespaceDefaultPermissions(NamespaceMetadataState namespaceMetadataState)
        {
            if (namespaceMetadataState.DefaultRolePermissions != null)
            {
                // unsubscribe first to avoid duplicate subscriptions if called multiple times
                namespaceMetadataState.DefaultRolePermissions.StateChanged -= OnNamespaceDefaultPermissionsChanged;
                namespaceMetadataState.DefaultRolePermissions.StateChanged += OnNamespaceDefaultPermissionsChanged;
            }

            if (namespaceMetadataState.DefaultUserRolePermissions != null)
            {
                namespaceMetadataState.DefaultUserRolePermissions.StateChanged -= OnNamespaceDefaultPermissionsChanged;
                namespaceMetadataState.DefaultUserRolePermissions.StateChanged += OnNamespaceDefaultPermissionsChanged;
            }

            namespaceMetadataState.StateChanged -= OnNamespaceChildrenChanged;
            namespaceMetadataState.StateChanged += OnNamespaceChildrenChanged;
        }

        /// <summary>
        /// Handles children change on NamespaceMetadataState and resubscribes to the default permissions nodes
        /// to ensure we are notified of changes on those nodes even if they are recreated.
        /// </summary>
        private void OnNamespaceChildrenChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Children) != 0 &&
                node is NamespaceMetadataState namespaceMetadataState)
            {
                SubscribeToNamespaceDefaultPermissions(namespaceMetadataState);
            }
        }

        /// <summary>
        /// Handles value changes on <c>DefaultRolePermissions</c> or <c>DefaultUserRolePermissions</c>
        /// and raises the <see cref="DefaultPermissionsChanged"/> event.
        /// </summary>
        private void OnNamespaceDefaultPermissionsChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Value) != 0)
            {
                DefaultPermissionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc/>
        public event EventHandler DefaultPermissionsChanged;

        private class UpdateCertificateData
        {
            public NodeId SessionId { get; set; }
            public Certificate CertificateWithPrivateKey { get; set; }
            public CertificateCollection IssuerCollection { get; set; }
        }

        private class ServerCertificateGroup
        {
            public string BrowseName { get; set; }
            public NodeId NodeId { get; set; }
            public CertificateGroupState Node { get; set; }
            public NodeId[] CertificateTypes { get; set; }
            public ArrayOf<CertificateIdentifier> ApplicationCertificates { get; set; }
            public CertificateStoreIdentifier IssuerStore { get; set; }
            public CertificateStoreIdentifier TrustedStore { get; set; }
            public UpdateCertificateData UpdateCertificate { get; set; }
            public Certificate TemporaryApplicationCertificate { get; set; }
        }

        private ServerConfigurationState m_serverConfigurationNode;
        private readonly ApplicationConfiguration m_configuration;
        private readonly List<ServerCertificateGroup> m_certificateGroups;
        private readonly CertificateStoreIdentifier m_rejectedStore;
        private readonly Dictionary<string, NamespaceMetadataState> m_namespaceMetadataStates = [];
        private readonly Dictionary<ushort, NamespaceMetadataState> m_namespaceMetadataStatesByIndex = [];
        private readonly Lock m_namespaceMetadataStatesLock = new();
    }
}
