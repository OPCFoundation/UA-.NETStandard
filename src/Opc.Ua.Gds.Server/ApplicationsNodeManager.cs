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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Diagnostics;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// A node manager for a global discovery server
    /// </summary>
    /// <remarks>
    /// <para>
    /// The GDS companion model, its loader and the fluent plumbing are
    /// source-generated from the <c>[NodeManager]</c> attribute below. The
    /// design stays owned by <c>Opc.Ua.Gds.Common</c>, which emits the model
    /// types; this assembly only binds a manager to it. What is written by
    /// hand is the behaviour, and it arrives in two passes: the I/O of
    /// starting the certificate authorities in
    /// <see cref="OnAddressSpaceReadyAsync"/>, then everything that touches
    /// the address space in <see cref="OnConfigure"/> — which is where the
    /// nodes those authorities are addressed by are resolved or created.
    /// </para>
    /// <para>
    /// The namespace order is deliberate and load-bearing:
    /// <c>NamespaceIndexes[0]</c> must stay the application-record
    /// namespace, because it is the one the application database, the
    /// certificate-request store and the base class's NodeId allocator
    /// mint ids in. The generated <c>DefaultNamespaceUris()</c> puts the
    /// model namespace first, so the constructor passes the order
    /// explicitly instead.
    /// </para>
    /// <para>
    /// The model ships its <c>AuthorizationServices</c> and
    /// <c>KeyCredentialManagement</c> folders empty. The GDS fills the
    /// first with a <c>Default</c> service, created and wired together in
    /// the <c>Configure</c> pass through the builder's node-creation
    /// surface. A host that adds further services later wires each through
    /// <see cref="ConfigureAuthorizationService"/> or
    /// <see cref="ConfigureKeyCredentialService(KeyCredentialServiceState)"/>;
    /// that is a direct call rather than an
    /// <c>AddBehaviourToPredefinedNodeAsync</c> override, because the
    /// fluent lifecycle hooks are keyed by NodeId and cannot name a node
    /// that does not exist yet.
    /// </para>
    /// </remarks>
    [NodeManager(
        NamespaceUri = Namespaces.OpcUaGds,
        AdditionalNamespaceUris = [ApplicationsNamespaceUri],
        GenerateFactory = false,
        GenerateDefaultConstructor = false)]
    public partial class ApplicationsNodeManager
    {
        /// <summary>
        /// Namespace the GDS mints application records, certificate
        /// requests and other server-owned instance NodeIds in.
        /// </summary>
        public const string ApplicationsNamespaceUri =
            "http://opcfoundation.org/UA/GDS/applications/";

        /// <summary>
        /// Browse name of the authorization service the GDS materialises
        /// under the model's <c>AuthorizationServices</c> folder, which
        /// the companion model itself ships empty.
        /// </summary>
        public const string DefaultAuthorizationServiceName = "Default";

        /// <summary>
        /// Gets or sets the trust-list manager for named store access.
        /// </summary>
        public ICertificateTrustListManager? TrustListManager { get; set; }

        private readonly NodeId m_directoryId;
        private readonly NodeId m_defaultApplicationGroupId;
        private readonly NodeId m_defaultHttpsGroupId;
        private readonly NodeId m_defaultUserTokenGroupId;

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ApplicationsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroupFactory,
            bool autoApprove = false)
            // Application-record namespace first: see the note on the class.
            : this(
                  server,
                  configuration,
                  [ApplicationsNamespaceUri, Namespaces.OpcUaGds])
        {
            m_configuration = configuration;
            // get the configuration for the node manager.
            m_globalDiscoveryServerConfiguration =
                configuration.ParseExtension<GlobalDiscoveryServerConfiguration>()
                ?? new GlobalDiscoveryServerConfiguration();

            // use suitable defaults if no configuration exists.

            string? defaultSubjectNameContext = m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext;
            if (!string.IsNullOrEmpty(defaultSubjectNameContext) &&
                defaultSubjectNameContext![0] != ',')
            {
                m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext =
                    "," + defaultSubjectNameContext;
            }

            m_directoryId = ExpandedNodeId.ToNodeId(ObjectIds.Directory, Server.NamespaceUris);
            m_defaultApplicationGroupId = ExpandedNodeId.ToNodeId(
                ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Server.NamespaceUris);
            m_defaultHttpsGroupId = ExpandedNodeId.ToNodeId(
                ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup,
                Server.NamespaceUris);
            m_defaultUserTokenGroupId = ExpandedNodeId.ToNodeId(
                ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup,
                Server.NamespaceUris);

            m_autoApprove = autoApprove;
            m_database = database;
            m_request = request;
            m_certificateGroupFactory = certificateGroupFactory;
            m_ownedCertificateGroups = [];
            m_certificateGroups = [];

            try
            {
                ServerOnNetwork[]? results = m_database.QueryServers(
                    0,
                    5,
                    null!,
                    null!,
                    null!,
                    default,
                    out DateTimeUtc lastResetTime);
                m_logger.QueryServersReturned(results?.Length ?? 0);

                if (results != null)
                {
                    foreach (ServerOnNetwork result in results)
                    {
                        m_logger.ServerFound(result.DiscoveryUrl);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.CouldNotConnectToDatabase(e);

                Exception? ie = e.InnerException;

                while (ie != null)
                {
                    m_logger.Exception(ie);
                    ie = ie.InnerException;
                }

                m_logger.InitializeDatabaseTables();
                m_database.Initialize();

                m_logger.DatabaseInitialized();
            }
        }

        // --- Fluent wiring of the GDS companion model -------------
        // Everything the Directory object, its certificate groups and
        // the authorization service need is resolved against the
        // loaded address space here, once, from the source-generated
        // CreateAddressSpaceAsync.

        /// <summary>
        /// The generated manager's wiring hook. Kept to a single call so
        /// the work itself stays overridable — a <c>partial</c> method is
        /// private and cannot be.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        partial void Configure(INodeManagerBuilder builder)
        {
            OnConfigure(builder);
        }

        /// <summary>
        /// Binds the node manager's handlers to the loaded GDS model.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every lookup resolves eagerly and throws
        /// <see cref="ServiceResultException"/> when it fails, so a model
        /// that no longer matches the code is reported at startup rather
        /// than as a <c>Bad_NotImplemented</c> on the first call.
        /// </para>
        /// <para>
        /// Subclasses that add their own wiring should override this and
        /// call <c>base.OnConfigure(builder)</c> first; the builder is
        /// sealed as soon as this method returns.
        /// </para>
        /// </remarks>
        /// <param name="builder">The active fluent builder.</param>
        protected virtual void OnConfigure(INodeManagerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            INodeBuilder<CertificateDirectoryState> directory =
                builder.Node<CertificateDirectoryState>(m_directoryId);

            ConfigureDirectoryServices(directory);
            ConfigureCertificateGroups(builder, directory);

            // Created and wired in one place: the builder's Add surface
            // stages the node, finalises its NodeIds before handing it back,
            // and registers it once this pass returns.
            ConfigureAuthorizationService(EnsureDefaultAuthorizationService(builder));
        }

        /// <summary>
        /// Wires the methods of the <c>Directory</c> object
        /// (OPC 10000-12 §7.5 - §7.8).
        /// </summary>
        private void ConfigureDirectoryServices(
            INodeBuilder<CertificateDirectoryState> directory)
        {
            // Services without a self-administration grant: these carry
            // no application id a caller could own, so the handlers apply
            // the plain role checks themselves.
            Method<QueryServersMethodState>(directory, BrowseNames.QueryServers)
                .OnCall = OnQueryServers;
            Method<QueryApplicationsMethodState>(directory, BrowseNames.QueryApplications)
                .OnCall = OnQueryApplications;
            Method<RegisterApplicationMethodState>(directory, BrowseNames.RegisterApplication)
                .OnCall = OnRegisterApplication;
            Method<GetApplicationMethodState>(directory, BrowseNames.GetApplication)
                .OnCall = OnGetApplication;
            Method<RevokeCertificateMethodState>(directory, BrowseNames.RevokeCertificate)
                .OnCallAsync = OnRevokeCertificateAsync;
            Method<CheckRevocationStatusMethodState>(directory, BrowseNames.CheckRevocationStatus)
                .OnCallAsync = OnCheckRevocationStatusAsync;

            // Services an application may invoke for its own record. The
            // permission hooks add the SelfAdmin role while the method's
            // RolePermissions are read, so the stack's access check lets
            // the owning application through before the handler runs.
            SelfAdministered<UpdateApplicationMethodState>(
                    directory, BrowseNames.UpdateApplication)
                .OnCall = OnUpdateApplication;
            SelfAdministered<UnregisterApplicationMethodState>(
                    directory, BrowseNames.UnregisterApplication)
                .OnCallAsync = OnUnregisterApplicationAsync;
            SelfAdministered<FindApplicationsMethodState>(
                    directory, BrowseNames.FindApplications)
                .OnCall = OnFindApplications;
            SelfAdministered<StartNewKeyPairRequestMethodState>(
                    directory, BrowseNames.StartNewKeyPairRequest)
                .OnCall = OnStartNewKeyPairRequest;
            SelfAdministered<StartSigningRequestMethodState>(
                    directory, BrowseNames.StartSigningRequest)
                .OnCallAsync = OnStartSigningRequestAsync;
            SelfAdministered<FinishRequestMethodState>(
                    directory, BrowseNames.FinishRequest)
                .OnCallAsync = OnFinishRequestAsync;
            SelfAdministered<GetCertificateGroupsMethodState>(
                    directory, BrowseNames.GetCertificateGroups)
                .OnCall = OnGetCertificateGroups;
            SelfAdministered<GetTrustListMethodState>(
                    directory, BrowseNames.GetTrustList)
                .OnCall = OnGetTrustList;
            SelfAdministered<GetCertificateStatusMethodState>(
                    directory, BrowseNames.GetCertificateStatus)
                .OnCall = OnGetCertificateStatus;
            SelfAdministered<GetCertificatesMethodState>(
                    directory, BrowseNames.GetCertificates)
                .OnCall = OnGetCertificates;
        }

        /// <summary>
        /// Binds the configured certificate authorities to their nodes and
        /// publishes the state of the three groups the model declares.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        /// <param name="directory">The <c>Directory</c> object.</param>
        private void ConfigureCertificateGroups(
            INodeManagerBuilder builder,
            INodeBuilder<CertificateDirectoryState> directory)
        {
            // Bind before publishing: a group only learns the NodeId it is
            // addressed by here, and the per-group publish below looks the
            // groups up by exactly that id.
            foreach (ICertificateGroup certificateGroup in m_ownedCertificateGroups)
            {
                SetCertificateGroupNodes(builder, certificateGroup);
                m_certificateGroups[certificateGroup.Id] = certificateGroup;
            }

            INodeBuilder<CertificateGroupFolderState> groups =
                directory.Child<CertificateGroupFolderState>(
                    GdsName(BrowseNames.CertificateGroups));

            ConfigureCertificateGroup(
                groups,
                Ua.BrowseNames.DefaultApplicationGroup,
                m_defaultApplicationGroupId,
                Ua.ObjectTypeIds.ApplicationCertificateType);
            ConfigureCertificateGroup(
                groups,
                Ua.BrowseNames.DefaultHttpsGroup,
                m_defaultHttpsGroupId,
                Ua.ObjectTypeIds.HttpsCertificateType);
            ConfigureCertificateGroup(
                groups,
                Ua.BrowseNames.DefaultUserTokenGroup,
                m_defaultUserTokenGroupId,
                Ua.ObjectTypeIds.UserCertificateType);
        }

        /// <summary>
        /// Publishes one predefined certificate group's certificate types
        /// and marks its trust list writeable.
        /// </summary>
        /// <param name="groups">The <c>CertificateGroups</c> folder.</param>
        /// <param name="browseName">Browse name of the group node.</param>
        /// <param name="groupId">
        /// NodeId the configured group registers itself under.
        /// </param>
        /// <param name="fallbackCertificateType">
        /// Concrete certificate type to advertise when the deployment
        /// does not configure this group at all.
        /// </param>
        private void ConfigureCertificateGroup(
            INodeBuilder<CertificateGroupFolderState> groups,
            string browseName,
            NodeId groupId,
            NodeId fallbackCertificateType)
        {
            INodeBuilder<CertificateGroupState> group =
                groups.Child<CertificateGroupState>(new QualifiedName(browseName));

            // OPC 10000-12 §7.8.2 requires CertificateTypes to list the
            // concrete types that can be requested through the group,
            // while the model declares the abstract base type. The
            // configured group knows what it can actually issue; without
            // one, fall back to the concrete type of this group.
            ArrayOf<NodeId> certificateTypes;
            if (m_certificateGroups.TryGetValue(
                groupId,
                out ICertificateGroup? certificateGroup))
            {
                certificateTypes = [.. certificateGroup.CertificateTypes];
            }
            else
            {
                certificateTypes = [fallbackCertificateType];
            }
            group.Node.CertificateTypes!.Value = certificateTypes;

            // OPC 10000-12 §7.8.2.1: a TrustList that supports
            // CloseAndUpdate / AddCertificate / RemoveCertificate is
            // writeable; Writable / UserWritable advertise the capability
            // while the role-based access on the individual methods
            // enforces who may actually mutate the trust list.
            TrustListState trustList = group
                .Child<TrustListState>(new QualifiedName(Ua.BrowseNames.TrustList))
                .Node;
            trustList.LastUpdateTime!.Value = DateTime.UtcNow;
            trustList.Writable!.Value = true;
            trustList.UserWritable!.Value = true;
        }

        /// <summary>
        /// Binds an initialized certificate group to the address space:
        /// its group node, its trust list, and the trust-list handler that
        /// serves the group's certificate stores. Creates the group node
        /// first for a group the model does not predefine.
        /// </summary>
        /// <remarks>
        /// This is where a group learns the NodeId it is addressed by, which
        /// is why it runs in the <c>Configure</c> pass rather than alongside
        /// the group's own startup: for a group the model does not predefine
        /// the id does not exist until the node is staged.
        /// </remarks>
        /// <param name="builder">The active fluent builder.</param>
        /// <param name="certificateGroup">The group to bind.</param>
        protected void SetCertificateGroupNodes(
            INodeManagerBuilder builder,
            ICertificateGroup certificateGroup)
        {
            certificateGroup.DefaultTrustList = null!;
            string groupId = certificateGroup.Configuration.Id!;

            if (string.Equals(groupId, "DefaultHttpsGroup", StringComparison.OrdinalIgnoreCase))
            {
                certificateGroup.Id = m_defaultHttpsGroupId;
                certificateGroup.DefaultTrustList = FindPredefinedNode<TrustListState>(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup_TrustList,
                        Server.NamespaceUris
                    ))!;
            }
            else if (string.Equals(groupId, "DefaultUserTokenGroup", StringComparison.OrdinalIgnoreCase))
            {
                certificateGroup.Id = m_defaultUserTokenGroupId;
                certificateGroup.DefaultTrustList = FindPredefinedNode<TrustListState>(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList,
                        Server.NamespaceUris
                    ))!;
            }
            else if (string.Equals(groupId, "Default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(groupId, "DefaultApplicationGroup", StringComparison.OrdinalIgnoreCase))
            {
                certificateGroup.Id = m_defaultApplicationGroupId;
                certificateGroup.DefaultTrustList = FindPredefinedNode<TrustListState>(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup_TrustList,
                        Server.NamespaceUris
                    ))!;
            }
            else
            {
                // Create a new custom certificate group node in the address space
                // for any group whose Id does not match one of the three predefined groups.
                // The root needs an id of this manager's own before it is
                // staged: Create only overrides the NodeId when it is given a
                // non-null one, so passing NodeId.Null would leave the node
                // holding the namespace-0 id of its type declaration.
                var customGroupNode = new CertificateGroupState(null);
                customGroupNode.Create(
                    SystemContext,
                    new NodeId(
                        BrowseNames.CertificateGroups + "/" + groupId,
                        NamespaceIndex),
                    new QualifiedName(groupId, NamespaceIndex),
                    new LocalizedText(groupId),
                    assignNodeIds: false);

                customGroupNode.CertificateTypes?.Value = [.. certificateGroup.CertificateTypes];

                // Staging the subtree through the builder attaches it to the
                // folder, rebases the declaration ids its children carry —
                // Create ran with assignNodeIds: false — onto per-instance
                // ids, and registers it once this pass returns. The ids are
                // final by the time Add hands the node back, which is what
                // lets the group key off one.
                CertificateGroupState addedGroupNode = builder
                    .Add(
                        customGroupNode,
                        ExpandedNodeId.ToNodeId(
                            ObjectIds.Directory_CertificateGroups,
                            Server.NamespaceUris))
                    .Node;

                certificateGroup.Id = addedGroupNode.NodeId;
                certificateGroup.DefaultTrustList = addedGroupNode.TrustList!;

                m_logger.CreatedCustomCertificateGroupNode(groupId, certificateGroup.Id);
            }

            certificateGroup.DefaultTrustList?.Handle = new TrustList(
                    certificateGroup.DefaultTrustList,
                    new CertificateStoreIdentifier(certificateGroup.Configuration.TrustedListPath!),
                    new CertificateStoreIdentifier(certificateGroup.Configuration.IssuerListPath!),
                    new TrustList.SecureAccess(HasTrustListAccess),
                    new TrustList.SecureAccess(HasTrustListAccess),
                    Server.Telemetry);
        }

        /// <summary>
        /// Materialises the <c>Default</c> authorization service the model's
        /// <c>AuthorizationServices</c> folder ships empty, and hands it back
        /// for wiring.
        /// </summary>
        /// <remarks>
        /// Creating it through the builder rather than registering it by hand
        /// is what lets creation and wiring live together in <c>Configure</c>:
        /// the node is staged, its NodeIds are final by the time the builder
        /// returns, and it is registered after the pass. The declaration ids
        /// its children carry — <c>Create</c> is called with
        /// <c>assignNodeIds: false</c> — are rebased by the same staging pass.
        /// </remarks>
        /// <param name="builder">The active fluent builder.</param>
        /// <returns>The service to wire, existing or newly created.</returns>
        private AuthorizationServiceState EnsureDefaultAuthorizationService(
            INodeManagerBuilder builder)
        {
            ushort namespaceIndex = GdsNamespaceIndex;
            var folderId = new NodeId(Objects.AuthorizationServices, namespaceIndex);
            var browseName = new QualifiedName(DefaultAuthorizationServiceName, namespaceIndex);

            // A deployment may have contributed its own Default already.
            if (FindPredefinedNode<BaseObjectState>(folderId)?
                    .FindChild(SystemContext, browseName) is AuthorizationServiceState existing)
            {
                return existing;
            }

            AuthorizationServiceState service = CreateDefaultAuthorizationService(
                null,
                SystemContext,
                namespaceIndex,
                browseName);

            return builder.Add(service, folderId).Node;
        }

        private AuthorizationServiceState CreateDefaultAuthorizationService(
            NodeState? folder,
            ISystemContext context,
            ushort namespaceIndex,
            QualifiedName browseName)
        {
            var service = new AuthorizationServiceState(folder);

            service.Create(
                context,
                new NodeId("AuthorizationServices/Default", namespaceIndex),
                browseName,
                new LocalizedText("Default"),
                false);

            // ServiceUri, ServiceCertificate and the mandatory GetServiceDescription method
            // are created automatically by the source-generated AuthorizationServiceState.
            // The Optional method children must be added explicitly using the generated
            // Add* helpers; the Configure pass wires their OnCall handlers afterwards.
            service
                .AddRequestAccessToken(context)
                .AddStartRequestToken(context)
                .AddFinishRequestToken(context)
                .AddRefreshToken(context);

            service.ServiceUri!.Value = m_configuration.ApplicationUri ?? string.Empty;
            service.ServiceCertificate!.Value = ByteString.Empty;
            service.UserTokenPolicies?.Value = m_configuration.ServerConfiguration?.UserTokenPolicies ?? default;

            return service;
        }

        /// <summary>
        /// Wires an <c>AuthorizationService</c> object (OPC 10000-12 §7.10).
        /// The GDS calls this from its <c>Configure</c> pass for the
        /// <c>Default</c> service it materialises itself; a host that adds
        /// further services to the folder at runtime calls it for each.
        /// </summary>
        /// <param name="authServiceNode">The service object to wire.</param>
        protected void ConfigureAuthorizationService(AuthorizationServiceState authServiceNode)
        {
            if (authServiceNode == null)
            {
                throw new ArgumentNullException(nameof(authServiceNode));
            }

            authServiceNode.GetServiceDescription!.OnCall = OnGetServiceDescription;
            authServiceNode.RequestAccessToken?.OnCallAsync = OnRequestAccessTokenAsync;
            authServiceNode.StartRequestToken?.OnCallAsync = OnStartRequestTokenAsync;
            authServiceNode.FinishRequestToken?.OnCallAsync = OnFinishRequestTokenAsync;
            authServiceNode.RefreshToken?.OnCallAsync = OnRefreshTokenAsync;
        }

        /// <summary>
        /// Wires a <c>KeyCredentialService</c> object contributed by the
        /// host (OPC 10000-12 §7.9). Unlike the <c>Directory</c>, these
        /// instances are not part of the companion model, so they are
        /// wired as they are registered rather than from
        /// <see cref="OnConfigure"/>.
        /// </summary>
        /// <param name="service">The service object to wire.</param>
        protected void ConfigureKeyCredentialService(KeyCredentialServiceState service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            NodeManagerBuilder builder = CreateFluentBuilder(GdsNamespaceIndex);
            ConfigureKeyCredentialService(builder, service);
            builder.Seal();
        }

        private void ConfigureKeyCredentialService(
            INodeManagerBuilder builder,
            KeyCredentialServiceState service)
        {
            // As on the Directory, the self-administration grant is what
            // lets an application manage its own credentials.
            SelfAdministered(builder.Node(service.StartRequest!))
                .OnCallAsync = OnKeyCredentialStartRequestAsync;
            SelfAdministered(builder.Node(service.FinishRequest!))
                .OnCallAsync = OnKeyCredentialFinishRequestAsync;

            // Revoke is an optional child of KeyCredentialServiceType.
            if (service.Revoke != null)
            {
                SelfAdministered(builder.Node(service.Revoke))
                    .OnCallAsync = OnKeyCredentialRevokeAsync;
            }
        }

        /// <summary>
        /// Resolves a method of the <c>Directory</c> object by browse name.
        /// </summary>
        /// <typeparam name="TMethod">
        /// The generated method state the child must be assignable to.
        /// </typeparam>
        private TMethod Method<TMethod>(
            INodeBuilder<CertificateDirectoryState> directory,
            string browseName)
            where TMethod : MethodState
        {
            return directory.Child<TMethod>(GdsName(browseName)).Node;
        }

        /// <summary>
        /// As <see cref="Method{TMethod}"/>, and additionally grants the
        /// SelfAdmin role on the resolved method so an application can
        /// invoke it for its own record.
        /// </summary>
        /// <typeparam name="TMethod">
        /// The generated method state the child must be assignable to.
        /// </typeparam>
        private TMethod SelfAdministered<TMethod>(
            INodeBuilder<CertificateDirectoryState> directory,
            string browseName)
            where TMethod : MethodState
        {
            return SelfAdministered(directory.Child<TMethod>(GdsName(browseName)));
        }

        /// <summary>
        /// Grants the SelfAdmin role on an already-resolved method.
        /// </summary>
        /// <typeparam name="TMethod">The method's state type.</typeparam>
        private TMethod SelfAdministered<TMethod>(INodeBuilder<TMethod> method)
            where TMethod : MethodState
        {
            return method
                .OnReadRolePermissions(OnAddSelfAdminRolePermissions)
                .OnReadUserRolePermissions(OnAddSelfAdminUserRolePermissions)
                .Node;
        }

        /// <summary>
        /// Qualifies a browse name of the GDS companion model.
        /// </summary>
        private QualifiedName GdsName(string browseName)
        {
            return new QualifiedName(browseName, GdsNamespaceIndex);
        }

        /// <summary>
        /// The index of the GDS companion model namespace
        /// (<c>http://opcfoundation.org/UA/GDS/</c>) this manager owns.
        /// The application record namespace is
        /// <c>NamespaceIndexes[0]</c>; every node of the loaded model
        /// lives in this one.
        /// </summary>
        protected ushort GdsNamespaceIndex => NamespaceIndexes[1];

        private NodeId GetTrustListId(NodeId certificateGroupId)
        {
            if (certificateGroupId.IsNull)
            {
                certificateGroupId = m_defaultApplicationGroupId;
            }

            if (m_certificateGroups.TryGetValue(
                certificateGroupId,
                out ICertificateGroup? certificateGroup))
            {
                return certificateGroup.DefaultTrustList?.NodeId ?? default;
            }

            return default;
        }

        private bool? GetCertificateStatus(NodeId certificateGroupId, NodeId certificateTypeId)
        {
            if (m_certificateGroups.TryGetValue(
                certificateGroupId,
                out ICertificateGroup? certificateGroup))
            {
                if (!certificateTypeId.IsNull &&
                    !certificateGroup.CertificateTypes.Contains(certificateTypeId))
                {
                    return null;
                }
                return certificateGroup.UpdateRequired;
            }

            return null;
        }

        private ICertificateGroup? GetGroupForCertificate(ByteString certificate)
        {
            if (certificate.Length > 0)
            {
                using var x509 = Certificate.FromRawData(certificate);
                NodeId certificateType = CertificateIdentifier.GetCertificateType(x509);
                foreach (ICertificateGroup certificateGroup in m_certificateGroups.Values)
                {
                    KeyValuePair<NodeId, Certificate?> matchingCert = certificateGroup
                        .Certificates
                        .FirstOrDefault(
                            kvp =>
                                X509Utils.CompareDistinguishedName(
                                    kvp.Value!.Subject,
                                    x509.Issuer) &&
                                kvp.Key == certificateType);

                    if (matchingCert.Value != null)
                    {
                        return certificateGroup;
                    }
                }
            }

            return null;
        }

        private async Task<bool> RevokeCertificateAsync(
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            bool revoked = false;
            if (certificate.Length > 0)
            {
                ICertificateGroup? certificateGroup = GetGroupForCertificate(certificate);

                if (certificateGroup != null)
                {
                    using var x509 = Certificate.FromRawData(certificate);
                    try
                    {
                        X509CRL crl = await certificateGroup
                            .RevokeCertificateAsync(x509, cancellationToken)
                            .ConfigureAwait(false);
                        if (crl != null)
                        {
                            revoked = true;
                        }
                    }
                    catch (Exception e)
                    {
                        m_logger.UnexpectedErrorRevokingCertificate(e, x509.Subject, certificateGroup.Id);
                    }
                }
            }
            return revoked;
        }

        /// <summary>
        /// Builds the issuer-certificate chain for a newly issued
        /// <paramref name="certificate"/> per OPC 10000-12 §7.6.6.
        /// </summary>
        /// <remarks>
        /// The chain is returned in leaf-to-root order, excluding the leaf
        /// itself. The immediate issuing CA is always included. The
        /// in-memory <see cref="ICertificateGroup.Certificates"/> map is
        /// used as the additional chain-building candidate set so the
        /// helper does not contend with concurrent
        /// <c>SigningRequestAsync</c> / <c>NewKeyPairRequestAsync</c>
        /// writes against the CertificateGroup's AuthoritiesStore.
        /// </remarks>
#pragma warning disable CA1822 // method uses instance fields via the captured certificateGroup parameter
        private ArrayOf<ByteString> BuildIssuerCertificateChain(
            Certificate certificate,
            ICertificateGroup certificateGroup,
            NodeId certificateTypeNodeId)
#pragma warning restore CA1822
        {
            var issuerChain = new List<ByteString>();
            var clones = new List<X509Certificate2>();

            try
            {
                var candidates = new X509Certificate2Collection();

                // Use the in-memory CA cert(s) the CertificateGroup
                // already exposes; clone the byte arrays so the chain
                // build holds independent X509Certificate2 handles.
                foreach (KeyValuePair<NodeId, Certificate?> kvp in certificateGroup.Certificates)
                {
                    if (kvp.Value == null)
                    {
                        continue;
                    }
                    X509Certificate2 clone = X509CertificateLoader.LoadCertificate(kvp.Value.RawData);
                    clones.Add(clone);
                    candidates.Add(clone);
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags =
                    X509VerificationFlags.AllowUnknownCertificateAuthority |
                    X509VerificationFlags.IgnoreEndRevocationUnknown |
                    X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                    X509VerificationFlags.IgnoreCtlNotTimeValid |
                    X509VerificationFlags.IgnoreRootRevocationUnknown;
                chain.ChainPolicy.ExtraStore.AddRange(candidates);

                using X509Certificate2 leaf = certificate.AsX509Certificate2();
                if (chain.Build(leaf))
                {
                    // ChainElements is leaf-first; skip the leaf and emit
                    // the remaining issuers in order (immediate issuer
                    // first).
                    for (int i = 1; i < chain.ChainElements.Count; i++)
                    {
                        byte[] raw = chain.ChainElements[i].Certificate.RawData;
                        issuerChain.Add(ByteString.From(raw));
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.FailedToBuildIssuerChain(ex, certificate.Subject);
            }
            finally
            {
                foreach (X509Certificate2 clone in clones)
                {
                    clone.Dispose();
                }
            }

            // Always ensure the immediate issuing CA is included even if
            // chain building did not produce one (e.g. AKI mismatch).
            if (issuerChain.Count == 0 &&
                certificateGroup.Certificates.TryGetValue(
                    certificateTypeNodeId,
                    out Certificate? issuingCa) &&
                issuingCa != null)
            {
                issuerChain.Add(ByteString.From(issuingCa.RawData));
            }

            return [.. issuerChain];
        }

        /// <summary>
        /// Brings up one configured certificate authority: creates the group
        /// and opens the certificate stores it serves from.
        /// </summary>
        /// <remarks>
        /// Only the I/O belongs here. Binding the group to its address-space
        /// nodes is synchronous work that happens in the <c>Configure</c>
        /// pass; see <see cref="SetCertificateGroupNodes"/>.
        /// </remarks>
        /// <param name="certificateGroupConfiguration">
        /// The configured group to bring up.
        /// </param>
        /// <returns>The initialized group, already owned by this manager.</returns>
        protected async Task<ICertificateGroup> InitializeCertificateGroupAsync(
            CertificateGroupConfiguration certificateGroupConfiguration)
        {
            if (string.IsNullOrEmpty(certificateGroupConfiguration.SubjectName))
            {
                throw new ArgumentNullException(
                    nameof(certificateGroupConfiguration),
                    "SubjectName not specified");
            }

            if (string.IsNullOrEmpty(certificateGroupConfiguration.BaseStorePath))
            {
                throw new ArgumentNullException(
                    nameof(certificateGroupConfiguration),
                    "BaseStorePath not specified");
            }

            ICertificateGroup certificateGroup = m_certificateGroupFactory.Create(
                m_globalDiscoveryServerConfiguration.AuthoritiesStorePath!,
                certificateGroupConfiguration,
                m_configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath);

            // Take ownership before the first call that can throw: InitAsync
            // opens certificate stores, so a group that fails half way through
            // still has to reach Dispose.
            m_ownedCertificateGroups.Add(certificateGroup);

            await certificateGroup.InitAsync().ConfigureAwait(false);

            return certificateGroup;
        }

        /// <summary>
        /// Does the asynchronous part of startup, which is what this hook
        /// exists for: bringing up the certificate authorities, whose
        /// stores and CA certificates are real I/O.
        /// </summary>
        /// <remarks>
        /// Nothing that touches the address space belongs here. The groups
        /// this pass brings up are bound to their nodes by <c>Configure</c>,
        /// which runs next and is where the node ids they key off are
        /// decided.
        /// </remarks>
        protected override async ValueTask OnAddressSpaceReadyAsync(
            CancellationToken cancellationToken)
        {
            m_certTypeMap = new Dictionary<NodeId, string>
            {
                // list of supported cert type mappings (V1.04)
                {
                    Ua.ObjectTypeIds.HttpsCertificateType,
                    nameof(Ua.ObjectTypeIds.HttpsCertificateType)
                },
                {
                    Ua.ObjectTypeIds.UserCertificateType,
                    nameof(Ua.ObjectTypeIds.UserCertificateType)
                },
                {
                    Ua.ObjectTypeIds.ApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.ApplicationCertificateType)
                },
                {
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.RsaMinApplicationCertificateType)
                },
                {
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)
                },
                {
                    Ua.ObjectTypeIds.EccApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.EccApplicationCertificateType)
                },
                {
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType)
                },
                {
                    Ua.ObjectTypeIds.EccNistP384ApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.EccNistP384ApplicationCertificateType)
                },
                {
                    Ua.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
                },
                {
                    Ua.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
#if CURVE25519
                },
                {
                    Ua.ObjectTypeIds.EccCurve25519ApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.EccCurve25519ApplicationCertificateType)
                },
                {
                    Ua.ObjectTypeIds.EccCurve448ApplicationCertificateType,
                    nameof(Ua.ObjectTypeIds.EccCurve448ApplicationCertificateType)
#endif
                }
            };

            m_database.NamespaceIndex = NamespaceIndexes[0];
            m_request.NamespaceIndex = NamespaceIndexes[0];

            foreach (
                CertificateGroupConfiguration certificateGroupConfiguration in m_globalDiscoveryServerConfiguration
                    .CertificateGroups.ToList())
            {
                try
                {
                    await InitializeCertificateGroupAsync(certificateGroupConfiguration)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    m_logger.UnexpectedErrorInitializingCertificateGroup(e, certificateGroupConfiguration.Id);
                    // make sure gds server doesn't start without cert groups!
                    throw;
                }
            }
        }

        private ServiceResult OnAddSelfAdminRolePermissions(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            if (value.IsEmpty)
            {
                return ServiceResult.Good;
            }
            return AddSelfAdminRolePermission(context, ref value);
        }

        private ServiceResult OnAddSelfAdminUserRolePermissions(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            var selfAdminRole = ExpandedNodeId.ToNodeId(
                GdsRole.ApplicationSelfAdmin.RoleId,
                context.NamespaceUris);
            IUserIdentity? userIdentity = (context as ISessionSystemContext)?.UserIdentity;

            if (userIdentity == null ||
                !userIdentity.GrantedRoleIds.Contains(selfAdminRole))
            {
                return ServiceResult.Good;
            }

            // This contains the self admin role and other permissions
            return AddSelfAdminRolePermission(context, ref value);
        }

        private static ServiceResult AddSelfAdminRolePermission(
            ISystemContext context,
            ref ArrayOf<RolePermissionType> value)
        {
            var selfAdminRole = ExpandedNodeId.ToNodeId(
                GdsRole.ApplicationSelfAdmin.RoleId,
                context.NamespaceUris);
            var selfAdminPermission = new RolePermissionType
            {
                RoleId = selfAdminRole,
                Permissions = (uint)PermissionType.Call
            };
            value = ArrayOf.Combine(value, [selfAdminPermission]);
            return ServiceResult.Good;
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
            ArrayOf<string> serverCapabilities,
            ref DateTimeUtc lastCounterResetTime,
            ref ArrayOf<ServerOnNetwork> servers)
        {
            m_logger.QueryServers(applicationUri, applicationName);

            servers = m_database.QueryServers(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities,
                out lastCounterResetTime)!;

            return ServiceResult.Good;
        }

        private ServiceResult OnQueryApplications(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            ArrayOf<string> serverCapabilities,
            ref DateTimeUtc lastCounterResetTime,
            ref uint nextRecordId,
            ref ArrayOf<ApplicationDescription> applications)
        {
            m_logger.QueryApplications(applicationUri, applicationName);

            applications = m_database.QueryApplications(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                applicationType,
                productUri,
                serverCapabilities,
                out lastCounterResetTime,
                out nextRecordId)!;
            return ServiceResult.Good;
        }

        private ServiceResult OnRegisterApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ApplicationRecordDataType application,
            ref NodeId applicationId)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.DiscoveryAdminOrAppAdmin);

            m_logger.OnRegisterApplication(application.ApplicationUri);

            try
            {
                applicationId = m_database.RegisterApplication(application);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex);
            }

            if (!applicationId.IsNull)
            {
                ArrayOf<Variant> inputArguments = [Variant.FromStructure(application), applicationId];
                Server.ReportApplicationRegistrationChangedAuditEvent(
                    context,
                    objectId,
                    method,
                    inputArguments,
                    m_logger);
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnUpdateApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ApplicationRecordDataType application)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                application.ApplicationId);

            m_logger.OnUpdateApplication(application.ApplicationUri);

            ApplicationRecordDataType? record = m_database.GetApplication(application.ApplicationId);

            if (record == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The application id does not exist."));
            }

            try
            {
                m_database.UpdateApplication(application);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex);
            }

            ArrayOf<Variant> inputArguments = [Variant.FromStructure(application)];
            Server.ReportApplicationRegistrationChangedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);

            return ServiceResult.Good;
        }

        private async ValueTask<UnregisterApplicationMethodStateResult>
            OnUnregisterApplicationAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                applicationId);

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.OnUnregisterApplication(applicationId.ToString());
            }

            if (m_database.GetApplication(applicationId) == null)
            {
                return new UnregisterApplicationMethodStateResult
                {
                    ServiceResult = new ServiceResult(
                        StatusCodes.BadNotFound,
                        LocalizedText.From("The application id does not exist."))
                };
            }

            foreach (KeyValuePair<NodeId, string> certType in m_certTypeMap)
            {
                try
                {
                    if (m_database.GetApplicationCertificate(
                            applicationId,
                            certType.Value,
                            out ByteString certificate) &&
                        !certificate.IsEmpty)
                    {
                        await RevokeCertificateAsync(certificate, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRevoke(ex, certType.Value);
                }
            }

            m_database.UnregisterApplication(applicationId);

            ArrayOf<Variant> inputArguments = [applicationId];
            Server.ReportApplicationRegistrationChangedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);

            return new UnregisterApplicationMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            };
        }

        private async ValueTask<RevokeCertificateMethodStateResult> OnRevokeCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ByteString certificate,
            CancellationToken cancellationToken)
        {
            // Per OPC 10000-12 §7.6.9 the CertificateRevokedAuditEvent shall
            // be generated on success or failure. The InputArguments are
            // (applicationId, certificate) per the method signature; the
            // certificate is a public ByteString so no redaction is needed.
            var result = new RevokeCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            };
            Exception? auditException = null;

            try
            {
                AuthorizationHelper.HasAuthorization(context, AuthorizationHelper.CertificateAuthorityAdmin);

                if (m_database.GetApplication(applicationId) == null)
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadNotFound,
                        LocalizedText.From("The ApplicationId does not refer to a registered application."));
                    return result;
                }
                if (certificate.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "The certificate is not a Certificate for the specified Application that was issued by the CertificateManager.");
                }

                bool revoked = false;
                foreach (KeyValuePair<NodeId, string> certType in m_certTypeMap)
                {
                    if (!m_database.GetApplicationCertificate(
                            applicationId,
                            certType.Value,
                            out ByteString applicationCertificate) ||
                        applicationCertificate.IsEmpty ||
                        !Utils.IsEqual(applicationCertificate, certificate))
                    {
                        continue;
                    }

                    revoked = await RevokeCertificateAsync(
                        certificate,
                        cancellationToken).ConfigureAwait(false);
                    if (revoked)
                    {
                        break;
                    }
                }
                if (!revoked)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "The certificate is not a Certificate for the specified Application that was issued by the CertificateManager.");
                }
                result.ServiceResult = ServiceResult.Good;
                return result;
            }
            catch (Exception ex)
            {
                auditException = ex;
                throw;
            }
            finally
            {
                if (auditException == null &&
                    result.ServiceResult != null &&
                    StatusCode.IsBad(result.ServiceResult.StatusCode))
                {
                    auditException = new ServiceResultException(result.ServiceResult);
                }

                ArrayOf<Variant> auditInputs = [applicationId, certificate];
                Server.ReportCertificateRevokedAuditEvent(
                    context,
                    objectId,
                    method,
                    auditInputs,
                    m_logger,
                    auditException);
            }
        }

        private ServiceResult OnFindApplications(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string applicationUri,
            ref ArrayOf<ApplicationRecordDataType> applications)
        {
            AuthorizationHelper.HasAuthorization(context, AuthorizationHelper.AuthenticatedUser);
            m_logger.OnFindApplications(applicationUri);
            applications = m_database.FindApplications(applicationUri) ?? [];
            return ServiceResult.Good;
        }

        private ServiceResult OnGetApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ref ApplicationRecordDataType application)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.AuthenticatedUserOrSelfAdmin,
                applicationId);
            m_logger.OnGetApplication(applicationId);
            try
            {
                application = m_database.GetApplication(applicationId)
                    ?? throw new ServiceResultException(StatusCodes.BadNotFound);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex);
            }
            return ServiceResult.Good;
        }

        private async ValueTask<CheckRevocationStatusMethodStateResult> OnCheckRevocationStatusAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ByteString certificate,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context);

            var result = new CheckRevocationStatusMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ValidityTime = DateTime.MinValue
            };

            // Per OPC 10000-12 §7.6.11, ValidityTime indicates when the
            // CertificateStatus result expires and should be rechecked.
            // We compute it as the earliest NextUpdate across the CRLs in
            // the trusted-issuer store after the chain has validated; for
            // Bad results the field remains DateTime.MinValue so callers
            // re-check immediately.
            DateTime computedValidityTime = DateTime.MinValue;

            try
            {
                //create chain to validate Certificate against it
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;

                //add GDS Issuer Cert Store Certificates to the Chain validation for consistent behaviour on all Platforms
                using ICertificateStore store = m_configuration.SecurityConfiguration
                    .TrustedIssuerCertificates
                    .OpenStore(Server.Telemetry);
                if (store != null)
                {
                    try
                    {
                        using CertificateCollection issuerCerts = await store
                            .EnumerateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        chain.ChainPolicy.ExtraStore
                            .AddRange(issuerCerts.AsX509Certificate2Collection());

                        X509CRLCollection crls = await store
                            .EnumerateCRLsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        DateTime nextUpdate = DateTime.MaxValue;
                        foreach (X509CRL crl in crls)
                        {
                            if (crl.NextUpdate != DateTime.MinValue &&
                                crl.NextUpdate < nextUpdate)
                            {
                                nextUpdate = crl.NextUpdate;
                            }
                        }
                        if (nextUpdate != DateTime.MaxValue)
                        {
                            computedValidityTime = nextUpdate;
                        }
                    }
                    finally
                    {
                        store.Close();
                    }
                }

                using var x509 = Certificate.FromRawData(certificate);
                using X509Certificate2 x509Cert = x509.AsX509Certificate2();
                if (chain.Build(x509Cert))
                {
                    result.CertificateStatus = StatusCodes.Good;
                    result.ValidityTime = computedValidityTime;
                    return result;
                }

                // Assessing certificateStatus for invalid chain
                X509ChainStatusFlags status = chain.ChainStatus.FirstOrDefault().Status;
                if ((status & X509ChainStatusFlags.NotTimeValid) ==
                    X509ChainStatusFlags.NotTimeValid)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateTimeInvalid;
                }
                else if ((status & X509ChainStatusFlags.Revoked) ==
                    X509ChainStatusFlags.Revoked)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateRevoked;
                }
                else if ((status & X509ChainStatusFlags.NotSignatureValid) ==
                    X509ChainStatusFlags.NotSignatureValid)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateInvalid;
                }
                else if ((status & X509ChainStatusFlags.NotValidForUsage) ==
                    X509ChainStatusFlags.NotValidForUsage)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateUseNotAllowed;
                }
                else if ((status & X509ChainStatusFlags.RevocationStatusUnknown) ==
                    X509ChainStatusFlags.RevocationStatusUnknown)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateRevocationUnknown;
                }
                else if ((status & X509ChainStatusFlags.PartialChain) ==
                    X509ChainStatusFlags.PartialChain)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateChainIncomplete;
                }
                else if ((status & X509ChainStatusFlags.ExplicitDistrust) ==
                    X509ChainStatusFlags.ExplicitDistrust)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateUntrusted;
                }
                else
                {
                    // If no matching found use StatusCodes.BadCertificateRevoked
                    // Even though this is a no error = 0 case, the chain is invalid
                    result.CertificateStatus = StatusCodes.BadCertificateRevoked;
                }
            }
            catch (CryptographicException)
            {
                result.CertificateStatus = StatusCodes.BadCertificateRevoked;
            }

            return result;
        }

        private ServiceResult OnGetCertificates(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            ref ArrayOf<NodeId> certificateTypeIds,
            ref ArrayOf<ByteString> certificates)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            var certificateTypeIdsList = new List<NodeId>();
            var certificatesList = new List<ByteString>();

            if (m_database.GetApplication(applicationId) == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a registered application."));
            }

            // If CertificateGroupId is null, the CertificateManager shall return the Certificates
            // for all CertificateGroups assigned to the Application.
            if (certificateGroupId.IsNull)
            {
                foreach (KeyValuePair<NodeId, string> certType in m_certTypeMap)
                {
                    if (m_database.GetApplicationCertificate(
                            applicationId,
                            certType.Value,
                            out ByteString certificate) &&
                        !certificate.IsEmpty)
                    {
                        certificateTypeIdsList.Add(certType.Key);
                        certificatesList.Add(certificate);
                    }
                }
            }
            // get only Certificate of the provided CertificateGroup
            else
            {
                if (!m_certificateGroups.TryGetValue(
                    certificateGroupId,
                    out ICertificateGroup? certificateGroup))
                {
                    return new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From(
                            "The CertificateGroupId is not recognized or not valid for the Application."));
                }
                foreach (NodeId certificateType in certificateGroup.CertificateTypes)
                {
                    if (m_certTypeMap.TryGetValue(certificateType, out string? certificateTypeId) &&
                        m_database.GetApplicationCertificate(
                            applicationId,
                            certificateTypeId,
                            out ByteString certificate
                        ) &&
                        !certificate.IsEmpty)
                    {
                        certificateTypeIdsList.Add(certificateType);
                        certificatesList.Add(certificate);
                    }
                }
            }

            certificates = [.. certificatesList];
            certificateTypeIds = [.. certificateTypeIdsList];

            return ServiceResult.Good;
        }

        private static ServiceResult CheckHttpsDomain(
            ApplicationRecordDataType application,
            string commonName)
        {
            if (application.ApplicationType == ApplicationType.Client)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    LocalizedText.From("Cannot issue HTTPS certificates to client applications."));
            }

            bool found = false;

            foreach (string discoveryUrl in application.DiscoveryUrls)
            {
                if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                {
                    var url = new Uri(discoveryUrl);

                    if (url.Scheme == Utils.UriSchemeHttps &&
                        Utils.AreDomainsEqual(commonName, url.IdnHost))
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    LocalizedText.From("Cannot issue HTTPS certificates to server applications without a matching HTTPS discovery URL."));
            }

            return ServiceResult.Good;
        }

        private static string GetDefaultHttpsDomain(ApplicationRecordDataType application)
        {
            foreach (string discoveryUrl in application.DiscoveryUrls)
            {
                if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                {
                    var url = new Uri(discoveryUrl);

                    if (url.Scheme == Utils.UriSchemeHttps)
                    {
                        return url.IdnHost;
                    }
                }
            }

            throw new ServiceResultException(
                StatusCodes.BadInvalidArgument,
                LocalizedText.From("Cannot issue HTTPS certificates to server applications without a HTTPS discovery URL."));
        }

        private static string GetDefaultUserToken()
        {
            return "USER";
        }

        private string GetSubjectName(
            ApplicationRecordDataType application,
            ICertificateGroup certificateGroup,
            string subjectName)
        {
            bool contextFound = false;

            List<string> fields = X509Utils.ParseDistinguishedName(subjectName);

            var builder = new StringBuilder();

            foreach (string field in fields)
            {
                if (builder.Length > 0)
                {
                    builder.Append(',');
                }

                if (field.StartsWith("CN=", StringComparison.Ordinal) &&
                    certificateGroup.Id == m_defaultHttpsGroupId)
                {
                    ServiceResult error = CheckHttpsDomain(application, field[3..]);

                    if (StatusCode.IsBad(error.StatusCode))
                    {
                        builder.Append("CN=")
                            .Append(GetDefaultHttpsDomain(application));
                        continue;
                    }
                }

                contextFound |=
                    field.StartsWith("DC=", StringComparison.Ordinal) ||
                    field.StartsWith("O=", StringComparison.Ordinal);

                builder.Append(field);
            }

            if (!contextFound &&
                !string.IsNullOrEmpty(
                    m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext))
            {
                builder.Append(m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext);
            }

            return builder.ToString();
        }

        private static string[] GetDefaultDomainNames(ApplicationRecordDataType application)
        {
            if (application.DiscoveryUrls.IsEmpty)
            {
                return [];
            }
            var names = new List<string>();
            foreach (string discoveryUrl in application.DiscoveryUrls)
            {
                if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                {
                    var url = new Uri(discoveryUrl);

                    foreach (string name in names)
                    {
                        if (Utils.AreDomainsEqual(name, url.IdnHost))
                        {
                            url = null;
                            break;
                        }
                    }

                    if (url != null)
                    {
                        names.Add(url.IdnHost);
                    }
                }
            }
            return [.. names];
        }

        private ServiceResult OnStartNewKeyPairRequest(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> domainNames,
            string privateKeyFormat,
            string privateKeyPassword,
            ref NodeId requestId)
        {
            // Per OPC 10000-12 §7.6.4 / §7.9.3, CertificateRequestedAuditEvent
            // is emitted after the method outcome is known so Status reflects
            // success or failure. The privateKeyPassword input is redacted to
            // avoid leaking secrets into audit payloads.
            ServiceResult result = ServiceResult.Good;
            Exception? auditException = null;
            NodeId resolvedGroupId = certificateGroupId;
            NodeId resolvedTypeId = certificateTypeId;

            try
            {
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                    applicationId);

                ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

                if (application == null)
                {
                    return result = new ServiceResult(
                        StatusCodes.BadNotFound,
                        LocalizedText.From("The ApplicationId does not refer to a valid application."));
                }

                if (resolvedGroupId.IsNull)
                {
                    resolvedGroupId = ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                        Server.NamespaceUris);
                }

                if (!m_certificateGroups.TryGetValue(
                    resolvedGroupId,
                    out ICertificateGroup? certificateGroup))
                {
                    return result = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The certificateGroup is not supported."));
                }

                if (!resolvedTypeId.IsNull)
                {
                    if (!certificateGroup.CertificateTypes.Contains(certificateType =>
                            Server.TypeTree.IsTypeOf(certificateType, resolvedTypeId)))
                    {
                        return result = new ServiceResult(
                            StatusCodes.BadInvalidArgument,
                            LocalizedText.From("The CertificateType is not supported by the certificateGroup."));
                    }
                }
                else
                {
                    resolvedTypeId = certificateGroup.CertificateTypes[0];
                }

                if (!m_certTypeMap.TryGetValue(resolvedTypeId, out string? certificateTypeNameId))
                {
                    return result = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The CertificateType is invalid."));
                }

                if (!string.IsNullOrEmpty(subjectName))
                {
                    subjectName = GetSubjectName(application, certificateGroup, subjectName);
                }
                else
                {
                    var buffer = new StringBuilder();

                    buffer.Append("CN=");

                    if ((certificateGroup.Id.IsNull ||
                        (certificateGroup.Id == m_defaultApplicationGroupId)) &&
                        (application.ApplicationNames.Count > 0))
                    {
                        buffer.Append(application.ApplicationNames[0]);
                    }
                    else if (certificateGroup.Id == m_defaultHttpsGroupId)
                    {
                        buffer.Append(GetDefaultHttpsDomain(application));
                    }
                    else if (certificateGroup.Id == m_defaultUserTokenGroupId)
                    {
                        buffer.Append(GetDefaultUserToken());
                    }

                    if (!string.IsNullOrEmpty(
                        m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext))
                    {
                        buffer.Append(m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext);
                    }

                    subjectName = buffer.ToString();
                }

                if (domainNames.Count > 0)
                {
                    foreach (string domainName in domainNames)
                    {
                        if (Uri.CheckHostName(domainName) == UriHostNameType.Unknown)
                        {
                            return result = ServiceResult.Create(
                                StatusCodes.BadInvalidArgument,
                                "The domainName ({0}) is not a valid DNS Name or IPAddress.",
                                domainName);
                        }
                    }
                }
                else
                {
                    domainNames = GetDefaultDomainNames(application);
                }

                IUserIdentity? userIdentity = (context as ISessionSystemContext)?.UserIdentity;
                requestId = m_request.StartNewKeyPairRequest(
                    applicationId,
                    certificateGroup.Configuration.Id!,
                    certificateTypeNameId,
                    subjectName,
                    domainNames,
                    privateKeyFormat,
                    privateKeyPassword?.ToCharArray()!,
                    userIdentity?.DisplayName!);

                if (m_autoApprove)
                {
                    try
                    {
                        m_request.ApproveRequest(requestId, false);
                    }
                    catch
                    {
                        // ignore error as user may not have authorization to approve requests
                    }
                }

                return result = ServiceResult.Good;
            }
            catch (Exception ex)
            {
                auditException = ex;
                throw;
            }
            finally
            {
                if (auditException == null && result != null && StatusCode.IsBad(result.StatusCode))
                {
                    auditException = new ServiceResultException(result);
                }

                ArrayOf<Variant> auditInputs =
                [
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    domainNames,
                    privateKeyFormat,
                    Diagnostics.AuditEvents.RedactedPrivateKeyPassword
                ];
                Server.ReportCertificateRequestedAuditEvent(
                    context,
                    objectId,
                    method,
                    auditInputs,
                    resolvedGroupId,
                    resolvedTypeId,
                    m_logger,
                    auditException);
            }
        }

        private async ValueTask<StartSigningRequestMethodStateResult> OnStartSigningRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificateRequest,
            CancellationToken cancellationToken)
        {
            // Per OPC 10000-12 §7.6.4 / §7.9.3, CertificateRequestedAuditEvent
            // is emitted after the method outcome is known so Status reflects
            // success or failure.
            var result = new StartSigningRequestMethodStateResult();
            Exception? auditException = null;
            NodeId resolvedGroupId = certificateGroupId;
            NodeId resolvedTypeId = certificateTypeId;

            try
            {
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                    applicationId);

                ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

                if (application == null)
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadNotFound,
                        LocalizedText.From("The ApplicationId does not refer to a valid application."));
                    return result;
                }

                if (resolvedGroupId.IsNull)
                {
                    resolvedGroupId = ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                        Server.NamespaceUris);
                }

                if (!m_certificateGroups.TryGetValue(
                    resolvedGroupId,
                    out ICertificateGroup? certificateGroup))
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The CertificateGroupId does not refer to a supported certificateGroup."));
                    return result;
                }

                if (!resolvedTypeId.IsNull)
                {
                    if (!certificateGroup.CertificateTypes.Contains(certificateType =>
                            Server.TypeTree.IsTypeOf(certificateType, resolvedTypeId)))
                    {
                        result.ServiceResult = new ServiceResult(
                            StatusCodes.BadInvalidArgument,
                            LocalizedText.From("The CertificateTypeId is not supported by the certificateGroup."));
                        return result;
                    }
                }
                else
                {
                    resolvedTypeId = certificateGroup.CertificateTypes[0];
                }

                if (!m_certTypeMap.TryGetValue(resolvedTypeId, out string? certificateTypeNameId))
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The CertificateType is invalid."));
                    return result;
                }

                // verify the CSR integrity for the application
                await certificateGroup.VerifySigningRequestAsync(application, certificateRequest, cancellationToken).ConfigureAwait(false);

                // store request in the queue for approval
                IUserIdentity? userIdentity = (context as ISessionSystemContext)?.UserIdentity;
                result.RequestId = m_request.StartSigningRequest(
                    applicationId,
                    certificateGroup.Configuration.Id!,
                    certificateTypeNameId,
                    certificateRequest,
                    userIdentity?.DisplayName!);

                if (m_autoApprove)
                {
                    try
                    {
                        m_request.ApproveRequest(result.RequestId, false);
                    }
                    catch
                    {
                        // ignore error as user may not have authorization to approve requests
                    }
                }

                result.ServiceResult = ServiceResult.Good;
                return result;
            }
            catch (Exception ex)
            {
                auditException = ex;
                throw;
            }
            finally
            {
                if (auditException == null &&
                    result.ServiceResult != null &&
                    StatusCode.IsBad(result.ServiceResult.StatusCode))
                {
                    auditException = new ServiceResultException(result.ServiceResult);
                }

                ArrayOf<Variant> auditInputs =
                [
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    certificateRequest
                ];
                Server.ReportCertificateRequestedAuditEvent(
                    context,
                    objectId,
                    method,
                    auditInputs,
                    resolvedGroupId,
                    resolvedTypeId,
                    m_logger,
                    auditException);
            }
        }

        private async ValueTask<FinishRequestMethodStateResult> OnFinishRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId requestId,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            var result = new FinishRequestMethodStateResult();

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);
            if (application == null)
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a valid application."));
                return result;
            }

            CertificateRequestState state = m_request.FinishRequest(
                applicationId,
                requestId,
                out string? certificateGroupId,
                out string? certificateTypeId,
                out ByteString generatedCertificate,
                out ByteString privateKey);

            result.Certificate = generatedCertificate;
            result.PrivateKey = privateKey;

            result.ServiceResult = VerifyApprovedState(state)!;
            if (result.ServiceResult != null)
            {
                return result;
            }

            ICertificateGroup? certificateGroup = null;
            if (!string.IsNullOrWhiteSpace(certificateGroupId))
            {
                foreach (KeyValuePair<NodeId, ICertificateGroup> group in m_certificateGroups)
                {
                    if (string.Equals(
                            group.Value.Configuration.Id,
                            certificateGroupId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        certificateGroup = group.Value;
                        break;
                    }
                }
            }

            if (certificateGroup == null)
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    LocalizedText.From("The CertificateGroupId does not refer to a supported certificate group."));
                return result;
            }

            NodeId certificateTypeNodeId = m_certTypeMap
                .Where(
                    pair => pair.Value
                        .Equals(certificateTypeId, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .SingleOrDefault();

            if (!certificateTypeNodeId.IsNull &&
                !certificateGroup.CertificateTypes.Contains(certificateType =>
                    Server.TypeTree.IsTypeOf(certificateType, certificateTypeNodeId)))
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    LocalizedText.From("The CertificateTypeId is not supported by the certificateGroup."));
                return result;
            }

            // distinguish cert creation at approval/complete time
            Certificate? certificate = null;
            if (result.Certificate.IsEmpty)
            {
                state = m_request.ReadRequest(
                    applicationId,
                    requestId,
                    out certificateGroupId,
                    out certificateTypeId,
                    out ByteString certificateRequest,
                    out string? subjectName,
                    out string[]? domainNames,
                    out string? privateKeyFormat,
                    out ReadOnlySpan<char> privateKeyPassword);

                result.ServiceResult = VerifyApprovedState(state)!;
                if (result.ServiceResult != null)
                {
                    return result;
                }

                if (!certificateRequest.IsEmpty)
                {
                    try
                    {
                        certificate = await certificateGroup.SigningRequestAsync(
                            application,
                            certificateTypeNodeId,
                            GetDefaultDomainNames(application),
                            certificateRequest,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        result.ServiceResult = ServiceResult.Create(
                            StatusCodes.BadConfigurationError,
                            "Error Generating Certificate={0}\nApplicationId={1}\nApplicationUri={2}\nApplicationName={3}",
                            e.Message,
                            applicationId.ToString(),
                            application.ApplicationUri!,
                            application.ApplicationNames[0].Text!);
                        return result;
                    }
                }
                else
                {
                    X509Certificate2KeyPair? newKeyPair = null;
                    try
                    {
                        newKeyPair = await certificateGroup.NewKeyPairRequestAsync(
                            application,
                            certificateTypeNodeId,
                            subjectName!,
                            domainNames!,
                            privateKeyFormat!,
                            privateKeyPassword.ToArray(),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        result.ServiceResult = ServiceResult.Create(
                            StatusCodes.BadConfigurationError,
                            "Error Generating New Key Pair Certificate={0}\nApplicationId={1}\nApplicationUri={2}",
                            e.Message,
                            applicationId.ToString(),
                            application.ApplicationUri!);
                        return result;
                    }

                    certificate = newKeyPair.Certificate;
                    result.PrivateKey = newKeyPair.PrivateKey;
                }

                result.Certificate = certificate.RawData.ToByteString();
            }
            else
            {
                certificate = Certificate.FromRawData(result.Certificate);
            }

            // Per OPC 10000-12 §7.6.6 FinishRequest returns the chain of
            // issuer certificates so the application can validate the new
            // certificate without depending on its local issuer store. The
            // chain is built from the in-memory CertificateGroup state to
            // avoid contending with concurrent SigningRequestAsync /
            // NewKeyPairRequestAsync writes against the AuthoritiesStore.
            try
            {
                result.IssuerCertificates = BuildIssuerCertificateChain(
                    certificate,
                    certificateGroup,
                    certificateTypeNodeId);

                // store new app certificate
                var certificateStoreIdentifier = new CertificateStoreIdentifier(
                    m_globalDiscoveryServerConfiguration.ApplicationCertificatesStorePath!);
                using (ICertificateStore store = certificateStoreIdentifier.OpenStore(Server.Telemetry))
                {
                    if (store != null)
                    {
                        await store.AddAsync(certificate, null, cancellationToken).ConfigureAwait(false);
                    }
                }

                m_database.SetApplicationCertificate(
                    applicationId,
                    m_certTypeMap[certificateTypeNodeId],
                    result.Certificate);

                m_database.SetApplicationTrustLists(
                    applicationId,
                    m_certTypeMap[certificateTypeNodeId],
                    certificateGroup.Configuration.TrustedListPath);

                m_request.AcceptRequest(requestId, result.Certificate);

                // Per OPC 10000-12 §7.6.6 the FinishRequest method takes
                // only (applicationId, requestId) as input. The previous
                // implementation included the returned PrivateKey in the audit
                // payload, which would leak the secret. Only true input
                // arguments are recorded; if a private key needs to be reflected
                // in audit it must be passed via the redacted placeholder.
                ArrayOf<Variant> inputArguments = [applicationId, requestId];
                Server.ReportCertificateDeliveredAuditEvent(context, objectId, method, inputArguments, m_logger);

                result.ServiceResult = ServiceResult.Good;
                return result;
            }
            finally
            {
                // Dispose the local owning handle even if any of the store /
                // database / audit operations above throw (the certificate's
                // raw data was already copied into result.Certificate).
                certificate?.Dispose();
            }
        }

        public ServiceResult OnGetCertificateGroups(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ref ArrayOf<NodeId> certificateGroupIds)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a valid application."));
            }

            var certificateGroupIdList = new List<NodeId>();
            foreach (KeyValuePair<NodeId, ICertificateGroup> certificateGroup in m_certificateGroups)
            {
                certificateGroupIdList.Add(certificateGroup.Key);
            }
            certificateGroupIds = [.. certificateGroupIdList];

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
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a valid application."));
            }

            if (certificateGroupId.IsNull)
            {
                certificateGroupId = m_defaultApplicationGroupId;
            }

            trustListId = GetTrustListId(certificateGroupId);

            if (trustListId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The CertificateGroupId does not refer to a group that is valid for the application."));
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
            ref bool updateRequired)
        {
            // Per OPC 10000-12 §7.6.12 GetCertificateStatus shall be called from
            // a Client with the CertificateAuthorityAdmin Role, the
            // ApplicationSelfAdmin Privilege, or the ApplicationAdmin Privilege.
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a valid application."));
            }

            if (certificateGroupId.IsNull)
            {
                certificateGroupId = m_defaultApplicationGroupId;
            }

            bool? updateRequiredResult = GetCertificateStatus(
                certificateGroupId,
                certificateTypeId);
            if (updateRequiredResult == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From(
                        "The CertificateGroupId and CertificateTypeId do not refer to a group and type that is valid for the application."));
            }

            updateRequired = (bool)updateRequiredResult;

            return ServiceResult.Good;
        }

        private void HasTrustListAccess(
            ISystemContext context,
            CertificateStoreIdentifier trustedStore)
        {
            AuthorizationHelper.HasTrustListAccess(
                context,
                trustedStore,
                m_certTypeMap,
                m_database);
        }

        private static ServiceResult? VerifyApprovedState(CertificateRequestState state)
        {
            switch (state)
            {
                case CertificateRequestState.New:
                    return new ServiceResult(
                        StatusCodes.BadNothingToDo,
                        LocalizedText.From("The request has not been approved by the administrator."));
                case CertificateRequestState.Rejected:
                    return new ServiceResult(
                        StatusCodes.BadRequestNotAllowed,
                        LocalizedText.From("The request has been rejected by the administrator."));
                case CertificateRequestState.Accepted:
                    return new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The request has already been accepted by the application."));
                case CertificateRequestState.Approved:
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected CertificateRequestState {state}");
            }
        }

        private ServiceResult OnGetServiceDescription(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref string serviceUri,
            ref ByteString serviceCertificate,
            ref ArrayOf<UserTokenPolicy> userTokenPolicies)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context);

            // Read the ServiceUri and ServiceCertificate from the parent
            // AuthorizationServiceState instance; these are populated from
            // the predefined nodeset.
            if (method.Parent is AuthorizationServiceState parentService)
            {
                serviceUri = parentService.ServiceUri?.Value ?? string.Empty;
                serviceCertificate = parentService.ServiceCertificate?.Value ?? default;
                userTokenPolicies = ResolveServiceUserTokenPolicies(parentService);
            }
            else
            {
                // Guarantee a valid, non-null output even for a detached Method
                // node: a null array cannot round-trip through the Method call.
                userTokenPolicies = ArrayOf<UserTokenPolicy>.Empty;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Resolves the UserTokenPolicies advertised by GetServiceDescription.
        /// The optional <c>UserTokenPolicies</c> Property may be absent on the
        /// AuthorizationService instance, so fall back to the Server's configured
        /// user token policies. The result is always non-null so the Method
        /// output round-trips (OPC 10000-12 §9).
        /// </summary>
        private ArrayOf<UserTokenPolicy> ResolveServiceUserTokenPolicies(AuthorizationServiceState service)
        {
            if (service.UserTokenPolicies is { } policyNode && !policyNode.Value.IsNull)
            {
                return policyNode.Value;
            }

            if (m_configuration.ServerConfiguration is { } serverConfiguration &&
                !serverConfiguration.UserTokenPolicies.IsNull)
            {
                return serverConfiguration.UserTokenPolicies;
            }

            return ArrayOf<UserTokenPolicy>.Empty;
        }

        /// <summary>
        /// Handles the legacy Part 12 <c>RequestAccessToken</c> method.
        /// Prefer <see cref="OnStartRequestTokenAsync"/> and
        /// <see cref="OnFinishRequestTokenAsync"/> for v1.05 clients.
        /// </summary>
        private async ValueTask<RequestAccessTokenMethodStateResult> OnRequestAccessTokenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            UserIdentityToken identityToken,
            string resourceId,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            var result = new RequestAccessTokenMethodStateResult();

            ArrayOf<Variant> auditInputs = [Variant.FromStructure(identityToken), resourceId];
            IAccessTokenProvider provider = GetAccessTokenProvider(
                context,
                objectId,
                method,
                auditInputs,
                "RequestAccessToken is not implemented by this GDS. Set AccessTokenProvider to enable.");

            try
            {
#pragma warning disable CS0618 // Legacy wire method is intentionally kept functional.
                result.AccessToken = await provider.RequestAccessTokenAsync(
                    identityToken, resourceId, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                Server.ReportAccessTokenIssuedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private async ValueTask<StartRequestTokenMethodStateResult> OnStartRequestTokenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string resourceId,
            string policyId,
            ByteString requestorData,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            var result = new StartRequestTokenMethodStateResult();
            ArrayOf<Variant> auditInputs = [resourceId, policyId, requestorData];

            IAccessTokenProvider provider = GetAccessTokenProvider(
                context,
                objectId,
                method,
                auditInputs,
                "StartRequestToken is not implemented by this GDS. Set AccessTokenProvider to enable.");

            try
            {
                IUserIdentity? callerIdentity = (context as ISessionSystemContext)?.UserIdentity;

                (ByteString serviceData, Guid requestId) = provider is AuthorizationServiceManager manager
                    ? await manager.StartRequestTokenAsync(
                        resourceId,
                        policyId,
                        requestorData,
                        callerIdentity,
                        cancellationToken).ConfigureAwait(false)
                    : await provider.StartRequestTokenAsync(
                        resourceId,
                        policyId,
                        requestorData,
                        cancellationToken).ConfigureAwait(false);

                result.ServiceData = serviceData;
                result.RequestId = requestId;
            }
            catch (Exception ex)
            {
                Server.ReportAccessTokenIssuedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private async ValueTask<FinishRequestTokenMethodStateResult> OnFinishRequestTokenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            Uuid requestId,
            ArrayOf<string> requestedRoles,
            UserIdentityToken userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            var result = new FinishRequestTokenMethodStateResult();
            ArrayOf<Variant> auditInputs =
            [
                requestId,
                requestedRoles,
                Variant.FromStructure(userIdentityToken),
                Variant.FromStructure(userTokenSignature)
            ];

            IAccessTokenProvider provider = GetAccessTokenProvider(
                context,
                objectId,
                method,
                auditInputs,
                "FinishRequestToken is not implemented by this GDS. Set AccessTokenProvider to enable.");

            try
            {
                AccessTokenResult atr = await provider.FinishRequestTokenAsync(
                    requestId, requestedRoles, userIdentityToken, userTokenSignature, cancellationToken)
                    .ConfigureAwait(false);

                result.AccessToken = atr.AccessToken;
                result.AccessTokenExpiryTime = atr.AccessTokenExpiryTime;
                result.RefreshToken = atr.RefreshToken ?? string.Empty;
                result.RefreshTokenExpiryTime = atr.RefreshTokenExpiryTime;
            }
            catch (Exception ex)
            {
                Server.ReportAccessTokenIssuedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private async ValueTask<RefreshTokenMethodStateResult> OnRefreshTokenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string resourceId,
            string currentRefreshToken,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            var result = new RefreshTokenMethodStateResult();
            ArrayOf<Variant> auditInputs = [resourceId];

            IAccessTokenProvider provider = GetAccessTokenProvider(
                context,
                objectId,
                method,
                auditInputs,
                "RefreshToken is not implemented by this GDS. Set AccessTokenProvider to enable.");

            try
            {
                AccessTokenResult atr = await provider
                    .RefreshTokenAsync(resourceId, currentRefreshToken, cancellationToken)
                    .ConfigureAwait(false);

                result.AccessToken = atr.AccessToken;
                result.AccessTokenExpiryTime = atr.AccessTokenExpiryTime;
                result.NewRefreshToken = atr.RefreshToken ?? string.Empty;
                result.NewRefreshTokenExpiryTime = atr.RefreshTokenExpiryTime;
            }
            catch (Exception ex)
            {
                Server.ReportAccessTokenIssuedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private IAccessTokenProvider GetAccessTokenProvider(
            ISystemContext context,
            NodeId objectId,
            MethodState method,
            ArrayOf<Variant> auditInputs,
            string message)
        {
            if (AccessTokenProvider != null)
            {
                return AccessTokenProvider;
            }

            var ex = new ServiceResultException(StatusCodes.BadNotSupported, message);
            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger, ex);
            throw ex;
        }

        private async ValueTask<KeyCredentialStartRequestMethodStateResult> OnKeyCredentialStartRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string applicationUri,
            ByteString publicKey,
            string securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> auditInputs = [applicationUri, publicKey, securityPolicyUri, requestedRoles];
            NodeId requestId;
            try
            {
                AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);
                ByteString clientCertificateFingerprint =
                    AuthorizationHelper.GetClientCertificateFingerprint(context);
                NodeId applicationId = ResolveKeyCredentialApplicationId(m_database, applicationUri);
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.KeyCredentialAdminOrSelfAdminOrAppAdmin,
                    applicationId);
                IApplicationOwnedKeyCredentialRequestStore store = GetApplicationOwnedKeyCredentialStore();

                m_logger.OnKeyCredentialStartRequest(applicationUri);

                requestId = await store.StartOwnedBoundRequestAsync(
                    applicationUri,
                    applicationId,
                    publicKey,
                    securityPolicyUri,
                    requestedRoles,
                    clientCertificateFingerprint,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Server.ReportKeyCredentialRequestedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportKeyCredentialRequestedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            return new KeyCredentialStartRequestMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                RequestId = requestId
            };
        }

        private async ValueTask<KeyCredentialFinishRequestMethodStateResult> OnKeyCredentialFinishRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId requestId,
            bool cancelRequest,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> auditInputs = [requestId, cancelRequest];
            try
            {
                AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);
                ByteString clientCertificateFingerprint =
                    AuthorizationHelper.GetClientCertificateFingerprint(context);
                IApplicationOwnedKeyCredentialRequestStore store = GetApplicationOwnedKeyCredentialStore();
                NodeId applicationId = await store
                    .GetRequestApplicationIdAsync(requestId, cancellationToken)
                    .ConfigureAwait(false);
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.KeyCredentialAdminOrSelfAdminOrAppAdmin,
                    applicationId);

                m_logger.OnKeyCredentialFinishRequest(requestId);

                FinishKeyCredentialRequestResult finished =
                    await store.FinishOwnedBoundRequestAsync(
                        requestId,
                        cancelRequest,
                        applicationId,
                        clientCertificateFingerprint,
                        cancellationToken).ConfigureAwait(false);
                KeyCredentialFinishRequestMethodStateResult result =
                    CreateKeyCredentialFinishResult(finished, cancelRequest);

                if (StatusCode.IsBad(result.ServiceResult.StatusCode))
                {
                    var exception = new ServiceResultException(result.ServiceResult);
                    Server.ReportKeyCredentialDeliveredAuditEvent(
                        context, objectId, method, auditInputs, m_logger, exception);
                    return result;
                }

                Server.ReportKeyCredentialDeliveredAuditEvent(
                    context, objectId, method, auditInputs, m_logger);

                return result;
            }
            catch (Exception ex)
            {
                Server.ReportKeyCredentialDeliveredAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }
        }

        private async ValueTask<KeyCredentialRevokeMethodStateResult> OnKeyCredentialRevokeAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string credentialId,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> auditInputs = [credentialId];
            try
            {
                AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);
                _ = AuthorizationHelper.GetClientCertificateFingerprint(context);
                IApplicationOwnedKeyCredentialRequestStore store = GetApplicationOwnedKeyCredentialStore();
                NodeId applicationId = await store
                    .GetCredentialApplicationIdAsync(credentialId, cancellationToken)
                    .ConfigureAwait(false);
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.KeyCredentialAdminOrSelfAdminOrAppAdmin,
                    applicationId);

                m_logger.OnKeyCredentialRevoke(credentialId);

                await store
                    .RevokeOwnedAsync(credentialId, applicationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Server.ReportKeyCredentialRevokedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportKeyCredentialRevokedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            return new KeyCredentialRevokeMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        internal static NodeId ResolveKeyCredentialApplicationId(
            IApplicationsDatabase database,
            string applicationUri)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }
            if (string.IsNullOrWhiteSpace(applicationUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The ApplicationUri is not known to the GDS.");
            }

            ApplicationRecordDataType[] matches = database
                .FindApplications(applicationUri)?
                .Where(application => string.Equals(
                    application.ApplicationUri,
                    applicationUri,
                    StringComparison.Ordinal))
                .ToArray() ?? [];
            if (matches.Length == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The ApplicationUri is not known to the GDS.");
            }
            if (matches.Length != 1 || matches[0].ApplicationId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The ApplicationUri does not uniquely identify a registered application.");
            }

            return matches[0].ApplicationId;
        }

        internal static ServiceResult GetKeyCredentialFinishServiceResult(
            KeyCredentialRequestState state,
            bool cancelRequest)
        {
            if (state == KeyCredentialRequestState.New)
            {
                return new ServiceResult(StatusCodes.BadRequestNotComplete);
            }
            if (state == KeyCredentialRequestState.Rejected && !cancelRequest)
            {
                return new ServiceResult(StatusCodes.BadRequestNotAllowed);
            }
            return ServiceResult.Good;
        }

        internal static KeyCredentialFinishRequestMethodStateResult CreateKeyCredentialFinishResult(
            FinishKeyCredentialRequestResult finished,
            bool cancelRequest)
        {
            ServiceResult serviceResult =
                GetKeyCredentialFinishServiceResult(finished.State, cancelRequest);
            if (StatusCode.IsBad(serviceResult.StatusCode) || cancelRequest)
            {
                return new KeyCredentialFinishRequestMethodStateResult
                {
                    ServiceResult = serviceResult
                };
            }

            return new KeyCredentialFinishRequestMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                CredentialId = finished.CredentialId ?? string.Empty,
                CredentialSecret = finished.CredentialSecret,
                CertificateThumbprint = finished.CertificateThumbprint ?? string.Empty,
                SecurityPolicyUri = finished.SecurityPolicyUri ?? string.Empty,
                GrantedRoles = finished.GrantedRoles
            };
        }

        private IApplicationOwnedKeyCredentialRequestStore GetApplicationOwnedKeyCredentialStore()
        {
            return KeyCredentialRequestStore as IApplicationOwnedKeyCredentialRequestStore ??
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The key-credential store does not support application ownership checks.");
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Resource ownership rather than address-space plumbing: the
        /// certificate groups and the certificate stores their trust-list
        /// handlers hold open are acquired while the address space comes up
        /// and have to be released deterministically. The fluent registries
        /// (events, simulation, monitored sources) are torn down by the base
        /// class.
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Every group this manager created is in the owning list, so
                // a startup that failed before Configure could bind and index
                // the groups still releases them.
                foreach (ICertificateGroup certificateGroup in m_ownedCertificateGroups)
                {
                    // The TrustList handler holds its store instances open
                    // for reuse across operations; the node manager owns the
                    // node and therefore the handler's lifetime.
                    (certificateGroup.DefaultTrustList?.Handle as IDisposable)?.Dispose();
                    certificateGroup.Dispose();
                }

                m_ownedCertificateGroups.Clear();
                m_certificateGroups.Clear();
            }

            base.Dispose(disposing);
        }

        private readonly bool m_autoApprove;

        private readonly ApplicationConfiguration m_configuration;
        private readonly GlobalDiscoveryServerConfiguration m_globalDiscoveryServerConfiguration;
        private readonly IApplicationsDatabase m_database;
        private readonly ICertificateRequest m_request;
        private readonly ICertificateGroup m_certificateGroupFactory;

        // The certificate authorities this manager brought up and therefore
        // owns, in configuration order. Filled while the address space comes
        // up; Dispose releases them from here.
        private readonly List<ICertificateGroup> m_ownedCertificateGroups;

        // The same groups indexed by the NodeId of the certificate group node
        // each is bound to. A group only learns that id in the Configure
        // pass, so this index stays empty until then.
        private readonly Dictionary<NodeId, ICertificateGroup> m_certificateGroups;
        private Dictionary<NodeId, string> m_certTypeMap = [];
        private IKeyCredentialRequestStore? m_keyCredentialStore;

        /// <summary>
        /// Gets or sets the key-credential request store used by
        /// KeyCredentialService handler methods. When <c>null</c> an
        /// in-memory store is created lazily on first use.
        /// </summary>
        public IKeyCredentialRequestStore KeyCredentialRequestStore
        {
            get => m_keyCredentialStore ??= new InMemoryKeyCredentialRequestStore();
            set => m_keyCredentialStore = value;
        }

        /// <summary>
        /// Gets or sets the access-token provider used by
        /// AuthorizationService handler methods. When <c>null</c> the
        /// built-in handlers return <c>Bad_NotSupported</c>.
        /// </summary>
        public IAccessTokenProvider? AccessTokenProvider { get; set; }

    }

    internal static partial class ApplicationsNodeManagerLog
    {
        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 0, Level = LogLevel.Information,
            Message = "QueryServers Returned: {Count} records")]
        public static partial void QueryServersReturned(this ILogger logger, int count);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 1, Level = LogLevel.Information,
            Message = "Server Found at {DiscoveryUrl}")]
        public static partial void ServerFound(this ILogger logger, string? discoveryUrl);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 2, Level = LogLevel.Error,
            Message = "Could not connect to the Database!")]
        public static partial void CouldNotConnectToDatabase(this ILogger logger, Exception e);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 3, Level = LogLevel.Information,
            Message = "Exception")]
        public static partial void Exception(this ILogger logger, Exception ie);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 4, Level = LogLevel.Information,
            Message = "Initialize Database tables!")]
        public static partial void InitializeDatabaseTables(this ILogger logger);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 5, Level = LogLevel.Information,
            Message = "Database Initialized!")]
        public static partial void DatabaseInitialized(this ILogger logger);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 6, Level = LogLevel.Error,
            Message = "Unexpected error revoking certificate. {Subject} for Authority={CertificateGroupId}")]
        public static partial void UnexpectedErrorRevokingCertificate(
            this ILogger logger,
            Exception e,
            string subject,
            NodeId certificateGroupId);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 7, Level = LogLevel.Warning,
            Message = "Failed to build issuer chain for {Subject}; falling back to the immediate issuing CA.")]
        public static partial void FailedToBuildIssuerChain(this ILogger logger, Exception ex, string subject);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 8, Level = LogLevel.Error,
            Message = "Unexpected error initializing certificateGroup: {CertificateGroupId}")]
        public static partial void UnexpectedErrorInitializingCertificateGroup(
            this ILogger logger,
            Exception e,
            string? certificateGroupId);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 9, Level = LogLevel.Information,
            Message = "QueryServers: {ApplicationUri} {ApplicationName}")]
        public static partial void QueryServers(this ILogger logger, string applicationUri, string applicationName);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 10, Level = LogLevel.Information,
            Message = "QueryApplications: {ApplicationUri} {ApplicationName}")]
        public static partial void QueryApplications(
            this ILogger logger,
            string applicationUri,
            string applicationName);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 11, Level = LogLevel.Information,
            Message = "OnRegisterApplication: {ApplicationUri}")]
        public static partial void OnRegisterApplication(this ILogger logger, string? applicationUri);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 12, Level = LogLevel.Information,
            Message = "OnUpdateApplication: {ApplicationUri}")]
        public static partial void OnUpdateApplication(this ILogger logger, string? applicationUri);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 13, Level = LogLevel.Information,
            Message = "OnUnregisterApplication: {ApplicationId}")]
        public static partial void OnUnregisterApplication(this ILogger logger, string applicationId);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 14, Level = LogLevel.Warning,
            Message = "Failed to revoke: {CertificateType}")]
        public static partial void FailedToRevoke(this ILogger logger, Exception ex, string certificateType);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 15, Level = LogLevel.Information,
            Message = "OnFindApplications: {ApplicationUri}")]
        public static partial void OnFindApplications(this ILogger logger, string applicationUri);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 16, Level = LogLevel.Information,
            Message = "OnGetApplication: {ApplicationId}")]
        public static partial void OnGetApplication(this ILogger logger, NodeId applicationId);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 17, Level = LogLevel.Information,
            Message = "Created custom certificate group node: {Id} with NodeId {NodeId}")]
        public static partial void CreatedCustomCertificateGroupNode(this ILogger logger, string id, NodeId nodeId);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 18, Level = LogLevel.Information,
            Message = "OnKeyCredentialStartRequest: {ApplicationUri}")]
        public static partial void OnKeyCredentialStartRequest(this ILogger logger, string applicationUri);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 19, Level = LogLevel.Information,
            Message = "OnKeyCredentialFinishRequest: {RequestId}")]
        public static partial void OnKeyCredentialFinishRequest(this ILogger logger, NodeId requestId);

        [LoggerMessage(EventId = GdsServerCommonEventIds.ApplicationsNodeManager + 20, Level = LogLevel.Information,
            Message = "OnKeyCredentialRevoke: {CredentialId}")]
        public static partial void OnKeyCredentialRevoke(this ILogger logger, string credentialId);
    }
}
