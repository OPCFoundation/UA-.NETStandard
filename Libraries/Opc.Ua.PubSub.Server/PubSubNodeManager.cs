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
using Opc.Ua.PubSub.Diagnostics;
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
        private const uint AddPublishedDataItemsNodeId = 14479;
        private const uint AddPublishedEventsNodeId = 14482;
        private const uint AddPublishedDataItemsTemplateNodeId = 16842;
        private const uint RemovePublishedDataSetNodeId = 14485;
        private const uint AddDataSetFolderNodeId = 16884;
        private const uint RemoveDataSetFolderNodeId = 16923;
        private static readonly NodeId s_publishedDataSetsNodeId = new(14478u);

        private readonly IPubSubApplication m_application;
        private readonly IPubSubKeyServiceServer? m_keyService;
        private readonly PubSubServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly PubSubMethodHandlers m_methodHandlers;
        private readonly PubSubActionMethodRegistration[] m_actionMethodRegistrations;
        private readonly System.Threading.Lock m_addressSpaceGate = new();
        private readonly List<NodeState> m_dynamicRoots = [];
        private readonly SortedSet<string> m_dataSetFolders = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, PubSubConfigurationFileHandle> m_fileHandles = [];
        private IDiagnosticsNodeManager? m_diagnosticsNodeManager;
        private PubSubStatusBinding? m_statusBinding;
        private bool m_methodsBound;
        private uint m_nextFileHandle;
        private uint m_nextReservedId;

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
        public PubSubNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IPubSubApplication pubSubApplication,
            IPubSubKeyServiceServer? sksServer,
            PubSubServerOptions options,
            ITelemetryContext telemetry,
            IEnumerable<PubSubActionMethodRegistration>? actionMethodRegistrations = null,
            IEnumerable<PushSecurityKeyProvider>? pushKeyProviders = null)
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
                ?? Array.Empty<PubSubActionMethodRegistration>();
            m_methodHandlers = new PubSubMethodHandlers(
                pubSubApplication,
                options.ExposeSecurityKeyService ? sksServer : null,
                options,
                telemetry,
                pushKeyProviders);
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
        internal PubSubMethodHandlers MethodHandlers => m_methodHandlers;

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
                m_logger.LogWarning(
                    "DiagnosticsNodeManager is not available; PubSub methods will not be bound.");
                return;
            }

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
                m_logger.LogDebug(
                    "IPubSubApplication implementation does not expose IPubSubDiagnostics; status binding skipped.");
            }

            if (m_options.ExposeSecurityKeyService &&
                m_keyService is not null &&
                !string.IsNullOrEmpty(m_options.DefaultSecurityGroupId))
            {
                await SeedDefaultSecurityGroupAsync(cancellationToken).ConfigureAwait(false);
            }
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

            if (enable is not null)
            {
                enable.OnCallMethod = m_methodHandlers.OnEnable;
            }
            if (disable is not null)
            {
                disable.OnCallMethod = m_methodHandlers.OnDisable;
            }
            if (m_options.ExposeConfigurationMethods)
            {
                if (setKeys is not null)
                {
                    setKeys.OnCallMethod = m_methodHandlers.OnSetSecurityKeys;
                }
                if (addConn is not null)
                {
                    addConn.OnCallMethod = m_methodHandlers.OnAddConnection;
                }
                if (removeConn is not null)
                {
                    removeConn.OnCallMethod = m_methodHandlers.OnRemoveConnection;
                }
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
                if (getKeys is not null)
                {
                    getKeys.OnCallMethod2 = m_methodHandlers.OnGetSecurityKeys;
                }
                if (getGroup is not null)
                {
                    getGroup.OnCallMethod = m_methodHandlers.OnGetSecurityGroup;
                }
                if (addGroup is not null)
                {
                    addGroup.OnCallMethod = m_methodHandlers.OnAddSecurityGroup;
                }
                if (removeGroup is not null)
                {
                    removeGroup.OnCallMethod = m_methodHandlers.OnRemoveSecurityGroup;
                }
            }

            m_methodsBound = enable is not null || disable is not null;
        }

        private void BindPublishedDataSetFolderMethods(IDiagnosticsNodeManager diagnosticsNodeManager)
        {
            BindStandardMethod(diagnosticsNodeManager, AddPublishedDataItemsNodeId, m_methodHandlers.OnAddPublishedDataItems);
            BindStandardMethod(diagnosticsNodeManager, AddPublishedEventsNodeId, m_methodHandlers.OnAddPublishedEvents);
            BindStandardMethod(
                diagnosticsNodeManager,
                AddPublishedDataItemsTemplateNodeId,
                m_methodHandlers.OnAddPublishedDataItemsTemplate);
            BindStandardMethod(diagnosticsNodeManager, RemovePublishedDataSetNodeId, m_methodHandlers.OnRemovePublishedDataSet);
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
            if (method is not null)
            {
                method.OnCallMethod = handler;
            }
        }

        private void OnConfigurationChanged(
            object? sender,
            Configuration.PubSubConfigurationChangedEventArgs e)
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
            lock (m_addressSpaceGate)
            {
                oldRoots = [.. m_dynamicRoots];
                m_dynamicRoots.Clear();
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
                new NodeId("pubsub:configuration", 0),
                "PubSubConfiguration",
                new NodeId(25482u));
            BindPubSubConfigurationFileMethods(configurationFile);
            newRoots.Add(configurationFile);

            if (publishedDataSets is not null)
            {
                foreach (string folderName in m_dataSetFolders)
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
            var method = new MethodState(status)
            {
                NodeId = new NodeId($"{statusId}:{browseName}", status.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName(browseName, status.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                Executable = true,
                UserExecutable = true,
                OnCallMethod = (_, _, _, _) =>
                {
                    state.Value = Variant.From((int)target);
                    state.Timestamp = DateTime.UtcNow;
                    return ServiceResult.Good;
                }
            };
            status.AddChild(method);
        }

        private void BindConnectionMethods(BaseObjectState connectionNode)
        {
            AddInjectedMethod(connectionNode, "AddWriterGroup", m_methodHandlers.OnAddWriterGroup, connectionNode.NodeId);
            AddInjectedMethod(connectionNode, "AddReaderGroup", m_methodHandlers.OnAddReaderGroup, connectionNode.NodeId);
            AddPlainMethod(connectionNode, "RemoveGroup", m_methodHandlers.OnRemoveGroup);
        }

        private void BindDataSetFolderMethods(BaseObjectState folderNode)
        {
            AddPlainMethod(folderNode, "AddPublishedDataItems", m_methodHandlers.OnAddPublishedDataItems);
            AddPlainMethod(folderNode, "AddPublishedEvents", m_methodHandlers.OnAddPublishedEvents);
            AddPlainMethod(folderNode, "AddPublishedDataItemsTemplate", m_methodHandlers.OnAddPublishedDataItemsTemplate);
            AddPlainMethod(folderNode, "RemovePublishedDataSet", m_methodHandlers.OnRemovePublishedDataSet);
            AddPlainMethod(folderNode, "AddDataSetFolder", OnAddDataSetFolder);
            AddPlainMethod(folderNode, "RemoveDataSetFolder", OnRemoveDataSetFolder);
        }

        private void BindPublishedDataItemsMethods(BaseObjectState dataSetNode)
        {
            AddPlainMethod(dataSetNode, "AddVariables", m_methodHandlers.OnAddVariables);
            AddPlainMethod(dataSetNode, "RemoveVariables", m_methodHandlers.OnRemoveVariables);
        }

        private void BindPubSubConfigurationFileMethods(BaseObjectState fileNode)
        {
            AddPlainMethod(fileNode, "Open", OnOpenPubSubConfigurationFile);
            AddPlainMethod(fileNode, "Read", OnReadPubSubConfigurationFile);
            AddPlainMethod(fileNode, "Write", OnWritePubSubConfigurationFile);
            AddPlainMethod(fileNode, "Close", OnClosePubSubConfigurationFile);
            AddPlainMethod(fileNode, "ReserveIds", OnReservePubSubConfigurationIds);
            AddPlainMethod(fileNode, "CloseAndUpdate", OnCloseAndUpdatePubSubConfigurationFile);
        }

        private void BindWriterGroupMethods(BaseObjectState writerGroupNode)
        {
            AddInjectedMethod(writerGroupNode, "AddDataSetWriter", m_methodHandlers.OnAddDataSetWriter, writerGroupNode.NodeId);
            AddPlainMethod(writerGroupNode, "RemoveDataSetWriter", m_methodHandlers.OnRemoveDataSetWriter);
        }

        private void BindReaderGroupMethods(BaseObjectState readerGroupNode)
        {
            AddInjectedMethod(readerGroupNode, "AddDataSetReader", m_methodHandlers.OnAddDataSetReader, readerGroupNode.NodeId);
            AddPlainMethod(readerGroupNode, "RemoveDataSetReader", m_methodHandlers.OnRemoveDataSetReader);
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
            outputArguments.Add(Variant.From(ReserveIds(writerGroupCount)));
            outputArguments.Add(Variant.From(ReserveIds(dataSetWriterCount)));
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
            uint handle = ++m_nextFileHandle;
            byte[] buffer = IsWriteMode(mode) ? [] : EncodeConfiguration(m_application.GetConfiguration());
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
            PubSubConfigurationFileHandle? file;
            lock (m_addressSpaceGate)
            {
                _ = m_fileHandles.Remove(handle, out file);
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
                m_logger.LogWarning(ex, "PubSubConfiguration CloseAndUpdate failed.");
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

        private ArrayOf<uint> ReserveIds(ushort count)
        {
            var ids = new uint[count];
            lock (m_addressSpaceGate)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    ids[i] = ++m_nextReservedId;
                }
            }
            return new ArrayOf<uint>(ids);
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

        private static NodeId CreateConnectionNodeId(string connectionName)
        {
            return new($"pubsub:connection:{connectionName}", 0);
        }

        private static NodeId CreateWriterGroupNodeId(string connectionName, string writerGroupName)
        {
            return new($"pubsub:writer-group:{connectionName}:{writerGroupName}", 0);
        }

        private static NodeId CreateReaderGroupNodeId(string connectionName, string readerGroupName)
        {
            return new($"pubsub:reader-group:{connectionName}:{readerGroupName}", 0);
        }

        private static NodeId CreateWriterNodeId(string connectionName, string writerGroupName, string writerName)
        {
            return new($"pubsub:writer:{connectionName}:{writerGroupName}:{writerName}", 0);
        }

        private static NodeId CreateReaderNodeId(string connectionName, string readerGroupName, string readerName)
        {
            return new($"pubsub:reader:{connectionName}:{readerGroupName}:{readerName}", 0);
        }

        private static NodeId CreatePublishedDataSetNodeId(string publishedDataSetName)
        {
            return new($"pubsub:published-data-set:{publishedDataSetName}", 0);
        }

        private static NodeId CreateDataSetFolderNodeId(string folderName)
        {
            return new($"pubsub:folder:{folderName}", 0);
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
                string policyUri = m_options.DefaultSecurityPolicyUri ?? m_methodHandlers.DefaultPolicyUri;
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
                m_logger.LogWarning(ex, "Seeding default SecurityGroup {Id} failed.", id);
            }
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
                var buffer = new byte[Math.Min(length, m_length - m_position)];
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
                var result = new byte[m_length];
                Array.Copy(m_buffer, 0, result, 0, result.Length);
                return result;
            }
        }
    }
}
