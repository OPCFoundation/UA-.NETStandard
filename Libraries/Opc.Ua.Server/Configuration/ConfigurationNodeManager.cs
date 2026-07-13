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
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
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
            : this(server, configuration, server.Telemetry.CreateLogger<ConfigurationNodeManager>(), timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the configuration and diagnostics manager.
        /// </summary>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger)
            : this(server, configuration, logger, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the configuration and diagnostics manager with an
        /// explicit <see cref="TimeProvider"/>.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used by the certificate-alarm
        /// timer and by the "apply changes" delay. When <c>null</c>, the time
        /// provider exposed by the server (via <see cref="ITimeProviderProvider"/>)
        /// is used, falling back to <see cref="TimeProvider.System"/>.
        /// </param>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            TimeProvider? timeProvider)
            : base(server, configuration, logger, timeProvider)
        {
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            string? rejectedStorePath = configuration.SecurityConfiguration.RejectedCertificateStore?
                .StorePath;
            if (!string.IsNullOrEmpty(rejectedStorePath))
            {
                m_rejectedStore = new CertificateStoreIdentifier(rejectedStorePath!);
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
                    configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath!
                ),
                TrustedStore = new CertificateStoreIdentifier(
                    configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath!)
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
                        configuration.SecurityConfiguration.UserIssuerCertificates.StorePath!
                    ),
                    TrustedStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedUserCertificates.StorePath!)
                };

                m_certificateGroups.Add(defaultUserGroup);
            }
            ServerCertificateGroup? defaultHttpsGroup = null;
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
                        configuration.SecurityConfiguration.HttpsIssuerCertificates.StorePath!
                    ),
                    TrustedStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedHttpsCertificates.StorePath!)
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
                if (IsNodeIdInNamespace(typeId) && typeId.TryGetValue(out uint numericId))
                {
                    switch (numericId)
                    {
                        case ObjectTypes.ServerConfigurationType:
                        {
                            var activeNode = (ServerConfigurationState)passiveNode;

                            activeNode
                                .AddGetCertificates(context)
                                .AddCreateSelfSignedCertificate(context);

                            m_serverConfigurationNode = activeNode;

                            return activeNode;
                        }
                        case ObjectTypes.CertificateGroupFolderType:
                        {
                            // The standard nodeset contains CertificateGroupFolderType
                            // instances under several types (e.g. ServerConfigurationType,
                            // ApplicationConfigurationType, ProvisionableDeviceType). Only
                            // the Server's own ServerConfiguration certificate groups folder
                            // is managed here; the others must keep their loaded structure.
                            if (passiveNode.NodeId != ObjectIds.ServerConfiguration_CertificateGroups)
                            {
                                break;
                            }

                            var activeNode = (CertificateGroupFolderState)passiveNode;

                            ServerCertificateGroup? applicationGroup =
                                m_certificateGroups.FirstOrDefault(m => m.BrowseName == BrowseNames.DefaultApplicationGroup);

                            applicationGroup!.Node = activeNode.DefaultApplicationGroup!;

                            ServerCertificateGroup? httpsGroup =
                                m_certificateGroups.FirstOrDefault(m => m.BrowseName == BrowseNames.DefaultHttpsGroup);
                            if (httpsGroup != null)
                            {
                                activeNode.AddDefaultHttpsGroup(context);
                                httpsGroup.Node = activeNode.DefaultHttpsGroup!;
                            }

                            ServerCertificateGroup? userTokenGroup =
                                m_certificateGroups.FirstOrDefault(m => m.BrowseName == BrowseNames.DefaultUserTokenGroup);
                            if (userTokenGroup != null)
                            {
                                activeNode.AddDefaultUserTokenGroup(context);
                                userTokenGroup.Node = activeNode.DefaultUserTokenGroup!;
                            }

                            return activeNode;
                        }
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
                m_serverConfigurationNode = null;
                m_userManagementBinding?.Dispose();
                m_userManagementBinding = null;

                foreach (ServerCertificateGroup certificateGroup in m_certificateGroups)
                {
                    DisposePendingRotationState(certificateGroup);
                }

                StopAlarmMonitoring();
            }

            base.Dispose(disposing);
        }

        ///<inheritdoc/>
        public void CreateServerConfiguration(
            ServerSystemContext systemContext,
            ApplicationConfiguration configuration)
        {
            // setup server configuration node
            ServerConfigurationState configNode = m_serverConfigurationNode!;
            configNode.ServerCapabilities!.Value =
            [
                .. configuration.ServerConfiguration!.ServerCapabilities
            ];
            configNode.ServerCapabilities.ValueRank = ValueRanks.OneDimension;
            configNode.SupportedPrivateKeyFormats!.Value =
            [
                .. configuration.ServerConfiguration.SupportedPrivateKeyFormats
            ];
            configNode.SupportedPrivateKeyFormats.ValueRank = ValueRanks
                .OneDimension;
            configNode.MaxTrustListSize!.Value = (uint)configuration
                .ServerConfiguration
                .MaxTrustListSize;
            configNode.MulticastDnsEnabled!.Value = configuration.ServerConfiguration
                .MultiCastDnsEnabled;

            configNode.UpdateCertificate!.OnCallAsync
                = new UpdateCertificateMethodStateMethodAsyncCallHandler(
                UpdateCertificateAsync);
            configNode.CreateSigningRequest!.OnCallAsync =
                new CreateSigningRequestMethodStateMethodAsyncCallHandler(CreateSigningRequestAsync);
            configNode.CreateSelfSignedCertificate?.OnCallAsync =
                    new CreateSelfSignedCertificateMethodStateMethodAsyncCallHandler(
                        CreateSelfSignedCertificateAsync);
            configNode.ApplyChanges!.OnCallMethod2
                = new GenericMethodCalledEventHandler2(ApplyChanges);
            configNode.GetRejectedList!.OnCall
                = new GetRejectedListMethodStateMethodCallHandler(
                GetRejectedList);
            configNode.GetCertificates!.OnCall
                = new GetCertificatesMethodStateMethodCallHandler(
                GetCertificates);
            configNode.ClearChangeMasks(systemContext, true);

            // setup certificate group trust list handlers
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                certGroup.Node!.CertificateTypes!.Value = certGroup.CertificateTypes;
                certGroup.Node!.TrustList!.Handle = new TrustList(
                    certGroup.Node.TrustList,
                    certGroup.TrustedStore,
                    certGroup.IssuerStore,
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    Server.Telemetry,
                    m_configuration.ServerConfiguration!.MaxTrustListSize);
                certGroup.Node.ClearChangeMasks(systemContext, true);
            }

            // OPC 10000-12 §7.8.3: populate the optional alarm property
            // values (ExpirationDate, TrustListId, LastUpdateTime) from
            // the current certificate and CRL state. Active-state
            // transitions (SetActiveState) are not performed during
            // CreateAddressSpace to avoid event-notification issues before
            // the subscription infrastructure is ready.
            EvaluateCertificateAlarms(systemContext);

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

            // Bind ServerConfiguration.UserManagement (i=24290) per Part 18 §5
            // if an IUserManagement was injected via IServerInternal.SetUserManagement.
            if (Server is IServerInternal serverInternal && serverInternal.UserManagement != null)
            {
                m_userManagementBinding?.Dispose();
                m_userManagementBinding = UserManagement.UserManagementBinding.Bind(
                    this,
                    serverInternal.UserManagement,
                    serverInternal.SessionManager);
            }
            else
            {
                m_userManagementBinding?.Dispose();
                m_userManagementBinding = null;
                DeleteNodeAsync(systemContext, new NodeId(Objects.UserManagement))
                    .AsTask().GetAwaiter().GetResult();
            }
        }

        ///<inheritdoc/>
        public async ValueTask<NamespaceMetadataState?> GetNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            lock (m_namespaceMetadataStatesLock)
            {
                if (m_namespaceMetadataStates.TryGetValue(
                    namespaceUri,
                    out NamespaceMetadataState? value))
                {
                    return value;
                }
            }

            NamespaceMetadataState? namespaceMetadataState = await FindNamespaceMetadataStateAsync(
                namespaceUri, cancellationToken).ConfigureAwait(false);

            lock (m_namespaceMetadataStatesLock)
            {
                // remember the result for faster access.
                m_namespaceMetadataStates[namespaceUri] = namespaceMetadataState!;
            }

            return namespaceMetadataState;
        }

        ///<inheritdoc/>
        public async ValueTask<NamespaceMetadataState?> GetNamespaceMetadataStateAsync(ushort namespaceIndex, CancellationToken cancellationToken = default)
        {
            lock (m_namespaceMetadataStatesLock)
            {
                if (m_namespaceMetadataStatesByIndex.TryGetValue(
                    namespaceIndex,
                    out NamespaceMetadataState? value))
                {
                    return value;
                }
            }

            string? namespaceUri = Server.NamespaceUris.GetString(namespaceIndex);
            NamespaceMetadataState? namespaceMetadataState = await GetNamespaceMetadataStateAsync(namespaceUri!, cancellationToken).ConfigureAwait(false);

            lock (m_namespaceMetadataStatesLock)
            {
                m_namespaceMetadataStatesByIndex[namespaceIndex] = namespaceMetadataState!;
            }

            return namespaceMetadataState!;
        }

        /// <inheritdoc/>
        public async ValueTask<NamespaceMetadataState> CreateNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            NamespaceMetadataState? namespaceMetadataState = await FindNamespaceMetadataStateAsync(
                namespaceUri, cancellationToken).ConfigureAwait(false);

            if (namespaceMetadataState == null)
            {
                // find ServerNamespaces node
                if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                    is not NamespacesState serverNamespacesNode)
                {
                    m_logger.CreateNamespaceMetadataStateForNamespaceNamespaceUri(namespaceUri);
                    return null!;
                }

                // create the NamespaceMetadata node
                namespaceMetadataState = SystemContext.CreateInstanceOfNamespaceMetadataType(
                    serverNamespacesNode,
                    new QualifiedName(namespaceUri, NamespaceIndex));
                namespaceMetadataState.NodeId = SystemContext.NodeIdFactory.New(SystemContext, namespaceMetadataState);
                namespaceMetadataState.DisplayName = LocalizedText.From(namespaceUri);
                namespaceMetadataState.SymbolicName = namespaceUri;
                namespaceMetadataState!.NamespaceUri!.Value = namespaceUri;
                namespaceMetadataState.AddDefaultRolePermissions(SystemContext)
                    .AddDefaultUserRolePermissions(SystemContext);

                // add node as child of ServerNamespaces and in predefined nodes
                serverNamespacesNode.AddChild(namespaceMetadataState);
                await serverNamespacesNode.ClearChangeMasksAsync(SystemContext, true, cancellationToken)
                    .ConfigureAwait(false);
                await AddPredefinedNodeAsync(SystemContext, namespaceMetadataState, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Subscribe to the default permission properties so that any future changes
            // trigger a DefaultPermissionsChanged notification to allow caches to be invalidated.
            SubscribeToNamespaceDefaultPermissions(namespaceMetadataState);

            return namespaceMetadataState;
        }

        /// <inheritdoc/>
        public async ValueTask BindKeyCredentialPushAsync(
            KeyCredentialPushSubject subject,
            CancellationToken cancellationToken = default)
        {
            if (subject == null)
            {
                throw new ArgumentNullException(nameof(subject));
            }

            NodeState? node = FindPredefinedNode<NodeState>(
                KeyCredentialPushSubject.StandardConfigurationFolderNodeId) ??
                await Server.NodeManager
                    .FindNodeInAddressSpaceAsync(KeyCredentialPushSubject.StandardConfigurationFolderNodeId, cancellationToken)
                    .ConfigureAwait(false);

            if (node is not KeyCredentialConfigurationFolderState folder)
            {
                if (node is not BaseObjectState passiveNode)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdUnknown,
                        "The standard KeyCredentialConfiguration folder is not present.");
                }

                folder = new KeyCredentialConfigurationFolderState(passiveNode.Parent);
                folder.Create(SystemContext, passiveNode);
                passiveNode.Parent?.ReplaceChild(SystemContext, folder);
                await AddPredefinedNodeAsync(SystemContext, folder, cancellationToken)
                    .ConfigureAwait(false);
            }

            await subject.BindAsync(
                    folder,
                    SystemContext,
                    (state, ct) => AddPredefinedNodeAsync(SystemContext, state, ct),
                    async (state, ct) => await DeleteNodeAsync(SystemContext, state.NodeId, ct).ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void HasApplicationSecureAdminAccess(ISystemContext context)
        {
            HasApplicationSecureAdminAccess(context, null!);
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
            string? privateKeyFormat,
            ByteString privateKey,
            CancellationToken ct)
        {
            bool applyChangesRequired = false;
            HasApplicationSecureAdminAccess(context);

            // OPC 10000-12 §7.10.3: the private key is sensitive material;
            // it must not be persisted into the
            // CertificateUpdateRequested / CertificateUpdated audit events.
            // The audit payload still reflects the public-key certificate,
            // issuer chain and key format so administrators can correlate
            // the request without exposing the secret.
            ArrayOf<Variant> inputArguments =
            [
                certificateGroupId,
                certificateTypeId,
                certificate,
                issuerCertificates,
                privateKeyFormat!,
                AuditEvents.RedactedPrivateKey
            ];

            Server.ReportCertificateUpdateRequestedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);
            Certificate? newCert = null;
            CertificateCollection? newIssuerCollection = null;
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
                    certificateTypeId)!;
                ResetPendingUpdateCertificate(certificateGroup);

                try
                {
                    newCert = Certificate.FromRawData(certificate);
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
                CertificateIdentifier existingCertIdentifier;
                CertificateIdentifier? subjectMatch = certificateGroup.ApplicationCertificates
                    .ToList()
                    .FirstOrDefault(cert =>
                        X509Utils.CompareDistinguishedName(cert.SubjectName!, newCert.Subject) &&
                        cert.CertificateType == certificateTypeId);

                if (subjectMatch != null)
                {
                    existingCertIdentifier = subjectMatch;
                }
                else if (m_configuration.CertificateManager is ICertificateRegistry registryFallback)
                {
                    // Subject changed mid-rotation: use the manager registry's
                    // currently-registered cert for this type to identify the
                    // configured identifier (matches by certificate type).
                    using (CertificateEntry? currentEntry = registryFallback
                        .AcquireApplicationCertificateByType(certificateTypeId))
                    {
                        if (currentEntry == null)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidArgument,
                                "No existing certificate found for the specified certificate type and subject name.");
                        }
                    }

                    existingCertIdentifier = certificateGroup.ApplicationCertificates
                        .ToList()
                        .FirstOrDefault(cert => cert.CertificateType == certificateTypeId) ??
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidArgument,
                            "No existing certificate found for the specified certificate type and subject name.");
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "No existing certificate found for the specified certificate type and subject name.");
                }

                newIssuerCollection = [];

                try
                {
                    // build issuer chain
                    foreach (ByteString issuerRawCert in issuerCertificates)
                    {
                        using var issuerCertificate = Certificate.FromRawData(issuerRawCert);
                        newIssuerCollection.Add(issuerCertificate);
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
                        await ValidatePushCertificateAndIssuerChainAsync(
                            newCert,
                            newIssuerCollection,
                            m_configuration.SecurityConfiguration,
                            Server.Telemetry,
                            ct).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.FailedToVerifyIntegrityOfTheNew(ex, newCert);
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
                    ICertificatePasswordProvider? passwordProvider = m_configuration
                        .SecurityConfiguration
                        .CertificatePasswordProvider;
                    switch (privateKeyFormat)
                    {
                        case null:
                        case "":
                            for (int attempt = 0; ; attempt++)
                            {
                                Certificate? exportableKey = null;
                                try
                                {
                                    // use the new generated private key if one exists and matches the provided public key
                                    if (certificateGroup.TemporaryApplicationCertificate != null &&
                                        X509Utils.VerifyKeyPair(
                                            newCert,
                                            certificateGroup.TemporaryApplicationCertificate))
                                    {
                                        // CA2000: exportableKey is disposed in the finally below; the analyzer
                                        // cannot track disposal across the for-retry loop / try / catch structure.
#pragma warning disable CA2000
                                        exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                            certificateGroup.TemporaryApplicationCertificate,
                                            false);
#pragma warning restore CA2000
                                    }
                                    else
                                    {
                                        using Certificate certWithPrivateKey = await CertificateIdentifierResolver
                                            .LoadPrivateKeyAsync(
                                                existingCertIdentifier,
                                                passwordProvider,
                                                m_configuration.ApplicationUri,
                                                Server.Telemetry,
                                                ct)
                                            .ConfigureAwait(false) ??
                                            throw new ServiceResultException(
                                                StatusCodes.BadSecurityChecksFailed,
                                                "A private key was not found");
                                        // CA2000: exportableKey is disposed in the finally below; the analyzer
                                        // cannot track disposal across the for-retry loop / try / catch structure.
#pragma warning disable CA2000
                                        exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                            certWithPrivateKey,
                                            false);
#pragma warning restore CA2000
                                    }

                                    updateCertificate.CertificateWithPrivateKey?.Dispose();
                                    updateCertificate.CertificateWithPrivateKey =
                                        DefaultCertificateFactory.Instance.CreateWithPrivateKey(
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
                                        m_logger.FailedToUpdateCertificateCertificateRetrying(ex, newCert);
                                    }
                                }
                                finally
                                {
                                    exportableKey?.Dispose();
                                }
                            }
                            break;
                        case "PFX":
                            for (int attempt = 0; ; attempt++)
                            {
#if !NET9_0_OR_GREATER
                                // https://github.com/OPCFoundation/UA-.NETStandard/commit/0b24d62b7c2bab2e5ed08e694103d49278e457af
                                // CopyWithPrivateKey apparently does not support ephimeralkeysets on windows
                                bool noEphemeralKeySet = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
                                // But it seems to work on .net 9 - and we prefer that over files
                                const bool noEphemeralKeySet = false;
#endif
                                // CA2000: certWithPrivateKey is a using declaration (disposed at scope exit);
                                // the analyzer mis-flags it inside the for-retry loop with break.
#pragma warning disable CA2000
                                using Certificate certWithPrivateKey = X509Utils.CreateCertificateFromPKCS12(
                                    privateKey.ToArray(),
                                    passwordProvider?.GetPassword(existingCertIdentifier),
                                    noEphemeralKeySet);
#pragma warning restore CA2000
                                try
                                {
                                    updateCertificate.CertificateWithPrivateKey?.Dispose();
                                    updateCertificate.CertificateWithPrivateKey =
                                        DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                                            newCert,
                                            certWithPrivateKey);
                                    await UpdateCertificateInternalAsync(
                                        certificateGroup,
                                        existingCertIdentifier,
                                        updateCertificate, ct).ConfigureAwait(false);
                                    break;
                                }
                                catch (Exception ex) when (ShouldRetry(attempt, ex))
                                {
                                    m_logger.FailedToUpdateCertificateCertificateWithPFX(ex, newCert);
                                }
                            }
                            break;
                        case "PEM":
                            for (int attempt = 0; ; attempt++)
                            {
                                updateCertificate.CertificateWithPrivateKey?.Dispose();
                                updateCertificate.CertificateWithPrivateKey =
                                    DefaultCertificateFactory.Instance.CreateWithPEMPrivateKey(
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
                                    m_logger.FailedToUpdateCertificateCertificateWithPEM(ex, newCert);
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
                    certificateGroup.TemporaryApplicationCertificate = null!;
                }

                certificateGroup.UpdateCertificate = updateCertificate;
                // Ownership of the issuer collection now belongs to the staged
                // UpdateCertificate (and the certificate group); clear the local
                // handle so the finally does not dispose the staged collection.
                newIssuerCollection = null;
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
                Server.ReportAuditCertificateEvent(newCert!, e, m_logger);
                throw;
            }
            finally
            {
                newCert?.Dispose();
                // Disposed only when ownership was not transferred to the staged
                // UpdateCertificate (i.e. an exception occurred before staging).
                newIssuerCollection?.Dispose();
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
                    // Resolve the currently-loaded certificate so we can
                    // delete the right blob from the store. The configured
                    // CertificateIdentifier may not carry an explicit
                    // thumbprint (typical config: only StorePath +
                    // SubjectName), and the identifier no longer caches the
                    // loaded certificate, so we ask the registry for the
                    // currently-active cert of this type.
                    string? thumbprintToDelete = null;
                    if (m_configuration.CertificateManager is ICertificateRegistry registry)
                    {
                        using CertificateEntry? currentEntry = registry
                            .AcquireApplicationCertificateByType(existingCertIdentifier.CertificateType);
                        thumbprintToDelete = currentEntry?.Certificate.Thumbprint
                            ?? existingCertIdentifier.Thumbprint;

                        // Capture the pre-transaction certificate exactly
                        // once, even if UpdateCertificate is called multiple
                        // times before ApplyChanges. Per OPC UA Part 12
                        // §7.10.2 a transaction groups multiple changes; the
                        // channel-cut in ApplyChanges must match every
                        // SecureChannel still negotiated against the cert
                        // that was active when the transaction started —
                        // including connections that arrived between the
                        // first and last staged UpdateCertificate. The
                        // captured cert is owned by the group and disposed
                        // by ApplyChanges after consumption (or by
                        // DisposePendingRotationState on teardown).
                        if (certificateGroup.OriginalCertificate == null && currentEntry != null)
                        {
                            certificateGroup.OriginalCertificate = currentEntry.Certificate.AddRef();
                            certificateGroup.OriginalCertificateType =
                                existingCertIdentifier.CertificateType;
                        }
                    }
                    else
                    {
                        thumbprintToDelete = existingCertIdentifier.Thumbprint;
                    }

                    using (ICertificateStore? appStore = CertificateIdentifierResolver
                        .OpenStore(existingCertIdentifier, Server.Telemetry))
                    {
                        if (appStore == null)
                        {
                            throw ServiceResultException.ConfigurationError(
                                "Failed to open application certificate store.");
                        }

                        m_logger.DeleteApplicationCertificateThumbprint(thumbprintToDelete);
                        if (!string.IsNullOrEmpty(thumbprintToDelete))
                        {
                            await appStore.DeleteAsync(
                                thumbprintToDelete!,
                                ct)
                                .ConfigureAwait(false);
                        }
                        ICertificatePasswordProvider? passwordProvider = m_configuration
                            .SecurityConfiguration
                            .CertificatePasswordProvider;
                        m_logger.AddNewApplicationCertificateCertificate(updateCertificate.CertificateWithPrivateKey);
                        Debug.Assert(updateCertificate.CertificateWithPrivateKey.HasPrivateKey);
                        await appStore.AddAsync(
                            updateCertificate.CertificateWithPrivateKey,
                            passwordProvider?.GetPassword(existingCertIdentifier),
                            ct)
                            .ConfigureAwait(false);

                        // Replace the registered application certificate in
                        // the CertificateManager's registry so endpoint
                        // descriptions, transport listeners, and validation
                        // cores pick up the new cert without waiting for the
                        // ApplyChanges-driven UpdateAsync reload.
                        if (m_configuration.CertificateManager is ICertificateLifecycle lifecycle)
                        {
                            await lifecycle.UpdateApplicationCertificateAsync(
                                existingCertIdentifier.CertificateType,
                                updateCertificate.CertificateWithPrivateKey,
                                issuerChain: null,
                                ct).ConfigureAwait(false);
                        }

                        // keep only track of cert without private key
                        var certOnly = Certificate.FromRawData(
                            updateCertificate.CertificateWithPrivateKey.RawData);
                        updateCertificate.CertificateWithPrivateKey.Dispose();
                        updateCertificate.CertificateWithPrivateKey = certOnly;
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
                                m_logger.AddNewIssuerCertificateCertificate(issuer);
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
                        issuerStore?.Dispose();
                    }

                    updateCertificate.IssuerCollection?.Dispose();
                    updateCertificate.IssuerCollection = null!;

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
                    m_logger.FailedToUpdateCertificateCertificate(ex, newCert);
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "Failed to update certificate.",
                        ex);
                }
            }
        }

        internal static async Task ValidatePushCertificateAndIssuerChainAsync(
            Certificate newCertificate,
            CertificateCollection issuerCertificates,
            SecurityConfiguration securityConfiguration,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            if (newCertificate == null)
            {
                throw new ArgumentNullException(nameof(newCertificate));
            }

            if (issuerCertificates == null)
            {
                throw new ArgumentNullException(nameof(issuerCertificates));
            }

            if (securityConfiguration == null)
            {
                throw new ArgumentNullException(nameof(securityConfiguration));
            }

            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            using CertificateCollection validationChain = issuerCertificates.AddRef();
            validationChain.Insert(0, newCertificate);

            using CertificateManager validator = CertificateManagerFactory.Create(securityConfiguration, telemetry);
            var options = new Security.Certificates.CertificateValidationOptions
            {
                AllowCertificateDownload = false,
                UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1),
                AcceptError = static (_, serviceResult) =>
                    serviceResult.StatusCode == StatusCodes.BadCertificateUntrusted
            };

            CertificateValidationResult validationResult = await validator.ValidateAsync(
                validationChain,
                trustList: null,
                options: options,
                ct).ConfigureAwait(false);

            validationResult.ThrowIfInvalid();
        }

        /// <summary>
        /// Creates a new self-signed certificate per OPC 10000-12 §7.10.6.
        /// The server generates a key pair internally, builds a self-signed
        /// certificate with the requested subject / DNS / IP and lifetime,
        /// stores it, and returns the DER-encoded public certificate.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ValueTask<CreateSelfSignedCertificateMethodStateResult>
            CreateSelfSignedCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> dnsNames,
            ArrayOf<string> ipAddresses,
            ushort lifetimeInDays,
            ushort keySizeInBits,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup? certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            if (string.IsNullOrEmpty(subjectName))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "SubjectName must be provided.");
            }

            if (lifetimeInDays == 0)
            {
                lifetimeInDays = CertificateFactory.DefaultLifeTime;
            }

            // merge DNS names and IP addresses into one domain list
            var domainNames = new List<string>();
            if (!dnsNames.IsNull)
            {
                foreach (string dns in dnsNames)
                {
                    if (!string.IsNullOrEmpty(dns))
                    {
                        domainNames.Add(dns);
                    }
                }
            }
            if (!ipAddresses.IsNull)
            {
                foreach (string ip in ipAddresses)
                {
                    if (!string.IsNullOrEmpty(ip))
                    {
                        domainNames.Add(ip);
                    }
                }
            }

            ICertificateBuilder builder = s_certificateFactory
                .CreateApplicationCertificate(
                    m_configuration.ApplicationUri!,
                    m_configuration.ApplicationName!,
                    subjectName,
                    [.. domainNames])
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddDays(lifetimeInDays));

            Certificate certificate;
            if (certificateTypeId.IsNull ||
                certificateTypeId == ObjectTypeIds.ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                ushort keySize = keySizeInBits > 0
                    ? keySizeInBits
                    : CertificateFactory.DefaultKeySize;
                certificate = builder.SetRSAKeySize(keySize).CreateForRSA();
            }
            else
            {
                ECCurve? curve =
                    CryptoUtils.GetCurveFromCertificateTypeId(certificateTypeId)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The ECC certificate type is not supported.");
                certificate = builder.SetECCurve(curve.Value).CreateForECDsa();
            }

            // persist the new self-signed certificate into the group's
            // configured store so it survives restarts and becomes the
            // active application certificate.
            CertificateIdentifier? existingIdent = certificateGroup!.ApplicationCertificates
                .ToList()
                .FirstOrDefault(c => c.CertificateType == certificateTypeId);

            existingIdent?.RawData = certificate.RawData;

            m_logger.CreatedSelfSignedCertificateSubjectForGroup(
                certificate.Subject,
                certificateGroupId,
                certificateTypeId);

            ByteString certBytes = certificate.RawData.ToByteString();
            return new ValueTask<CreateSelfSignedCertificateMethodStateResult>(
                new CreateSelfSignedCertificateMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Certificate = certBytes
                });
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

            ServerCertificateGroup? certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            // identify the existing certificate for which to CreateSigningRequest
            // it should be of the same type
            CertificateIdentifier? existingCertIdentifier = certificateGroup!.ApplicationCertificates
                .ToList().FirstOrDefault(
                    cert => cert.CertificateType == certificateTypeId);

            // Look up the currently-active certificate via the manager
            // registry — the configured identifier is metadata only. The
            // acquired entry is disposed at method scope; the borrowed
            // certificate is only read.
            using CertificateEntry? currentEntry =
                (m_configuration.CertificateManager as ICertificateRegistry)?
                    .AcquireApplicationCertificateByType(certificateTypeId);
            Certificate? currentCert = currentEntry?.Certificate;

            if (string.IsNullOrEmpty(subjectName))
            {
                subjectName = (currentCert?.Subject ?? existingCertIdentifier?.SubjectName)!;
            }

            certificateGroup.TemporaryApplicationCertificate?.Dispose();
            certificateGroup.TemporaryApplicationCertificate = null!;

            Certificate certWithPrivateKey;
            if (regeneratePrivateKey)
            {
                ArrayOf<string> domainNames = currentCert != null
                    ? X509Utils.GetDomainsFromCertificate(currentCert)
                    : default;

                certWithPrivateKey = GenerateTemporaryApplicationCertificate(
                    certificateTypeId,
                    certificateGroup,
                    subjectName,
                    domainNames);
            }
            else
            {
                ICertificatePasswordProvider? passwordProvider = m_configuration
                    .SecurityConfiguration
                    .CertificatePasswordProvider;
                certWithPrivateKey = await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        existingCertIdentifier!,
                        passwordProvider,
                        m_configuration.ApplicationUri,
                        Server.Telemetry,
                        cancellationToken)
                    .ConfigureAwait(false) ??
                    throw ServiceResultException.Create(StatusCodes.BadInternalError, "Failed to load private key");
            }

            try
            {
                m_logger.CreateSigningRequestCertificate(certWithPrivateKey);
                var certificateRequest = ByteString.From(s_certificateFactory.CreateSigningRequest(
                    certWithPrivateKey,
                    X509Utils.GetDomainsFromCertificate(certWithPrivateKey).ToArray()));

                return new CreateSigningRequestMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    CertificateRequest = certificateRequest
                };
            }
            finally
            {
                if (!regeneratePrivateKey)
                {
                    certWithPrivateKey.Dispose();
                }
            }
        }

        private Certificate GenerateTemporaryApplicationCertificate(
            NodeId certificateTypeId,
            ServerCertificateGroup certificateGroup,
            string subjectName,
            ArrayOf<string> domainNames)
        {
            Certificate certificate;

            ICertificateBuilder certificateBuilder = s_certificateFactory
                .CreateApplicationCertificate(m_configuration.ApplicationUri!, m_configuration.ApplicationName!, subjectName, domainNames.ToArray())
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

            // Capture the per-group rotation payload (original cert +
            // type, captured at the first staged UpdateCertificate) and
            // clear the staging slot so the post-response channel-cut
            // can target the correct SecureChannels by thumbprint per
            // OPC UA Part 12 §7.10.9.
            var pendingRotations = new List<PendingCertificateRotation>();

            foreach (ServerCertificateGroup certificateGroup in m_certificateGroups)
            {
                UpdateCertificateData? updateCertificate = certificateGroup.UpdateCertificate;
                if (updateCertificate == null)
                {
                    // No staged update for this group — but a previous
                    // failed staging may have left an OriginalCertificate
                    // behind. Discard it so it does not leak.
                    DisposePendingRotationState(certificateGroup);
                    continue;
                }

                m_logger.ApplyChangesForCertificateCertificate(updateCertificate.CertificateWithPrivateKey);

                // Hand off ownership of OriginalCertificate to the
                // deferred task. The reference on the group is then
                // cleared so DisposePendingRotationState cannot
                // double-dispose it.
                pendingRotations.Add(new PendingCertificateRotation
                {
                    OldCertificate = certificateGroup.OriginalCertificate,
                    CertificateType = certificateGroup.OriginalCertificateType
                });
                certificateGroup.OriginalCertificate = null;
                certificateGroup.OriginalCertificateType = NodeId.Null;
                updateCertificate.CertificateWithPrivateKey?.Dispose();
                updateCertificate.IssuerCollection?.Dispose();
                certificateGroup.UpdateCertificate = null!;
            }

            if (pendingRotations.Count == 0)
            {
                return StatusCodes.Good;
            }

            // Schedule the deferred apply: wait a short grace period for
            // the method response to be flushed, then re-sync the
            // certificate manager from disk and force-close every
            // SecureChannel that was negotiated against the rotated
            // certificate(s). The completion handle is exposed via
            // DrainPendingApplyChangesAsync so tests and hosts can
            // deterministically await rotation rather than racing the
            // delay.
            ScheduleDeferredApplyChanges(pendingRotations);

            return StatusCodes.Good;
        }

        /// <summary>
        /// Schedules the post-response cert-rotation fan-out. Chains
        /// onto any already-running deferred apply so concurrent calls
        /// to <see cref="ApplyChanges"/> run sequentially.
        /// </summary>
        private void ScheduleDeferredApplyChanges(List<PendingCertificateRotation> rotations)
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task previous;
            lock (m_pendingApplyChangesLock)
            {
                previous = m_pendingApplyChangesTask;
                m_pendingApplyChangesTask = completion.Task;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for any earlier deferred apply to finish to
                    // preserve ordering.
                    if (previous != null)
                    {
                        try
                        {
                            await previous.ConfigureAwait(false);
                        }
                        catch
                        {
                            // Errors on the previous task are already
                            // logged; do not propagate to the new one.
                        }
                    }

                    TimeSpan gracePeriod = ApplyChangesGracePeriod;
                    if (gracePeriod < TimeSpan.Zero)
                    {
                        gracePeriod = TimeSpan.Zero;
                    }

                    m_logger.ApplyChangesForApplicationCertificateScheduled(gracePeriod.TotalMilliseconds);

                    // Give the client a chance to receive the
                    // ApplyChanges response before cutting its channel.
                    // OPC UA Part 12 §7.10.9 requires the response is
                    // returned first; without a transport-level
                    // "response flushed" hook this grace period is the
                    // pragmatic compromise. The grace period itself is
                    // configurable via ApplyChangesGracePeriod so hosts
                    // running over high-latency links can tune it.
                    // TODO: implement a transport-level
                    // "response-flushed" callback so this can be
                    // deterministic without relying on a fixed delay.
                    await m_timeProvider.Delay(gracePeriod)
                        .ConfigureAwait(false);

                    m_logger.ApplyChangesForApplicationCertificateRunning();

                    if (m_configuration.CertificateManager != null)
                    {
                        await m_configuration.CertificateManager.UpdateAsync(
                                m_configuration.SecurityConfiguration,
                                m_configuration.ApplicationUri)
                            .ConfigureAwait(false);
                    }

                    // Force-close affected SecureChannels on every
                    // transport listener that opted into
                    // ITransportListenerCertificateRotation.
                    IReadOnlyList<ITransportListener> listeners
                        = (Server as ITransportListenerRegistryProvider)?.TransportListeners
                            ?? [];

                    int totalCut = 0;
                    foreach (PendingCertificateRotation rotation in rotations)
                    {
                        if (rotation.OldCertificate == null)
                        {
                            continue;
                        }

                        foreach (ITransportListener listener in listeners)
                        {
                            if (listener is not ITransportListenerCertificateRotation rotator)
                            {
                                continue;
                            }

                            try
                            {
                                IReadOnlyList<string> closed
                                    = await rotator.CloseChannelsForCertificateAsync(rotation.OldCertificate)
                                        .ConfigureAwait(false);
                                totalCut += closed.Count;
                            }
                            catch (Exception ex)
                            {
                                m_logger.ListenerListenerFailedToCloseChannelsFor(
                                    ex,
                                    listener.ListenerId,
                                    rotation.CertificateType);
                            }
                        }
                    }

                    m_logger.ApplyChangesForApplicationCertificateCompleted(totalCut);

                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    m_logger.ApplyChangesForApplicationCertificateUpdateFailed(ex);
                    completion.TrySetException(ex);
                }
                finally
                {
                    foreach (PendingCertificateRotation rotation in rotations)
                    {
                        rotation.OldCertificate?.Dispose();
                    }
                }
            });
        }

        /// <inheritdoc/>
        public Task DrainPendingApplyChangesAsync(CancellationToken cancellationToken = default)
        {
            Task pending;
            lock (m_pendingApplyChangesLock)
            {
                pending = m_pendingApplyChangesTask;
            }

            if (pending == null || pending.IsCompleted)
            {
                return Task.CompletedTask;
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return pending;
            }

            return pending.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Clears the staging slot for a new <c>UpdateCertificate</c>
        /// call. The pre-transaction
        /// <see cref="ServerCertificateGroup.OriginalCertificate"/> is
        /// <i>preserved</i> across consecutive stagings so a multi-step
        /// transaction (Part 12 §7.10.2) still cuts every SecureChannel
        /// established before the first staged update. Use
        /// <see cref="DisposePendingRotationState"/> to release the
        /// captured certificate on full teardown.
        /// </summary>
        private static void ResetPendingUpdateCertificate(ServerCertificateGroup certificateGroup)
        {
            certificateGroup.UpdateCertificate?.CertificateWithPrivateKey?.Dispose();
            certificateGroup.UpdateCertificate?.IssuerCollection?.Dispose();
            certificateGroup.UpdateCertificate = null!;
        }

        /// <summary>
        /// Releases the pre-transaction certificate captured during
        /// <c>UpdateCertificate</c> staging and clears the staging slot.
        /// Called by <see cref="ApplyChanges"/> when a group has no
        /// pending update (stale capture from a failed transaction) and
        /// by the manager's <see cref="Dispose"/>.
        /// </summary>
        private static void DisposePendingRotationState(ServerCertificateGroup certificateGroup)
        {
            certificateGroup.OriginalCertificate?.Dispose();
            certificateGroup.OriginalCertificate = null;
            certificateGroup.OriginalCertificateType = NodeId.Null;
            certificateGroup.UpdateCertificate?.CertificateWithPrivateKey?.Dispose();
            certificateGroup.UpdateCertificate?.IssuerCollection?.Dispose();
            certificateGroup.UpdateCertificate = null!;
        }

        /// <summary>
        /// Captured payload for a single certificate-group rotation
        /// scheduled by <see cref="ApplyChanges"/>. The deferred apply
        /// task owns the contained <see cref="Certificate"/> reference
        /// and disposes it once the channel-cut completes.
        /// </summary>
        private sealed class PendingCertificateRotation
        {
            public Certificate? OldCertificate { get; set; }
            public NodeId CertificateType { get; set; }
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
                    using CertificateCollection collection = store.EnumerateAsync().Result;
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

            // Look up each certificate via the manager registry so the
            // returned blobs reflect the currently-active cert (the
            // configured identifier carries no Certificate cache).
            var rawCerts = new List<ByteString>();
            var registry = m_configuration.CertificateManager as ICertificateRegistry;
            foreach (CertificateIdentifier appId in certificateGroup.ApplicationCertificates)
            {
                using CertificateEntry? entry = registry?.AcquireApplicationCertificateByType(appId.CertificateType);
                rawCerts.Add(entry?.Certificate?.RawData.ToByteString() ?? default);
            }
            certificates = rawCerts.ToArrayOf();

            return ServiceResult.Good;
        }

        private ServerCertificateGroup? VerifyGroupAndTypeId(
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
        private async ValueTask<NamespaceMetadataState?> FindNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            try
            {
                // find ServerNamespaces node
                if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                    is not NamespacesState serverNamespacesNode)
                {
                    m_logger.FindObjectIdsServerNamespacesNode();
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

                    if (namespaceMetadata!.NamespaceUri!.Value == namespaceUri)
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
                        // Find NamespaceMetadata node of NamespaceUri in Namespaces references.
                        var nameSpaceNodeId = ExpandedNodeId.ToNodeId(
                            serverNamespacesReference.TargetId,
                            Server.NamespaceUris);
                        if (await Server.NodeManager.FindNodeInAddressSpaceAsync(
                            nameSpaceNodeId, cancellationToken).ConfigureAwait(false) is not NamespaceMetadataState namespaceMetadata)
                        {
                            continue;
                        }

                        if (namespaceMetadata!.NamespaceUri!.Value == namespaceUri)
                        {
                            return namespaceMetadata;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                m_logger.ErrorSearchingNamespaceMetadataForNamespaceUri(ex, namespaceUri);
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
        public event EventHandler? DefaultPermissionsChanged;

        private class UpdateCertificateData
        {
            public NodeId SessionId { get; set; }
            public Certificate CertificateWithPrivateKey { get; set; } = null!;
            public CertificateCollection IssuerCollection { get; set; } = null!;
        }

        /// <summary>
        /// Evaluates certificate expiration and trust-list staleness for
        /// all certificate groups and activates/deactivates the optional
        /// <c>CertificateExpired</c> and <c>TrustListOutOfDate</c> alarm
        /// instances per OPC 10000-12 §7.8.3.
        /// </summary>
        /// <inheritdoc/>
        public void StartAlarmMonitoring(TimeSpan interval)
        {
            if (m_alarmTimer != null)
            {
                return;
            }

            m_alarmTimer = m_timeProvider.CreateTimer(
                _ =>
                {
                    try
                    {
                        EvaluateCertificateAlarms(SystemContext);
                    }
                    catch (Exception ex)
                    {
                        m_logger.AlarmEvaluationTickFailed(ex);
                    }
                },
                null,
                interval,
                interval);
        }

        /// <inheritdoc/>
        public void StopAlarmMonitoring()
        {
            m_alarmTimer?.Dispose();
            m_alarmTimer = null;
        }

        private void EvaluateCertificateAlarms(ISystemContext context)
        {
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                CertificateGroupState node = certGroup.Node!;

                try
                {
                    // --- CertificateExpired alarm ---
                    // Only populate properties if the optional alarm instance
                    // was loaded from the predefined nodeset. We set property
                    // values directly rather than calling SetActiveState to
                    // avoid triggering event notifications during server
                    // startup (which can fail before subscriptions exist).
                    if (node.CertificateExpired?.ExpirationDate != null)
                    {
                        DateTime expirationDate = DateTime.MaxValue;

                        foreach (CertificateIdentifier certIdent in certGroup.ApplicationCertificates)
                        {
                            if (certIdent.RawData != null && certIdent.RawData.Length > 0)
                            {
                                try
                                {
                                    using var cert = Certificate.FromRawData(certIdent.RawData);
                                    if (cert.NotAfter < expirationDate)
                                    {
                                        expirationDate = cert.NotAfter;
                                    }
                                }
                                catch
                                {
                                    // ignore parsing errors
                                }
                            }
                        }

                        if (expirationDate != DateTime.MaxValue)
                        {
                            node.CertificateExpired.ExpirationDate.Value = expirationDate;
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_logger.FailedToEvaluateCertificateExpiredAlarmForGroup(ex, certGroup.BrowseName);
                }

                try
                {
                    // --- TrustListOutOfDate alarm ---
                    if (node.TrustListOutOfDate?.TrustListId != null)
                    {
                        node.TrustListOutOfDate.TrustListId.Value =
                            node.TrustList?.NodeId ?? default;

                        node.TrustListOutOfDate.LastUpdateTime?.Value =
                                (DateTime)(node.TrustList?.LastUpdateTime?.Value
                                    ?? (DateTimeUtc)DateTime.MinValue);
                    }
                }
                catch (Exception ex)
                {
                    m_logger.FailedToEvaluateTrustListOutOfDateAlarmForGroup(ex, certGroup.BrowseName);
                }
            }
        }

        private class ServerCertificateGroup
        {
            public string BrowseName { get; set; } = null!;
            public NodeId NodeId { get; set; }
            public CertificateGroupState Node { get; set; } = null!;
            public NodeId[] CertificateTypes { get; set; } = null!;
            public ArrayOf<CertificateIdentifier> ApplicationCertificates { get; set; }
            public CertificateStoreIdentifier IssuerStore { get; set; } = null!;
            public CertificateStoreIdentifier TrustedStore { get; set; } = null!;
            public UpdateCertificateData UpdateCertificate { get; set; } = null!;
            public Certificate TemporaryApplicationCertificate { get; set; } = null!;

            /// <summary>
            /// The application certificate that was active in the
            /// registry BEFORE the first <c>UpdateCertificate</c> call
            /// of the current transaction. Captured on the first staging
            /// in <c>UpdateCertificateInternalAsync</c> and preserved
            /// across subsequent staging calls (per OPC UA Part 12
            /// §7.10.2 transaction lifecycle) so that the channel-cut
            /// in <c>ApplyChanges</c> matches every SecureChannel still
            /// negotiated against the pre-transaction certificate —
            /// including connections established between the first and
            /// last staged <c>UpdateCertificate</c>. Owned by the group;
            /// disposed only by <c>ApplyChanges</c> after consumption
            /// or by <c>DisposePendingRotationState</c> on
            /// teardown.
            /// </summary>
            public Certificate? OriginalCertificate { get; set; }

            /// <summary>
            /// The certificate type that <see cref="OriginalCertificate"/>
            /// belongs to. <see cref="NodeId.Null"/> when no original
            /// has been captured.
            /// </summary>
            public NodeId OriginalCertificateType { get; set; }
        }

#pragma warning disable CA2213 // m_serverConfigurationNode is owned by the address space, not by this manager.
        private ServerConfigurationState? m_serverConfigurationNode;
        private UserManagement.UserManagementBinding? m_userManagementBinding;
#pragma warning restore CA2213
        private readonly ApplicationConfiguration m_configuration;
        private readonly TimeProvider m_timeProvider;
        private readonly List<ServerCertificateGroup> m_certificateGroups;
        private readonly CertificateStoreIdentifier? m_rejectedStore;
        private ITimer? m_alarmTimer;
        private readonly Dictionary<string, NamespaceMetadataState> m_namespaceMetadataStates = [];
        private readonly Dictionary<ushort, NamespaceMetadataState> m_namespaceMetadataStatesByIndex = [];
        private readonly Lock m_namespaceMetadataStatesLock = new();
        private readonly Lock m_pendingApplyChangesLock = new();
        private Task m_pendingApplyChangesTask = Task.CompletedTask;

        /// <inheritdoc/>
        public TimeSpan ApplyChangesGracePeriod { get; set; }
            = TimeSpan.FromMilliseconds(250);

        private static readonly ICertificateFactory s_certificateFactory = DefaultCertificateFactory.Instance;
    }

    /// <summary>
    /// Source-generated log messages for ConfigurationNodeManager.
    /// </summary>
    internal static partial class ConfigurationNodeManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 0, Level = LogLevel.Error,
            Message = "Cannot create NamespaceMetadataState for namespace '{NamespaceUri}'.")]
        public static partial void CreateNamespaceMetadataStateForNamespaceNamespaceUri(
            this ILogger logger,
            string namespaceUri);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 1, Level = LogLevel.Error,
            Message = "Failed to verify integrity of the new certificate {Certificate} and the issuer list.")]
        public static partial void FailedToVerifyIntegrityOfTheNew(
            this ILogger logger,
            Exception ex,
            Certificate certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 2, Level = LogLevel.Debug,
            Message = "Failed to update certificate {Certificate}. Retrying...")]
        public static partial void FailedToUpdateCertificateCertificateRetrying(
            this ILogger logger,
            Exception ex,
            Certificate certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 3, Level = LogLevel.Debug,
            Message = "Failed to update certificate {Certificate} with PFX private key. Retrying...")]
        public static partial void FailedToUpdateCertificateCertificateWithPFX(
            this ILogger logger,
            Exception ex,
            Certificate certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 4, Level = LogLevel.Debug,
            Message = "Failed to update certificate {Certificate} with PEM private key. Retrying...")]
        public static partial void FailedToUpdateCertificateCertificateWithPEM(
            this ILogger logger,
            Exception ex,
            Certificate certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 5, Level = LogLevel.Information,
            Message = "Delete application certificate {Thumbprint}")]
        public static partial void DeleteApplicationCertificateThumbprint(this ILogger logger, string? thumbprint);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 6, Level = LogLevel.Information,
            Message = "Add new application certificate {Certificate}")]
        public static partial void AddNewApplicationCertificateCertificate(this ILogger logger, Certificate? certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 7, Level = LogLevel.Information,
            Message = "Add new issuer certificate {Certificate}")]
        public static partial void AddNewIssuerCertificateCertificate(this ILogger logger, Certificate certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 8, Level = LogLevel.Error,
            Message = "Failed to update certificate {Certificate}.")]
        public static partial void FailedToUpdateCertificateCertificate(
            this ILogger logger,
            Exception ex,
            Certificate? certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 9, Level = LogLevel.Information,
            Message = "Created self-signed certificate {Subject} for {Group}/{Type}.")]
        public static partial void CreatedSelfSignedCertificateSubjectForGroup(
            this ILogger logger,
            string? subject,
            NodeId group,
            NodeId type);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 10, Level = LogLevel.Information,
            Message = "Create signing request {Certificate}")]
        public static partial void CreateSigningRequestCertificate(this ILogger logger, Certificate certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 11, Level = LogLevel.Information,
            Message = "Apply Changes for certificate {Certificate}")]
        public static partial void ApplyChangesForCertificateCertificate(this ILogger logger, Certificate? certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 12, Level = LogLevel.Information,
            Message = "Apply Changes for application certificate scheduled in {Grace} ms...")]
        public static partial void ApplyChangesForApplicationCertificateScheduled(this ILogger logger, double grace);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 13, Level = LogLevel.Information,
            Message = "Apply Changes for application certificate running...")]
        public static partial void ApplyChangesForApplicationCertificateRunning(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 14, Level = LogLevel.Warning,
            Message = "Listener {Listener} failed to close channels for {CertType}.")]
        public static partial void ListenerListenerFailedToCloseChannelsFor(
            this ILogger logger,
            Exception ex,
            string listener,
            NodeId certType);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 15, Level = LogLevel.Information,
            Message = "Apply Changes for application certificate completed: {Count} SecureChannel(s) cut.")]
        public static partial void ApplyChangesForApplicationCertificateCompleted(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 16, Level = LogLevel.Critical,
            Message = "Apply Changes for application certificate update failed. Server could be in a faulted state.")]
        public static partial void ApplyChangesForApplicationCertificateUpdateFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 17, Level = LogLevel.Error,
            Message = "Cannot find ObjectIds.Server_Namespaces node.")]
        public static partial void FindObjectIdsServerNamespacesNode(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 18, Level = LogLevel.Error,
            Message = "Error searching NamespaceMetadata for namespaceUri {NamespaceUri}.")]
        public static partial void ErrorSearchingNamespaceMetadataForNamespaceUri(
            this ILogger logger,
            Exception ex,
            string namespaceUri);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 19, Level = LogLevel.Warning,
            Message = "Alarm evaluation tick failed.")]
        public static partial void AlarmEvaluationTickFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 20, Level = LogLevel.Warning,
            Message = "Failed to evaluate CertificateExpired alarm for group {Group}.")]
        public static partial void FailedToEvaluateCertificateExpiredAlarmForGroup(
            this ILogger logger,
            Exception ex,
            string group);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 21, Level = LogLevel.Warning,
            Message = "Failed to evaluate TrustListOutOfDate alarm for group {Group}.")]
        public static partial void FailedToEvaluateTrustListOutOfDateAlarmForGroup(
            this ILogger logger,
            Exception ex,
            string group);
    }

}
