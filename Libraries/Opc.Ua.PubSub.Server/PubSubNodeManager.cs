/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Server.Internal;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Mounts behaviour onto the standard <c>PublishSubscribe</c>
    /// Object (NodeId <c>i=14443</c>) loaded by the hosting server's
    /// <c>DiagnosticsNodeManager</c>: binds the
    /// <c>Status.Enable</c> / <c>Status.Disable</c> methods, the
    /// <c>AddConnection</c> / <c>RemoveConnection</c> methods, the
    /// <c>SecurityGroups</c> management methods, and the
    /// <c>GetSecurityKeys</c> SKS entry-point.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1">
    /// Part 14 §9.1 PublishSubscribe Object</see>. This manager does
    /// not own any nodes itself; the standard PublishSubscribe
    /// sub-tree is loaded by the server core from
    /// <c>Opc.Ua.NodeSet.xml</c>. The manager registers a vendor
    /// PubSub-server namespace so it has a distinct identity in
    /// <see cref="IServerInternal.NamespaceUris"/> but contains no
    /// predefined nodes.
    /// </remarks>
    public sealed class PubSubNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Vendor namespace URI registered by the PubSub server
        /// manager. The URI is added to
        /// <see cref="IServerInternal.NamespaceUris"/> so clients
        /// can discover that the OPC UA Server hosts a PubSub
        /// runtime.
        /// </summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/PubSub/Server";

        private const uint StatusEnableNodeId = 17407;
        private const uint StatusDisableNodeId = 17408;
        private const uint SetSecurityKeysNodeId = 17364;
        private const uint AddConnectionNodeId = 17366;
        private const uint RemoveConnectionNodeId = 17369;
        private const uint GetSecurityKeysNodeId = 15215;
        private const uint GetSecurityGroupNodeId = 15440;
        private const uint AddSecurityGroupNodeId = 15444;
        private const uint RemoveSecurityGroupNodeId = 15447;
        private const uint AddPushTargetNodeId = 25441;
        private const uint RemovePushTargetNodeId = 25444;
        private const uint AddPublishedDataItemsNodeId = 14479;
        private const uint AddPublishedEventsNodeId = 14482;
        private const uint AddPublishedDataItemsTemplateNodeId = 16842;
        private const uint RemovePublishedDataSetNodeId = 14485;
        private const uint AddDataSetFolderNodeId = 16884;
        private const uint RemoveDataSetFolderNodeId = 16923;
        private static readonly NodeId s_publishedDataSetsNodeId = new(14478u);
        private static readonly NodeId s_securityGroupsNodeId = new(15443u);
        private static readonly NodeId s_keyPushTargetsNodeId = new(25440u);

        private readonly IPubSubApplication m_application;
        private readonly IPubSubKeyServiceServer? m_keyService;
        private readonly PubSubServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly PubSubActionMethodRegistration[] m_actionMethodRegistrations;
        private readonly PushSecurityKeyProvider[] m_pushKeyProviders;
        private readonly Lock m_addressSpaceGate = new();
        private readonly List<NodeState> m_dynamicRoots = [];
        private readonly List<NodeState> m_securityGroupRoots = [];
        private readonly List<NodeState> m_keyPushTargetRoots = [];
        private readonly SortedSet<string> m_dataSetFolders = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, PubSubConfigurationFileHandle> m_fileHandles = [];
        private readonly Dictionary<NodeId, PubSubKeyPushTargetRegistration> m_keyPushTargets = [];
        private readonly IPubSubIdAllocator m_idAllocator;
        private IDiagnosticsNodeManager? m_diagnosticsNodeManager;
        private PubSubStatusBinding? m_statusBinding;
        private bool m_methodsBound;

        /// <summary>
        /// Creates a new <see cref="PubSubNodeManager"/>.
        /// </summary>
        /// <param name="server">Hosting server.</param>
        /// <param name="configuration">Application configuration.</param>
        /// <param name="pubSubApplication">Runtime application.</param>
        /// <param name="sksServer">
        /// Optional SKS server. When non-<see langword="null"/> and
        /// <see cref="PubSubServerOptions.ExposeSecurityKeyService"/>
        /// is set, the SKS methods are bound.
        /// </param>
        /// <param name="options">Server options.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="actionMethodRegistrations">Optional PublishedActionMethod bindings.</param>
        /// <param name="pushKeyProviders">Optional SetSecurityKeys push providers.</param>
        /// <param name="idAllocator">Optional shared id allocator.</param>
        public PubSubNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IPubSubApplication pubSubApplication,
            IPubSubKeyServiceServer? sksServer,
            PubSubServerOptions options,
            ITelemetryContext telemetry,
            IEnumerable<PubSubActionMethodRegistration>? actionMethodRegistrations = null,
            IEnumerable<PushSecurityKeyProvider>? pushKeyProviders = null,
            IPubSubIdAllocator? idAllocator = null)
            : base(
                  server,
                  configuration,
                  (telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
                      .CreateLogger<PubSubNodeManager>(),
                  NamespaceUri)
        {
            if (pubSubApplication is null)
            {
                throw new ArgumentNullException(nameof(pubSubApplication));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_application = pubSubApplication;
            m_keyService = sksServer;
            m_options = options;
            m_telemetry = telemetry;
            m_actionMethodRegistrations = actionMethodRegistrations?.ToArray()
                ?? [];
            m_pushKeyProviders = pushKeyProviders?.ToArray() ?? [];
            m_idAllocator = idAllocator ?? new InMemoryPubSubIdAllocator();
            MethodHandlers = new PubSubMethodHandlers(
                pubSubApplication,
                options.ExposeSecurityKeyService ? sksServer : null,
                options,
                telemetry,
                m_pushKeyProviders);
        }

        /// <summary>
        /// <see langword="true"/> once the standard PubSub method
        /// nodes have been located and bound by
        /// <see cref="CreateAddressSpaceAsync"/>. Test-only.
        /// </summary>
        internal bool AreMethodsBound => m_methodsBound;

        /// <summary>
        /// The status / diagnostics binding allocated by
        /// <see cref="CreateAddressSpaceAsync"/>; null until the
        /// address space is initialised. Test-only.
        /// </summary>
        internal PubSubStatusBinding? StatusBinding => m_statusBinding;

        /// <summary>
        /// Returns the <see cref="PubSubMethodHandlers"/> instance
        /// owned by this node manager. Test-only.
        /// </summary>
        internal PubSubMethodHandlers MethodHandlers { get; }

        /// <summary>
        /// Namespace index registered for dynamic PubSub instance nodes. Test-only.
        /// </summary>
        internal ushort AddressSpaceNamespaceIndex => NamespaceIndexes[0];

        /// <summary>
        /// Rebuilds SKS dynamic nodes. Test-only.
        /// </summary>
        internal async ValueTask RebuildSksAddressSpaceForTestsAsync()
        {
            await RebuildSecurityGroupAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
            await RebuildKeyPushTargetAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            IDiagnosticsNodeManager? diagnosticsNodeManager = Server.DiagnosticsNodeManager;
            if (diagnosticsNodeManager is null)
            {
                m_logger.DiagnosticsNodeManagerNotAvailable();
                return;
            }

            if (m_application is PubSubApplication concreteApplication)
            {
                concreteApplication.SetAddressSpaceNamespaceIndex(NamespaceIndexes[0]);
            }
            MethodHandlers.SetSecurityGroupNamespaceIndex(NamespaceIndexes[0]);

            BindMethods(diagnosticsNodeManager);
            RegisterActionMethodHandlers();
            m_diagnosticsNodeManager = diagnosticsNodeManager;
            m_application.ConfigurationChanged += OnConfigurationChanged;
            await RebuildConfigurationAddressSpaceAsync(cancellationToken).ConfigureAwait(false);

            if (m_application is PubSubApplication concrete &&
                m_options.DiagnosticsExposure != PubSubDiagnosticsExposure.None)
            {
                m_statusBinding = new PubSubStatusBinding(
                    m_application,
                    concrete.Diagnostics,
                    diagnosticsNodeManager,
                    m_options.DiagnosticsExposure,
                    m_telemetry);
                m_statusBinding.Bind();
            }
            else if (m_options.DiagnosticsExposure != PubSubDiagnosticsExposure.None)
            {
                m_logger.PubSubDiagnosticsNotExposed();
            }

            if (m_options.ExposeSecurityKeyService &&
                m_keyService is not null &&
                !string.IsNullOrEmpty(m_options.DefaultSecurityGroupId))
            {
                await SeedDefaultSecurityGroupAsync(cancellationToken).ConfigureAwait(false);
            }
            await RebuildSecurityGroupAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            await RebuildKeyPushTargetAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_statusBinding?.Dispose();
                m_statusBinding = null;
                m_application.ConfigurationChanged -= OnConfigurationChanged;
            }
            base.Dispose(disposing);
        }

        private void BindMethods(IDiagnosticsNodeManager diagnosticsNodeManager)
        {
            MethodState? enable = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(StatusEnableNodeId));
            MethodState? disable = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(StatusDisableNodeId));
            MethodState? setKeys = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(SetSecurityKeysNodeId));
            MethodState? addConn = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(AddConnectionNodeId));
            MethodState? removeConn = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(RemoveConnectionNodeId));

            enable?.OnCallMethod = MethodHandlers.OnEnable;
            disable?.OnCallMethod = MethodHandlers.OnDisable;
            if (m_options.ExposeConfigurationMethods)
            {
                if (setKeys is not null)
                {
                    setKeys.OnCallMethod = MethodHandlers.OnSetSecurityKeys;
                    setKeys.RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                            Permissions = (uint)PermissionType.Call
                        }
                    ];
                }
                addConn?.OnCallMethod = MethodHandlers.OnAddConnection;
                removeConn?.OnCallMethod = MethodHandlers.OnRemoveConnection;
                BindPublishedDataSetFolderMethods(diagnosticsNodeManager);
            }

            if (m_options.ExposeSecurityKeyService && m_keyService is not null)
            {
                MethodState? getKeys = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(GetSecurityKeysNodeId));
                MethodState? getGroup = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(GetSecurityGroupNodeId));
                MethodState? addGroup = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(AddSecurityGroupNodeId));
                MethodState? removeGroup = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(RemoveSecurityGroupNodeId));
                MethodState? addPushTarget = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(AddPushTargetNodeId));
                MethodState? removePushTarget = diagnosticsNodeManager
                    .FindPredefinedNode<MethodState>(new NodeId(RemovePushTargetNodeId));
                getKeys?.OnCallMethod2 = MethodHandlers.OnGetSecurityKeys;
                getGroup?.OnCallMethod = OnGetSecurityGroup;
                addGroup?.OnCallMethod = OnAddSecurityGroup;
                removeGroup?.OnCallMethod = OnRemoveSecurityGroup;
                addPushTarget?.OnCallMethod = OnAddPushTarget;
                removePushTarget?.OnCallMethod = OnRemovePushTarget;
            }

            m_methodsBound = enable is not null || disable is not null;
        }

        private void BindPublishedDataSetFolderMethods(IDiagnosticsNodeManager diagnosticsNodeManager)
        {
            BindStandardMethod(diagnosticsNodeManager, AddPublishedDataItemsNodeId, MethodHandlers.OnAddPublishedDataItems);
            BindStandardMethod(diagnosticsNodeManager, AddPublishedEventsNodeId, MethodHandlers.OnAddPublishedEvents);
            BindStandardMethod(
                diagnosticsNodeManager,
                AddPublishedDataItemsTemplateNodeId,
                MethodHandlers.OnAddPublishedDataItemsTemplate);
            BindStandardMethod(diagnosticsNodeManager, RemovePublishedDataSetNodeId, MethodHandlers.OnRemovePublishedDataSet);
            BindStandardMethod(diagnosticsNodeManager, AddDataSetFolderNodeId, OnAddDataSetFolder);
            BindStandardMethod(diagnosticsNodeManager, RemoveDataSetFolderNodeId, OnRemoveDataSetFolder);
        }

        private static void BindStandardMethod(
            IDiagnosticsNodeManager diagnosticsNodeManager,
            uint nodeId,
            GenericMethodCalledEventHandler handler)
        {
            MethodState? method = diagnosticsNodeManager
                .FindPredefinedNode<MethodState>(new NodeId(nodeId));
            method?.OnCallMethod = handler;
        }

        private void OnConfigurationChanged(
            object? sender,
            PubSubConfigurationChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            RebuildConfigurationAddressSpaceAsync(CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        private async ValueTask RebuildConfigurationAddressSpaceAsync(
            CancellationToken cancellationToken)
        {
            IDiagnosticsNodeManager? diagnosticsNodeManager = m_diagnosticsNodeManager;
            if (diagnosticsNodeManager is null)
            {
                return;
            }

            BaseObjectState? publishSubscribe = diagnosticsNodeManager
                .FindPredefinedNode<BaseObjectState>(ObjectIds.PublishSubscribe);
            BaseObjectState? publishedDataSets = diagnosticsNodeManager
                .FindPredefinedNode<BaseObjectState>(s_publishedDataSetsNodeId);
            if (publishSubscribe is null)
            {
                return;
            }

            PubSubConfigurationDataType configuration = m_application.GetConfiguration();
            List<NodeState> oldRoots;
            string[] dataSetFolders;
            lock (m_addressSpaceGate)
            {
                oldRoots = [.. m_dynamicRoots];
                m_dynamicRoots.Clear();
                dataSetFolders = [.. m_dataSetFolders];
            }

            foreach (NodeState oldRoot in oldRoots)
            {
                await RemovePredefinedNodeAsync(SystemContext, oldRoot, [], cancellationToken)
                    .ConfigureAwait(false);
            }

            var newRoots = new List<NodeState>();
            if (!configuration.Connections.IsNull)
            {
                foreach (PubSubConnectionDataType connection in configuration.Connections)
                {
                    BaseObjectState connectionNode = CreateObject(
                        publishSubscribe,
                        CreateConnectionNodeId(connection.Name ?? string.Empty),
                        connection.Name ?? "Connection",
                        new NodeId(14209u));
                    BindConnectionMethods(connectionNode);
                    AddStatusObject(connectionNode);
                    AddConfigurationVersion(connectionNode, m_application.ConfigurationVersion);
                    newRoots.Add(connectionNode);

                    if (!connection.WriterGroups.IsNull)
                    {
                        foreach (WriterGroupDataType writerGroup in connection.WriterGroups)
                        {
                            BaseObjectState writerGroupNode = CreateObject(
                                connectionNode,
                                CreateWriterGroupNodeId(connection.Name ?? string.Empty, writerGroup.Name ?? string.Empty),
                                writerGroup.Name ?? "WriterGroup",
                                new NodeId(17725u));
                            BindWriterGroupMethods(writerGroupNode);
                            AddStatusObject(writerGroupNode);
                            AddConfigurationVersion(writerGroupNode, m_application.ConfigurationVersion);

                            if (!writerGroup.DataSetWriters.IsNull)
                            {
                                foreach (DataSetWriterDataType writer in writerGroup.DataSetWriters)
                                {
                                    BaseObjectState writerNode = CreateObject(
                                        writerGroupNode,
                                        CreateWriterNodeId(
                                            connection.Name ?? string.Empty,
                                            writerGroup.Name ?? string.Empty,
                                            writer.Name ?? string.Empty),
                                        writer.Name ?? "DataSetWriter",
                                        new NodeId(15298u));
                                    AddStatusObject(writerNode);
                                    AddConfigurationVersion(writerNode, m_application.ConfigurationVersion);
                                }
                            }
                        }
                    }

                    if (!connection.ReaderGroups.IsNull)
                    {
                        foreach (ReaderGroupDataType readerGroup in connection.ReaderGroups)
                        {
                            BaseObjectState readerGroupNode = CreateObject(
                                connectionNode,
                                CreateReaderGroupNodeId(connection.Name ?? string.Empty, readerGroup.Name ?? string.Empty),
                                readerGroup.Name ?? "ReaderGroup",
                                new NodeId(17999u));
                            BindReaderGroupMethods(readerGroupNode);
                            AddStatusObject(readerGroupNode);
                            AddConfigurationVersion(readerGroupNode, m_application.ConfigurationVersion);

                            if (!readerGroup.DataSetReaders.IsNull)
                            {
                                foreach (DataSetReaderDataType reader in readerGroup.DataSetReaders)
                                {
                                    BaseObjectState readerNode = CreateObject(
                                        readerGroupNode,
                                        CreateReaderNodeId(
                                            connection.Name ?? string.Empty,
                                            readerGroup.Name ?? string.Empty,
                                            reader.Name ?? string.Empty),
                                        reader.Name ?? "DataSetReader",
                                        new NodeId(15306u));
                                    AddStatusObject(readerNode);
                                    AddConfigurationVersion(readerNode, m_application.ConfigurationVersion);
                                }
                            }
                        }
                    }
                }
            }

            BaseObjectState configurationFile = CreateObject(
                publishSubscribe,
                new NodeId("pubsub:configuration", NamespaceIndexes[0]),
                "PubSubConfiguration",
                new NodeId(25482u));
            BindPubSubConfigurationFileMethods(configurationFile);
            newRoots.Add(configurationFile);

            if (publishedDataSets is not null)
            {
                foreach (string folderName in dataSetFolders)
                {
                    BaseObjectState folderNode = CreateObject(
                        publishedDataSets,
                        CreateDataSetFolderNodeId(folderName),
                        folderName,
                        new NodeId(14477u));
                    BindDataSetFolderMethods(folderNode);
                    newRoots.Add(folderNode);
                }

                if (!configuration.PublishedDataSets.IsNull)
                {
                    foreach (PublishedDataSetDataType dataSet in configuration.PublishedDataSets)
                    {
                        NodeId typeDefinitionId = GetPublishedDataSetTypeDefinition(dataSet);
                        BaseObjectState dataSetNode = CreateObject(
                            publishedDataSets,
                            CreatePublishedDataSetNodeId(dataSet.Name ?? string.Empty),
                            dataSet.Name ?? "PublishedDataSet",
                            typeDefinitionId);
                        AddStatusObject(dataSetNode);
                        AddConfigurationVersion(
                            dataSetNode,
                            dataSet.DataSetMetaData?.ConfigurationVersion ?? m_application.ConfigurationVersion);
                        if (typeDefinitionId == new NodeId(14534u))
                        {
                            BindPublishedDataItemsMethods(dataSetNode);
                            AddPublishedDataProperty(dataSetNode, dataSet);
                        }
                        newRoots.Add(dataSetNode);
                    }
                }
            }

            foreach (NodeState root in newRoots)
            {
                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken).ConfigureAwait(false);
            }

            lock (m_addressSpaceGate)
            {
                m_dynamicRoots.AddRange(newRoots);
            }
        }

        private async ValueTask RebuildSecurityGroupAddressSpaceAsync(CancellationToken cancellationToken)
        {
            if (!m_options.ExposeSecurityKeyService || m_keyService is null || m_diagnosticsNodeManager is null)
            {
                return;
            }

            BaseObjectState? securityGroups = m_diagnosticsNodeManager
                .FindPredefinedNode<BaseObjectState>(s_securityGroupsNodeId);
            if (securityGroups is null)
            {
                return;
            }

            List<NodeState> oldRoots;
            lock (m_addressSpaceGate)
            {
                oldRoots = [.. m_securityGroupRoots];
                m_securityGroupRoots.Clear();
            }

            foreach (NodeState oldRoot in oldRoots)
            {
                await RemovePredefinedNodeAsync(SystemContext, oldRoot, [], cancellationToken)
                    .ConfigureAwait(false);
            }

            var newRoots = new List<NodeState>();
            foreach (string securityGroupId in (string[])[.. m_keyService.SecurityGroupIds])
            {
                SksSecurityGroup? group = await m_keyService
                    .GetSecurityGroupAsync(securityGroupId, cancellationToken)
                    .ConfigureAwait(false);
                if (group is null)
                {
                    continue;
                }

                NodeId groupNodeId = CreateSecurityGroupNodeId(securityGroupId);
                BaseObjectState groupNode = CreateObject(
                    securityGroups,
                    groupNodeId,
                    securityGroupId,
                    new NodeId(15471u));
                groupNode.RolePermissions = group.RolePermissions;
                BindSecurityGroupMethods(groupNode);
                MethodHandlers.RegisterSecurityGroupNodeId(securityGroupId, groupNodeId);
                newRoots.Add(groupNode);
            }

            foreach (NodeState root in newRoots)
            {
                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken).ConfigureAwait(false);
            }

            lock (m_addressSpaceGate)
            {
                m_securityGroupRoots.AddRange(newRoots);
            }
        }

        private async ValueTask RebuildKeyPushTargetAddressSpaceAsync(CancellationToken cancellationToken)
        {
            if (!m_options.ExposeSecurityKeyService || m_diagnosticsNodeManager is null)
            {
                return;
            }

            BaseObjectState? keyPushTargets = m_diagnosticsNodeManager
                .FindPredefinedNode<BaseObjectState>(s_keyPushTargetsNodeId);
            if (keyPushTargets is null)
            {
                return;
            }

            List<NodeState> oldRoots;
            PubSubKeyPushTargetRegistration[] targets;
            lock (m_addressSpaceGate)
            {
                oldRoots = [.. m_keyPushTargetRoots];
                m_keyPushTargetRoots.Clear();
                targets = [.. m_keyPushTargets.Values];
            }

            foreach (NodeState oldRoot in oldRoots)
            {
                await RemovePredefinedNodeAsync(SystemContext, oldRoot, [], cancellationToken)
                    .ConfigureAwait(false);
            }

            var newRoots = new List<NodeState>();
            foreach (PubSubKeyPushTargetRegistration target in targets)
            {
                BaseObjectState targetNode = CreateObject(
                    keyPushTargets,
                    target.NodeId,
                    target.Name,
                    new NodeId(25337u));
                BindKeyPushTargetMethods(targetNode);
                newRoots.Add(targetNode);
            }

            foreach (NodeState root in newRoots)
            {
                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken).ConfigureAwait(false);
            }

            lock (m_addressSpaceGate)
            {
                m_keyPushTargetRoots.AddRange(newRoots);
            }
        }

        private static BaseObjectState CreateObject(
            BaseObjectState parent,
            NodeId nodeId,
            string browseName,
            NodeId typeDefinitionId)
        {
            var node = new BaseObjectState(parent)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(browseName, nodeId.NamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                TypeDefinitionId = typeDefinitionId,
                EventNotifier = EventNotifiers.None
            };
            parent.AddChild(node);
            node.AddReference(ReferenceTypeIds.HasComponent, true, parent.NodeId);
            return node;
        }

        private void AddStatusObject(BaseObjectState parent)
        {
            string parentId = parent.NodeId.IdentifierAsString;
            BaseObjectState status = CreateObject(
                parent,
                new NodeId($"{parentId}:Status", parent.NodeId.NamespaceIndex),
                "Status",
                new NodeId(14643u));
            var state = new BaseDataVariableState(status)
            {
                NodeId = new NodeId($"{parentId}:Status:State", parent.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName("State", parent.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("State"),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = new NodeId(14647u),
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = Variant.From((int)PubSubState.Disabled),
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };
            status.AddChild(state);

            AddStatusMethod(status, "Enable", state, PubSubState.PreOperational);
            AddStatusMethod(status, "Disable", state, PubSubState.Disabled);
        }

        private static void AddConfigurationVersion(
            BaseObjectState parent,
            ConfigurationVersionDataType version)
        {
            string parentId = parent.NodeId.IdentifierAsString;
            var variable = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId($"{parentId}:ConfigurationVersion", parent.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName("ConfigurationVersion", parent.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("ConfigurationVersion"),
                TypeDefinitionId = VariableTypeIds.PropertyType,
                DataType = DataTypeIds.ConfigurationVersionDataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = new ExtensionObject(version),
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };
            parent.AddChild(variable);
        }

        private static void AddPublishedDataProperty(
            BaseObjectState parent,
            PublishedDataSetDataType dataSet)
        {
            if (dataSet.DataSetSource.IsNull ||
                !dataSet.DataSetSource.TryGetValue(out PublishedDataItemsDataType? items) ||
                items is null)
            {
                return;
            }
            string parentId = parent.NodeId.IdentifierAsString;
            var variable = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId($"{parentId}:PublishedData", parent.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName("PublishedData", parent.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("PublishedData"),
                TypeDefinitionId = VariableTypeIds.PropertyType,
                DataType = DataTypeIds.PublishedVariableDataType,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = new Variant(CreateExtensionObjects(items.PublishedData)),
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };
            parent.AddChild(variable);
        }

        private static NodeId GetPublishedDataSetTypeDefinition(PublishedDataSetDataType dataSet)
        {
            if (!dataSet.DataSetSource.IsNull &&
                dataSet.DataSetSource.TryGetValue(out PublishedDataItemsDataType? items) &&
                items is not null)
            {
                return new NodeId(14534u);
            }
            if (!dataSet.DataSetSource.IsNull &&
                dataSet.DataSetSource.TryGetValue(out PublishedEventsDataType? events) &&
                events is not null)
            {
                return new NodeId(14572u);
            }
            return new NodeId(14509u);
        }

        private void AddStatusMethod(
            BaseObjectState status,
            string browseName,
            BaseDataVariableState state,
            PubSubState target)
        {
            string statusId = status.NodeId.IdentifierAsString;
            NodeId componentId = status.Parent?.NodeId
                ?? throw new ArgumentException("Status object must have a parent.", nameof(status));
            var method = new MethodState(status)
            {
                NodeId = new NodeId($"{statusId}:{browseName}", status.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName(browseName, status.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                Executable = true,
                UserExecutable = true,
                OnCallMethod = (_, _, _, _) =>
                {
                    try
                    {
                        ApplyStatusTransition(componentId, target, CancellationToken.None)
                            .AsTask()
                            .GetAwaiter()
                            .GetResult();
                        state.Value = Variant.From((int)target);
                        state.Timestamp = DateTime.UtcNow;
                        return ServiceResult.Good;
                    }
                    catch (Exception ex)
                    {
                        m_logger.PubSubInstanceStatusMethodFailed(ex, browseName, componentId);
                        return new ServiceResult(StatusCodes.BadInvalidState, new LocalizedText(ex.Message));
                    }
                }
            };
            status.AddChild(method);
        }

        private async ValueTask ApplyStatusTransition(
            NodeId componentId,
            PubSubState target,
            CancellationToken cancellationToken)
        {
            if (target == PubSubState.PreOperational)
            {
                await EnableComponentAsync(componentId, cancellationToken).ConfigureAwait(false);
                return;
            }

            await DisableComponentAsync(componentId, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask EnableComponentAsync(NodeId componentId, CancellationToken cancellationToken)
        {
            if (TryGetConnection(componentId, out IPubSubConnection? connection))
            {
                await connection!.EnableAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (TryGetWriterGroup(componentId, out WriterGroup? writerGroup))
            {
                await writerGroup!.EnableAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (TryGetReaderGroup(componentId, out ReaderGroup? readerGroup))
            {
                await readerGroup!.EnableAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (TryGetDataSetWriter(componentId, out IDataSetWriter? writer))
            {
                _ = writer!.State.TryEnable();
                _ = writer.State.TryMarkOperational();
                return;
            }

            if (TryGetDataSetReader(componentId, out IDataSetReader? reader))
            {
                _ = reader!.State.TryEnable();
                _ = reader.State.TryMarkOperational();
                return;
            }

            throw new ArgumentException("The specified PubSub component does not exist.", nameof(componentId));
        }

        private async ValueTask DisableComponentAsync(NodeId componentId, CancellationToken cancellationToken)
        {
            if (TryGetConnection(componentId, out IPubSubConnection? connection))
            {
                await connection!.DisableAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (TryGetWriterGroup(componentId, out WriterGroup? writerGroup))
            {
                await writerGroup!.DisableAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (TryGetReaderGroup(componentId, out ReaderGroup? readerGroup))
            {
                await readerGroup!.DisableAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (TryGetDataSetWriter(componentId, out IDataSetWriter? writer))
            {
                _ = writer!.State.TryDisable();
                return;
            }

            if (TryGetDataSetReader(componentId, out IDataSetReader? reader))
            {
                _ = reader!.State.TryDisable();
                return;
            }

            throw new ArgumentException("The specified PubSub component does not exist.", nameof(componentId));
        }

        private bool TryGetConnection(NodeId componentId, out IPubSubConnection? connection)
        {
            string? id = componentId.IdentifierAsString;
            const string prefix = "pubsub:connection:";
            if (id is not null && id.StartsWith(prefix, StringComparison.Ordinal))
            {
                string connectionName = id[prefix.Length..];
                connection = m_application.Connections.FirstOrDefault(c =>
                    StringComparer.Ordinal.Equals(c.Name, connectionName));
                return connection is not null;
            }

            connection = null;
            return false;
        }

        private bool TryGetWriterGroup(NodeId componentId, out WriterGroup? writerGroup)
        {
            string[] parts = SplitNodeId(componentId);
            if (parts.Length == 4 &&
                parts[0] == "pubsub" &&
                parts[1] == "writer-group")
            {
                foreach (IPubSubConnection connection in m_application.Connections)
                {
                    if (!StringComparer.Ordinal.Equals(connection.Name, parts[2]))
                    {
                        continue;
                    }

                    foreach (IWriterGroup group in connection.WriterGroups)
                    {
                        if (group is WriterGroup runtimeGroup &&
                            StringComparer.Ordinal.Equals(runtimeGroup.Name, parts[3]))
                        {
                            writerGroup = runtimeGroup;
                            return true;
                        }
                    }
                }
            }

            writerGroup = null;
            return false;
        }

        private bool TryGetReaderGroup(NodeId componentId, out ReaderGroup? readerGroup)
        {
            string[] parts = SplitNodeId(componentId);
            if (parts.Length == 4 &&
                parts[0] == "pubsub" &&
                parts[1] == "reader-group")
            {
                foreach (IPubSubConnection connection in m_application.Connections)
                {
                    if (!StringComparer.Ordinal.Equals(connection.Name, parts[2]))
                    {
                        continue;
                    }

                    foreach (IReaderGroup group in connection.ReaderGroups)
                    {
                        if (group is ReaderGroup runtimeGroup &&
                            StringComparer.Ordinal.Equals(runtimeGroup.Name, parts[3]))
                        {
                            readerGroup = runtimeGroup;
                            return true;
                        }
                    }
                }
            }

            readerGroup = null;
            return false;
        }

        private bool TryGetDataSetWriter(NodeId componentId, out IDataSetWriter? writer)
        {
            string[] parts = SplitNodeId(componentId);
            if (parts.Length == 5 &&
                parts[0] == "pubsub" &&
                parts[1] == "writer")
            {
                foreach (IPubSubConnection connection in m_application.Connections)
                {
                    if (!StringComparer.Ordinal.Equals(connection.Name, parts[2]))
                    {
                        continue;
                    }

                    foreach (IWriterGroup group in connection.WriterGroups)
                    {
                        if (!StringComparer.Ordinal.Equals(group.Name, parts[3]))
                        {
                            continue;
                        }

                        foreach (IDataSetWriter candidate in group.DataSetWriters)
                        {
                            if (StringComparer.Ordinal.Equals(candidate.Name, parts[4]))
                            {
                                writer = candidate;
                                return true;
                            }
                        }
                    }
                }
            }

            writer = null;
            return false;
        }

        private bool TryGetDataSetReader(NodeId componentId, out IDataSetReader? reader)
        {
            string[] parts = SplitNodeId(componentId);
            if (parts.Length == 5 &&
                parts[0] == "pubsub" &&
                parts[1] == "reader")
            {
                foreach (IPubSubConnection connection in m_application.Connections)
                {
                    if (!StringComparer.Ordinal.Equals(connection.Name, parts[2]))
                    {
                        continue;
                    }

                    foreach (IReaderGroup group in connection.ReaderGroups)
                    {
                        if (!StringComparer.Ordinal.Equals(group.Name, parts[3]))
                        {
                            continue;
                        }

                        foreach (IDataSetReader candidate in group.DataSetReaders)
                        {
                            if (StringComparer.Ordinal.Equals(candidate.Name, parts[4]))
                            {
                                reader = candidate;
                                return true;
                            }
                        }
                    }
                }
            }

            reader = null;
            return false;
        }

        private static string[] SplitNodeId(NodeId componentId)
        {
            return componentId.IdentifierAsString?.Split(':') ?? [];
        }

        private NodeId CreateSecurityGroupNodeId(string securityGroupId)
        {
            return new($"pubsub:security-group:{securityGroupId}", NamespaceIndexes[0]);
        }

        private NodeId CreateKeyPushTargetNodeId(string targetName)
        {
            return new($"pubsub:key-push-target:{targetName}", NamespaceIndexes[0]);
        }

        private PubSubKeyPushTargetRegistration? GetKeyPushTarget(NodeId targetNodeId)
        {
            lock (m_addressSpaceGate)
            {
                return m_keyPushTargets.TryGetValue(targetNodeId, out PubSubKeyPushTargetRegistration? target)
                    ? target
                    : null;
            }
        }

        private ServiceResult PushKeysToTarget(PubSubKeyPushTargetRegistration target)
        {
            if (m_keyService is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }

            PushSecurityKeyProvider? provider = FindPushProvider(target.EndpointUrl);
            if (provider is null)
            {
                return new ServiceResult(StatusCodes.BadNotFound);
            }

            string? securityGroupId = target.SecurityGroupIds.FirstOrDefault();
            if (string.IsNullOrEmpty(securityGroupId))
            {
                return new ServiceResult(StatusCodes.BadInvalidState);
            }

            try
            {
                SksKeyResponse response = m_keyService.GetSecurityKeysAsync(
                    "sks",
                    new SksKeyRequest(securityGroupId, 0, Math.Max(target.RequestedKeyCount, (ushort)1)),
                    [ObjectIds.WellKnownRole_SecurityAdmin])
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                var futureKeys = new List<ByteString>();
                for (int i = 1; i < response.Keys.Count; i++)
                {
                    futureKeys.Add(ByteString.Create(response.Keys[i]));
                }

                provider.SetSecurityKeysAsync(
                    response.SecurityPolicyUri,
                    response.FirstTokenId,
                    ByteString.Create(response.Keys[0]),
                    [.. futureKeys],
                    response.TimeToNextKey,
                    response.KeyLifetime)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                return ServiceResult.Good;
            }
            catch (OpcUaSksException ex)
            {
                return new ServiceResult(ex.Status, new LocalizedText(ex.Message));
            }
        }

        private PushSecurityKeyProvider? FindPushProvider(string endpointUrl)
        {
            for (int i = 0; i < m_pushKeyProviders.Length; i++)
            {
                if (StringComparer.Ordinal.Equals(m_pushKeyProviders[i].SecurityGroupId, endpointUrl))
                {
                    return m_pushKeyProviders[i];
                }
            }

            return null;
        }

        private void BindConnectionMethods(BaseObjectState connectionNode)
        {
            AddInjectedMethod(connectionNode, "AddWriterGroup", MethodHandlers.OnAddWriterGroup, connectionNode.NodeId);
            AddInjectedMethod(connectionNode, "AddReaderGroup", MethodHandlers.OnAddReaderGroup, connectionNode.NodeId);
            AddPlainMethod(connectionNode, "RemoveGroup", MethodHandlers.OnRemoveGroup);
        }

        private void BindDataSetFolderMethods(BaseObjectState folderNode)
        {
            AddPlainMethod(folderNode, "AddPublishedDataItems", MethodHandlers.OnAddPublishedDataItems);
            AddPlainMethod(folderNode, "AddPublishedEvents", MethodHandlers.OnAddPublishedEvents);
            AddPlainMethod(folderNode, "AddPublishedDataItemsTemplate", MethodHandlers.OnAddPublishedDataItemsTemplate);
            AddPlainMethod(folderNode, "RemovePublishedDataSet", MethodHandlers.OnRemovePublishedDataSet);
            AddPlainMethod(folderNode, "AddDataSetFolder", OnAddDataSetFolder);
            AddPlainMethod(folderNode, "RemoveDataSetFolder", OnRemoveDataSetFolder);
        }

        private void BindPublishedDataItemsMethods(BaseObjectState dataSetNode)
        {
            AddPlainMethod(dataSetNode, "AddVariables", MethodHandlers.OnAddVariables);
            AddPlainMethod(dataSetNode, "RemoveVariables", MethodHandlers.OnRemoveVariables);
        }

        private void BindPubSubConfigurationFileMethods(BaseObjectState fileNode)
        {
            AddPlainMethod(fileNode, "SetConfiguration", MethodHandlers.OnSetConfiguration);
            AddPlainMethod(fileNode, "GetConfiguration", MethodHandlers.OnGetConfiguration);
            AddPlainMethod(fileNode, "Open", OnOpenPubSubConfigurationFile);
            AddPlainMethod(fileNode, "Read", OnReadPubSubConfigurationFile);
            AddPlainMethod(fileNode, "Write", OnWritePubSubConfigurationFile);
            AddPlainMethod(fileNode, "Close", OnClosePubSubConfigurationFile);
            AddPlainMethod(fileNode, "ReserveIds", OnReservePubSubConfigurationIds);
            AddPlainMethod(fileNode, "CloseAndUpdate", OnCloseAndUpdatePubSubConfigurationFile);
        }

        private void BindSecurityGroupMethods(BaseObjectState securityGroupNode)
        {
            AddPlainMethod(securityGroupNode, "InvalidateKeys", (context, method, inputs, outputs) =>
                MethodHandlers.OnInvalidateKeys(context, method, securityGroupNode.NodeId, inputs, outputs));
            AddPlainMethod(securityGroupNode, "ForceKeyRotation", (context, method, inputs, outputs) =>
                MethodHandlers.OnForceKeyRotation(context, method, securityGroupNode.NodeId, inputs, outputs));
        }

        private void BindKeyPushTargetMethods(BaseObjectState targetNode)
        {
            AddPlainMethod(targetNode, "ConnectSecurityGroups", OnConnectSecurityGroups);
            AddPlainMethod(targetNode, "DisconnectSecurityGroups", OnDisconnectSecurityGroups);
            AddPlainMethod(targetNode, "TriggerKeyUpdate", OnTriggerKeyUpdate);
        }

        private void BindWriterGroupMethods(BaseObjectState writerGroupNode)
        {
            AddInjectedMethod(writerGroupNode, "AddDataSetWriter", MethodHandlers.OnAddDataSetWriter, writerGroupNode.NodeId);
            AddPlainMethod(writerGroupNode, "RemoveDataSetWriter", MethodHandlers.OnRemoveDataSetWriter);
        }

        private void BindReaderGroupMethods(BaseObjectState readerGroupNode)
        {
            AddInjectedMethod(readerGroupNode, "AddDataSetReader", MethodHandlers.OnAddDataSetReader, readerGroupNode.NodeId);
            AddPlainMethod(readerGroupNode, "RemoveDataSetReader", MethodHandlers.OnRemoveDataSetReader);
        }

        private static void AddInjectedMethod(
            BaseObjectState parent,
            string browseName,
            GenericMethodCalledEventHandler handler,
            NodeId ownerNodeId)
        {
            AddMethod(parent, browseName, (context, method, inputs, outputs) =>
            {
                var injectedValues = new Variant[inputs.Count + 1];
                injectedValues[0] = Variant.From(ownerNodeId);
                for (int i = 0; i < inputs.Count; i++)
                {
                    injectedValues[i + 1] = inputs[i];
                }
                var injected = new ArrayOf<Variant>(injectedValues);
                return handler(context, method, injected, outputs);
            });
        }

        private static void AddPlainMethod(
            BaseObjectState parent,
            string browseName,
            GenericMethodCalledEventHandler handler)
        {
            AddMethod(parent, browseName, handler);
        }

        private static void AddMethod(
            BaseObjectState parent,
            string browseName,
            GenericMethodCalledEventHandler handler)
        {
            string parentId = parent.NodeId.IdentifierAsString;
            var method = new MethodState(parent)
            {
                NodeId = new NodeId($"{parentId}:{browseName}", parent.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName(browseName, parent.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                Executable = true,
                UserExecutable = true,
                OnCallMethod = handler
            };
            parent.AddChild(method);
        }

        private ServiceResult OnGetSecurityGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            ServiceResult result = MethodHandlers.OnGetSecurityGroup(context, method, inputArguments, outputArguments);
            if (StatusCode.IsGood(result.StatusCode))
            {
                RebuildSecurityGroupAddressSpaceAsync(CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            return result;
        }

        private ServiceResult OnAddSecurityGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            ServiceResult result = MethodHandlers.OnAddSecurityGroup(context, method, inputArguments, outputArguments);
            if (StatusCode.IsGood(result.StatusCode))
            {
                RebuildSecurityGroupAddressSpaceAsync(CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            return result;
        }

        private ServiceResult OnRemoveSecurityGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            ServiceResult result = MethodHandlers.OnRemoveSecurityGroup(context, method, inputArguments, outputArguments);
            if (StatusCode.IsGood(result.StatusCode))
            {
                RebuildSecurityGroupAddressSpaceAsync(CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            return result;
        }

        private ServiceResult OnAddPushTarget(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (inputArguments.Count < 6 ||
                !inputArguments[0].TryGetValue(out string? applicationUri) ||
                string.IsNullOrEmpty(applicationUri) ||
                !inputArguments[1].TryGetValue(out string? endpointUrl) ||
                string.IsNullOrEmpty(endpointUrl) ||
                !inputArguments[2].TryGetValue(out string? securityPolicyUri) ||
                string.IsNullOrEmpty(securityPolicyUri) ||
                !inputArguments[3].TryGetValue(out UserTokenType userTokenType) ||
                !inputArguments[4].TryGetValue(out ushort requestedKeyCount) ||
                !inputArguments[5].TryGetValue(out double retryIntervalMs))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            string targetName = applicationUri;
            NodeId targetNodeId = CreateKeyPushTargetNodeId(targetName);
            var target = new PubSubKeyPushTargetRegistration(
                targetName,
                targetNodeId,
                applicationUri,
                endpointUrl,
                securityPolicyUri,
                userTokenType,
                requestedKeyCount,
                TimeSpan.FromMilliseconds(retryIntervalMs));
            lock (m_addressSpaceGate)
            {
                m_keyPushTargets[targetNodeId] = target;
            }

            outputArguments.Add(Variant.From(targetNodeId));
            RebuildKeyPushTargetAddressSpaceAsync(CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return ServiceResult.Good;
        }

        private ServiceResult OnRemovePushTarget(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (inputArguments.Count < 1 ||
                !inputArguments[0].TryGetValue(out NodeId targetNodeId) ||
                targetNodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            bool removed;
            lock (m_addressSpaceGate)
            {
                removed = m_keyPushTargets.Remove(targetNodeId);
            }
            if (!removed)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }

            RebuildKeyPushTargetAddressSpaceAsync(CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return ServiceResult.Good;
        }

        private ServiceResult OnConnectSecurityGroups(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            if (method.Parent?.NodeId is not NodeId targetNodeId ||
                inputArguments.Count < 1 ||
                !inputArguments[0].TryGetValue(out ArrayOf<NodeId> securityGroupIds))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            PubSubKeyPushTargetRegistration? target = GetKeyPushTarget(targetNodeId);
            if (target is null)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }

            var results = new StatusCode[securityGroupIds.Count];
            for (int i = 0; i < securityGroupIds.Count; i++)
            {
                string? securityGroupId = MethodHandlers.LookupSecurityGroupIdForNode(securityGroupIds[i]);
                if (securityGroupId is null)
                {
                    results[i] = StatusCodes.BadNodeIdUnknown;
                    continue;
                }

                target.SecurityGroupIds.Add(securityGroupId);
                results[i] = StatusCodes.Good;
            }

            outputArguments.Add(Variant.From(new ArrayOf<StatusCode>(results)));
            return ServiceResult.Good;
        }

        private ServiceResult OnDisconnectSecurityGroups(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            if (method.Parent?.NodeId is not NodeId targetNodeId ||
                inputArguments.Count < 1 ||
                !inputArguments[0].TryGetValue(out ArrayOf<NodeId> securityGroupIds))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            PubSubKeyPushTargetRegistration? target = GetKeyPushTarget(targetNodeId);
            if (target is null)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }

            var results = new StatusCode[securityGroupIds.Count];
            for (int i = 0; i < securityGroupIds.Count; i++)
            {
                string? securityGroupId = MethodHandlers.LookupSecurityGroupIdForNode(securityGroupIds[i]);
                if (securityGroupId is null || !target.SecurityGroupIds.Remove(securityGroupId))
                {
                    results[i] = StatusCodes.BadNotFound;
                    continue;
                }

                results[i] = StatusCodes.Good;
            }

            outputArguments.Add(Variant.From(new ArrayOf<StatusCode>(results)));
            return ServiceResult.Good;
        }

        private ServiceResult OnTriggerKeyUpdate(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = inputArguments;
            _ = outputArguments;
            if (method.Parent?.NodeId is not NodeId targetNodeId)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            PubSubKeyPushTargetRegistration? target = GetKeyPushTarget(targetNodeId);
            if (target is null)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }

            return PushKeysToTarget(target);
        }

        private ServiceResult OnAddDataSetFolder(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1 ||
                !inputArguments[0].TryGetValue(out string? folderName) ||
                string.IsNullOrEmpty(folderName))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddDataSetFolder argument 0 (Name) is missing or empty."));
            }
            NodeId nodeId = CreateDataSetFolderNodeId(folderName);
            lock (m_addressSpaceGate)
            {
                _ = m_dataSetFolders.Add(folderName);
            }
            RebuildConfigurationAddressSpaceAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            outputArguments.Add(Variant.From(nodeId));
            return ServiceResult.Good;
        }

        private ServiceResult OnRemoveDataSetFolder(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1 ||
                !inputArguments[0].TryGetValue(out NodeId folderNodeId) ||
                folderNodeId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveDataSetFolder argument 0 is not a valid NodeId."));
            }
            string identifier = folderNodeId.IdentifierAsString;
            const string prefix = "pubsub:folder:";
            if (!identifier.StartsWith(prefix, StringComparison.Ordinal))
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }
            lock (m_addressSpaceGate)
            {
                _ = m_dataSetFolders.Remove(identifier[prefix.Length..]);
            }
            RebuildConfigurationAddressSpaceAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            return ServiceResult.Good;
        }

        private ServiceResult OnReservePubSubConfigurationIds(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (inputArguments.Count < 3 ||
                !inputArguments[1].TryGetValue(out ushort writerGroupCount) ||
                !inputArguments[2].TryGetValue(out ushort dataSetWriterCount))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            outputArguments.Add(Variant.Null);
            if (!TryReserveIds(writerGroupCount, out ArrayOf<uint> writerGroupIds) ||
                !TryReserveIds(dataSetWriterCount, out ArrayOf<uint> dataSetWriterIds))
            {
                return new ServiceResult(StatusCodes.BadInvalidState);
            }
            outputArguments.Add(Variant.From(writerGroupIds));
            outputArguments.Add(Variant.From(dataSetWriterIds));
            return ServiceResult.Good;
        }

        private ServiceResult OnOpenPubSubConfigurationFile(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            byte mode = 1;
            if (inputArguments.Count > 0)
            {
                _ = inputArguments[0].TryGetValue(out mode);
            }
            byte[] buffer = IsWriteMode(mode) ? [] : EncodeConfiguration(m_application.GetConfiguration());
            if (!TryAllocateFileHandle(out uint handle))
            {
                return new ServiceResult(StatusCodes.BadInvalidState);
            }
            lock (m_addressSpaceGate)
            {
                m_fileHandles[handle] = new PubSubConfigurationFileHandle(IsWriteMode(mode), buffer);
            }
            outputArguments.Add(Variant.From(handle));
            return ServiceResult.Good;
        }

        private ServiceResult OnReadPubSubConfigurationFile(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (inputArguments.Count < 2 ||
                !inputArguments[0].TryGetValue(out uint handle) ||
                !inputArguments[1].TryGetValue(out int length))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            PubSubConfigurationFileHandle? file = GetFileHandle(handle);
            if (file is null)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            outputArguments.Add(Variant.From(file.Read(length)));
            return ServiceResult.Good;
        }

        private ServiceResult OnWritePubSubConfigurationFile(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (inputArguments.Count < 2 ||
                !inputArguments[0].TryGetValue(out uint handle) ||
                !inputArguments[1].TryGetValue(out ArrayOf<byte> data) ||
                data.IsNull)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            PubSubConfigurationFileHandle? file = GetFileHandle(handle);
            if (file is null || !file.Writable)
            {
                return new ServiceResult(StatusCodes.BadInvalidState);
            }
            file.Write([.. data]);
            return ServiceResult.Good;
        }

        private ServiceResult OnClosePubSubConfigurationFile(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (inputArguments.Count < 1 || !inputArguments[0].TryGetValue(out uint handle))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            lock (m_addressSpaceGate)
            {
                _ = m_fileHandles.Remove(handle, out _);
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnCloseAndUpdatePubSubConfigurationFile(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (inputArguments.Count < 1 || !inputArguments[0].TryGetValue(out uint handle))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            PubSubConfigurationFileHandle? file;
            lock (m_addressSpaceGate)
            {
                _ = m_fileHandles.Remove(handle, out file);
            }
            if (file is null)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            try
            {
                PubSubConfigurationDataType configuration = DecodeConfiguration(file.ToArray());
                _ = m_application.ReplaceConfigurationAsync(configuration)
                    .AsTask().GetAwaiter().GetResult();
                outputArguments.Add(Variant.From(true));
                outputArguments.Add(Variant.From(Array.Empty<StatusCode>()));
                outputArguments.Add(Variant.From(Array.Empty<ExtensionObject>()));
                outputArguments.Add(Variant.From(Array.Empty<NodeId>()));
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.PubSubConfigurationCloseAndUpdateFailed(ex);
                return new ServiceResult(StatusCodes.BadConfigurationError, new LocalizedText(ex.Message));
            }
        }

        private PubSubConfigurationFileHandle? GetFileHandle(uint handle)
        {
            lock (m_addressSpaceGate)
            {
                return m_fileHandles.TryGetValue(handle, out PubSubConfigurationFileHandle? file) ? file : null;
            }
        }

        private byte[] EncodeConfiguration(PubSubConfigurationDataType configuration)
        {
            using var stream = new MemoryStream();
            UaPubSubConfigurationHelper.SaveConfiguration(configuration, stream, m_telemetry);
            return stream.ToArray();
        }

        private PubSubConfigurationDataType DecodeConfiguration(byte[] payload)
        {
            using var stream = new MemoryStream(payload);
            return UaPubSubConfigurationHelper.LoadConfiguration(stream, m_telemetry);
        }

        private static bool IsWriteMode(byte mode)
        {
            return (mode & 0x2) != 0 || (mode & 0x4) != 0;
        }

        private bool TryReserveIds(ushort count, out ArrayOf<uint> ids)
        {
            ids = default;
            ValueTask<ArrayOf<uint>> idTask =
                m_idAllocator.ReserveIdsAsync(count, CancellationToken.None);
            if (!idTask.IsCompletedSuccessfully)
            {
                return false;
            }

            ids = idTask.Result;
            return true;
        }

        private bool TryAllocateFileHandle(out uint handle)
        {
            handle = 0;
            ValueTask<uint> handleTask =
                m_idAllocator.AllocateFileHandleAsync(CancellationToken.None);
            if (!handleTask.IsCompletedSuccessfully)
            {
                return false;
            }

            handle = handleTask.Result;
            return true;
        }

        private static ArrayOf<ExtensionObject> CreateExtensionObjects(
            ArrayOf<PublishedVariableDataType> publishedData)
        {
            if (publishedData.IsNull)
            {
                return [];
            }
            var values = new ExtensionObject[publishedData.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = new ExtensionObject(publishedData[i]);
            }
            return [.. values];
        }

        private NodeId CreateConnectionNodeId(string connectionName)
        {
            return new($"pubsub:connection:{connectionName}", NamespaceIndexes[0]);
        }

        private NodeId CreateWriterGroupNodeId(string connectionName, string writerGroupName)
        {
            return new($"pubsub:writer-group:{connectionName}:{writerGroupName}", NamespaceIndexes[0]);
        }

        private NodeId CreateReaderGroupNodeId(string connectionName, string readerGroupName)
        {
            return new($"pubsub:reader-group:{connectionName}:{readerGroupName}", NamespaceIndexes[0]);
        }

        private NodeId CreateWriterNodeId(string connectionName, string writerGroupName, string writerName)
        {
            return new($"pubsub:writer:{connectionName}:{writerGroupName}:{writerName}", NamespaceIndexes[0]);
        }

        private NodeId CreateReaderNodeId(string connectionName, string readerGroupName, string readerName)
        {
            return new($"pubsub:reader:{connectionName}:{readerGroupName}:{readerName}", NamespaceIndexes[0]);
        }

        private NodeId CreatePublishedDataSetNodeId(string publishedDataSetName)
        {
            return new($"pubsub:published-data-set:{publishedDataSetName}", NamespaceIndexes[0]);
        }

        private NodeId CreateDataSetFolderNodeId(string folderName)
        {
            return new($"pubsub:folder:{folderName}", NamespaceIndexes[0]);
        }

        private void RegisterActionMethodHandlers()
        {
            if (m_actionMethodRegistrations.Length == 0)
            {
                return;
            }

            IMasterNodeManager nodeManager = Server.NodeManager;
            for (int i = 0; i < m_actionMethodRegistrations.Length; i++)
            {
                PubSubActionMethodRegistrar.Register(
                    m_application,
                    nodeManager,
                    m_actionMethodRegistrations[i],
                    m_telemetry);
            }
        }

        private async ValueTask SeedDefaultSecurityGroupAsync(CancellationToken cancellationToken)
        {
            if (m_keyService is null || string.IsNullOrEmpty(m_options.DefaultSecurityGroupId))
            {
                return;
            }
            string id = m_options.DefaultSecurityGroupId!;
            try
            {
                SksSecurityGroup? existing = await m_keyService
                    .GetSecurityGroupAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    return;
                }
                string policyUri = m_options.DefaultSecurityPolicyUri ?? MethodHandlers.DefaultPolicyUri;
                var seed = new SksSecurityGroup(
                    securityGroupId: id,
                    securityPolicyUri: policyUri,
                    keyLifetime: TimeSpan.FromMilliseconds(m_options.DefaultKeyLifetimeMs),
                    maxFutureKeyCount: 4,
                    maxPastKeyCount: 4,
                    keys: Array.Empty<PubSubSecurityKey>(),
                    authorizedCallerIdentities: m_options.DefaultAuthorizedCallerIdentities ?? [],
                    rolePermissions: m_options.DefaultSecurityGroupRolePermissions ?? []);
                await m_keyService.AddSecurityGroupAsync(seed, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.SeedingDefaultSecurityGroupFailed(ex, id);
            }
        }

        private sealed class PubSubKeyPushTargetRegistration
        {
            public PubSubKeyPushTargetRegistration(
                string name,
                NodeId nodeId,
                string applicationUri,
                string endpointUrl,
                string securityPolicyUri,
                UserTokenType userTokenType,
                ushort requestedKeyCount,
                TimeSpan retryInterval)
            {
                Name = name;
                NodeId = nodeId;
                ApplicationUri = applicationUri;
                EndpointUrl = endpointUrl;
                SecurityPolicyUri = securityPolicyUri;
                UserTokenType = userTokenType;
                RequestedKeyCount = requestedKeyCount;
                RetryInterval = retryInterval;
            }

            public string Name { get; }

            public NodeId NodeId { get; }

            public string ApplicationUri { get; }

            public string EndpointUrl { get; }

            public string SecurityPolicyUri { get; }

            public UserTokenType UserTokenType { get; }

            public ushort RequestedKeyCount { get; }

            public TimeSpan RetryInterval { get; }

            public SortedSet<string> SecurityGroupIds { get; } = new(StringComparer.Ordinal);
        }

        private sealed class PubSubConfigurationFileHandle
        {
            private byte[] m_buffer;
            private int m_position;
            private int m_length;

            public PubSubConfigurationFileHandle(bool writable, byte[] initialContent)
            {
                Writable = writable;
                m_buffer = [.. initialContent];
                m_length = m_buffer.Length;
            }

            public bool Writable { get; }

            public byte[] Read(int length)
            {
                if (length < 0)
                {
                    length = 0;
                }
                byte[] buffer = new byte[Math.Min(length, m_length - m_position)];
                Array.Copy(m_buffer, m_position, buffer, 0, buffer.Length);
                m_position += buffer.Length;
                return buffer;
            }

            public void Write(byte[] data)
            {
                int requiredLength = m_position + data.Length;
                if (requiredLength > m_buffer.Length)
                {
                    Array.Resize(ref m_buffer, requiredLength);
                }
                Array.Copy(data, 0, m_buffer, m_position, data.Length);
                m_position += data.Length;
                m_length = Math.Max(m_length, m_position);
            }

            public byte[] ToArray()
            {
                byte[] result = new byte[m_length];
                Array.Copy(m_buffer, 0, result, 0, result.Length);
                return result;
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for PubSubNodeManager.
    /// </summary>
    internal static partial class PubSubNodeManagerLog
    {
        [LoggerMessage(EventId = PubSubServerEventIds.PubSubNodeManager + 0, Level = LogLevel.Warning,
            Message = "DiagnosticsNodeManager is not available; PubSub methods will not be bound.")]
        public static partial void DiagnosticsNodeManagerNotAvailable(this ILogger logger);

        [LoggerMessage(EventId = PubSubServerEventIds.PubSubNodeManager + 1, Level = LogLevel.Debug,
            Message = "IPubSubApplication implementation does not expose IPubSubDiagnostics; status binding skipped.")]
        public static partial void PubSubDiagnosticsNotExposed(this ILogger logger);

        [LoggerMessage(EventId = PubSubServerEventIds.PubSubNodeManager + 2, Level = LogLevel.Warning,
            Message = "PubSub instance Status.{Method} failed for {NodeId}.")]
        public static partial void PubSubInstanceStatusMethodFailed(
            this ILogger logger,
            Exception exception,
            string method,
            NodeId nodeId);

        [LoggerMessage(EventId = PubSubServerEventIds.PubSubNodeManager + 3, Level = LogLevel.Warning,
            Message = "PubSubConfiguration CloseAndUpdate failed.")]
        public static partial void PubSubConfigurationCloseAndUpdateFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = PubSubServerEventIds.PubSubNodeManager + 4, Level = LogLevel.Warning,
            Message = "Seeding default SecurityGroup {Id} failed.")]
        public static partial void SeedingDefaultSecurityGroupFailed(
            this ILogger logger,
            Exception exception,
            string id);
    }

}
