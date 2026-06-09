using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Parallelizable(ParallelScope.None)]
    public class ConfigurationNodeManagerTests
    {
        [Test]
        public async Task ConfigurationNodeManager_NamespaceMetadata_LifecycleAsync()
        {
            // Arrange
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

            // Get ConfigurationNodeManager
            IServerInternal serverInternal = server.CurrentInstance;
            var configManager = serverInternal.ConfigurationNodeManager as ConfigurationNodeManager;
            Assert.That(configManager, Is.Not.Null, "ConfigurationNodeManager not found or not of expected type");

            const string namespaceUri = "http://test.org/UA/NamespaceMetadataTest";

            // Act 1: CreateNamespaceMetadataState
            NamespaceMetadataState metadata = await configManager.CreateNamespaceMetadataStateAsync(namespaceUri).ConfigureAwait(false);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.NamespaceUri.Value, Is.EqualTo(namespaceUri));

            // Act 2: GetNamespaceMetadataState
            NamespaceMetadataState metadataGet = await configManager.GetNamespaceMetadataStateAsync(namespaceUri).ConfigureAwait(false);
            Assert.That(metadataGet, Is.SameAs(metadata));

            // Act 3: Verify Event Subscription
            bool eventRaised = false;
            configManager.DefaultPermissionsChanged += (s, e) => eventRaised = true;

            // Trigger change
            // Use array initialization
            metadata.DefaultRolePermissions.Value =
            [
                new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Observer, Permissions = (uint)PermissionType.Read }
            ];
            metadata.ClearChangeMasks(serverInternal.DefaultSystemContext, true);

            Assert.That(eventRaised, Is.True, "DefaultPermissionsChanged event should be raised when DefaultRolePermissions changes");

            // Act 4: Dispose Behavior
            eventRaised = false;

            // We call Dispose() on the manager directly to verify it unsubscribes.
            configManager.Dispose();

            // Trigger change again
            metadata.DefaultRolePermissions.Value =
            [
                 new() { RoleId = ObjectIds.WellKnownRole_Observer, Permissions = (uint)PermissionType.Read | (uint)PermissionType.Browse }
            ];

            metadata.ClearChangeMasks(serverInternal.DefaultSystemContext, true);

            Assert.That(eventRaised, Is.False, "DefaultPermissionsChanged event should NOT be raised after Dispose");

            // Cleanup
            await fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ConfigurationNodeManager_ServerNamespaces_Changed_TriggersSubscriptionAsync()
        {
            // Arrange
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

            // Get ConfigurationNodeManager
            IServerInternal serverInternal = server.CurrentInstance;
            var configManager = serverInternal.ConfigurationNodeManager as ConfigurationNodeManager;
            Assert.That(configManager, Is.Not.Null, "ConfigurationNodeManager not found");

            // Find ServerNamespaces node
            // Note: We need to access the node directly to modify it.
            // Since we are in the same process and using StandardServer, we can try to find it via NodeManager.
            // StandardServer.NodeManager is IMasterNodeManager which has FindNodeInAddressSpaceAsync.
            NodeState result = await serverInternal.NodeManager
                .FindNodeInAddressSpaceAsync(ObjectIds.Server_Namespaces)
                .ConfigureAwait(false);
            var serverNamespacesNode = result as NamespacesState;
            Assert.That(serverNamespacesNode, Is.Not.Null, "ServerNamespaces node not found in address space");

            bool eventRaised = false;
            configManager.DefaultPermissionsChanged += (s, e) => eventRaised = true;

            const string manualNamespaceUri = "http://manual.org/UA/Test";

            // Act: Manually add a new metadata node to ServerNamespaces
            // This bypasses CreateNamespaceMetadataState to ensure the event handler picks it up
            var manualMetadata = new NamespaceMetadataState(serverNamespacesNode)
            {
                // Assign a NodeId
                NodeId = new NodeId(Guid.NewGuid(), 1),
                BrowseName = new QualifiedName("ManualMetadata", 1)
            };
            // NamespaceUri property is required for it to be useful, though strictly not for the subscription
            manualMetadata.NamespaceUri = new PropertyState<string>.Implementation<VariantBuilder>(manualMetadata)
            {
                Value = manualNamespaceUri
            };

            // Create DefaultRolePermissions property
            manualMetadata.DefaultRolePermissions = new PropertyState<ArrayOf<RolePermissionType>>
                .Implementation<StructureBuilder<RolePermissionType>>(manualMetadata)
            {
                NodeId = new NodeId(Guid.NewGuid(), 1),
                BrowseName = new QualifiedName(BrowseNames.DefaultRolePermissions, manualMetadata.NodeId.NamespaceIndex),
                Value = [] // Empty initially
            };
            manualMetadata.AddChild(manualMetadata.DefaultRolePermissions);

            // Add to ServerNamespaces
            serverNamespacesNode.AddChild(manualMetadata);

            // Trigger StateChanged on ServerNamespaces (simulating dynamic addition)
            // This is crucial because AddChild alone might not trigger the event depending on implementation
            serverNamespacesNode.ClearChangeMasks(serverInternal.DefaultSystemContext, true);

            // Verify subscription is active by modifying the protected property
            // We expect the event to fire

            manualMetadata.DefaultRolePermissions.Value =
            [
                new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Observer, Permissions = (uint)PermissionType.Read }
            ];

            manualMetadata.ClearChangeMasks(serverInternal.DefaultSystemContext, true);

            Assert.That(eventRaised, Is.True, "DefaultPermissionsChanged event should be raised for manually added metadata node");

            // Cleanup
            await fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ConfigurationNodeManager_NoUserManagement_RemovesUserManagementNodeAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

            IServerInternal serverInternal = server.CurrentInstance;
            Assert.That(serverInternal.UserManagement, Is.Null);

            NodeState userManagementNode = await serverInternal.NodeManager
                .FindNodeInAddressSpaceAsync(ObjectIds.UserManagement)
                .ConfigureAwait(false);
            Assert.That(userManagementNode, Is.Null);

            await fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Issue #3768: Optional descendants of top-level singletons (Server,
        /// ServerConfiguration, HistoryServerCapabilities) that the SDK does
        /// not implement must not be exposed as null-valued nodes in the
        /// address space.
        ///
        /// The generator now applies the suppression TRANSITIVELY to
        /// Variable/Method descendants. Optional Object instances are
        /// intentionally exempt: their subtrees use well-known instance
        /// NodeIds that the SDK / ConfigurationNodeManager bind against, so
        /// they are still emitted by the singleton-specific factory.
        ///
        /// This test asserts:
        ///   - Optional Variables/Methods that the SDK does NOT implement
        ///     are absent (ServerConfiguration.ApplicationUri /
        ///     ProductUri / ApplicationType / HasSecureElement, the
        ///     CancelChanges / ResetToServerDefaults methods, Server's
        ///     UrisVersion / EstimatedReturnTime / LocalTime, the unimplemented
        ///     PublishSubscribe.AddConnection / RemoveConnection methods);
        ///   - Optional Variables/Methods that the SDK DOES implement are
        ///     re-added at the well-known singleton-instance NodeIds
        ///     (Server.Namespaces / GetMonitoredItems / ResendData incl.
        ///     their argument variables; HistoryServerCapabilities.
        ///     ServerTimestampSupported; the ServerCapabilities and
        ///     OperationLimits Optional Property surface area; ServerRedundancy.
        ///     RedundantServerArray);
        ///   - The CertificateGroups Optional Object exemption keeps the
        ///     DefaultHttpsGroup / DefaultUserTokenGroup subtrees emitted at
        ///     their well-known NodeIds, and the DefaultApplicationGroup
        ///     CertificateTypes Variable is resolvable (regression guard
        ///     for the failure described in DiagnosticsNodeManager and
        ///     ConfigurationNodeManager).
        /// </summary>
        [Test]
        public async Task ServerConfiguration_OptionalUnimplementedChildren_NotInAddressSpaceAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

            try
            {
                IServerInternal serverInternal = server.CurrentInstance;

                NodeId[] suppressedNodeIds =
                [
                    // ServerConfiguration Optional Variables/Methods - SDK
                    // does not implement these.
                    VariableIds.ServerConfiguration_ApplicationUri,
                    VariableIds.ServerConfiguration_ProductUri,
                    VariableIds.ServerConfiguration_ApplicationType,
                    VariableIds.ServerConfiguration_HasSecureElement,
                    MethodIds.ServerConfiguration_CancelChanges,
                    MethodIds.ServerConfiguration_ResetToServerDefaults,
                    // PublishSubscribe Optional Methods - SDK does not
                    // implement these (no PubSub configuration provider).
                    MethodIds.PublishSubscribe_AddConnection,
                    MethodIds.PublishSubscribe_RemoveConnection
                ];

                foreach (NodeId suppressedNodeId in suppressedNodeIds)
                {
                    NodeState node = await serverInternal.NodeManager
                        .FindNodeInAddressSpaceAsync(suppressedNodeId)
                        .ConfigureAwait(false);
                    Assert.That(node, Is.Null,
                        $"NodeId {suppressedNodeId} should not be exposed because the SDK does not " +
                        "implement it. See issue #3768.");
                }

                // ServerConfiguration itself must exist.
                NodeState serverConfigurationNode = await serverInternal.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                Assert.That(serverConfigurationNode, Is.Not.Null,
                    "ServerConfiguration itself must exist in the address space.");

                // Optional children that the SDK programmatically adds back must
                // resolve at the well-known singleton-instance NodeIds. The
                // generator-emitted Add{Child} extension uses the type-level
                // factory which assigns the type-level NodeId; the SDK overrides
                // the NodeId after Add{Child}. A missing NodeId override would
                // silently leave the type-level id in place (e.g. 11489 instead
                // of 11492 for GetMonitoredItems), which is invisible at startup
                // but breaks any client that browses by well-known id.
                NodeId[] sdkAddedNodeIds =
                [
                    ObjectIds.Server_Namespaces,
                    MethodIds.Server_GetMonitoredItems,
                    MethodIds.Server_ResendData,
                    VariableIds.HistoryServerCapabilities_ServerTimestampSupported,

                    // Server Optional Variables - re-added by
                    // DiagnosticsNodeManager.AddServerSdkOptionalChildren
                    // (no SDK value source but expected by clients that
                    // browse the standard browse paths).
                    VariableIds.Server_UrisVersion,
                    VariableIds.Server_EstimatedReturnTime,
                    VariableIds.Server_LocalTime,

                    // ServerCapabilities Optional Properties - re-added by
                    // DiagnosticsNodeManager.AddServerCapabilitiesSdkOptionalChildren
                    // (the transitive gate stops the generator from emitting them).
                    VariableIds.Server_ServerCapabilities_MaxArrayLength,
                    VariableIds.Server_ServerCapabilities_MaxStringLength,
                    VariableIds.Server_ServerCapabilities_MaxByteStringLength,
                    VariableIds.Server_ServerCapabilities_MaxSessions,
                    VariableIds.Server_ServerCapabilities_MaxSubscriptions,
                    VariableIds.Server_ServerCapabilities_MaxMonitoredItems,
                    VariableIds.Server_ServerCapabilities_MaxSubscriptionsPerSession,
                    VariableIds.Server_ServerCapabilities_MaxMonitoredItemsPerSubscription,
                    VariableIds.Server_ServerCapabilities_MaxSelectClauseParameters,
                    VariableIds.Server_ServerCapabilities_MaxWhereClauseParameters,
                    VariableIds.Server_ServerCapabilities_MaxMonitoredItemsQueueSize,
                    VariableIds.Server_ServerCapabilities_ConformanceUnits,

                    // OperationLimits Optional Properties - re-added by
                    // DiagnosticsNodeManager.AddOperationLimitsSdkOptionalChildren.
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall,

                    // ServerRedundancy Optional Property - re-added by
                    // DiagnosticsNodeManager.AddServerRedundancySdkOptionalChildren.
                    VariableIds.Server_ServerRedundancy_RedundantServerArray,

                    // CertificateGroups regression guard: Optional Object
                    // instances and their Mandatory CertificateTypes children
                    // are emitted by the singleton factory (the gate exempts
                    // ObjectDesign children to preserve their subtrees'
                    // well-known instance NodeIds, which the SDK and
                    // ConfigurationNodeManager bind against).
                    VariableIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_CertificateTypes,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup,

                    // WellKnownRole regression guard: the StandardTypes.xml
                    // declarations promote RoleType's Optional children to
                    // Mandatory on each modifiable well-known role
                    // (Observer, Operator, Engineer, Supervisor,
                    // ConfigureAdmin, SecurityAdmin). The transitive gate
                    // suppresses them at the generator level;
                    // DiagnosticsNodeManager.AddWellKnownRoleSdkOptionalChildren
                    // re-adds them so RoleStateBinding finds them and so
                    // standard Role-based access tests pass (one sample per
                    // role child for fast feedback).
                    MethodIds.WellKnownRole_Observer_AddIdentity,
                    VariableIds.WellKnownRole_Observer_ApplicationsExclude,
                    MethodIds.WellKnownRole_Observer_AddApplication,
                    MethodIds.WellKnownRole_Operator_AddEndpoint,
                    MethodIds.WellKnownRole_Engineer_RemoveIdentity,
                    MethodIds.WellKnownRole_Supervisor_RemoveApplication,
                    MethodIds.WellKnownRole_ConfigureAdmin_RemoveEndpoint,
                    MethodIds.WellKnownRole_SecurityAdmin_AddIdentity
                ];

                foreach (NodeId sdkAddedNodeId in sdkAddedNodeIds)
                {
                    NodeState node = await serverInternal.NodeManager
                        .FindNodeInAddressSpaceAsync(sdkAddedNodeId)
                        .ConfigureAwait(false);
                    Assert.That(node, Is.Not.Null,
                        $"NodeId {sdkAddedNodeId} must be exposed at its " +
                        "well-known instance-level NodeId by the SDK.");
                    Assert.That(node!.NodeId, Is.EqualTo(sdkAddedNodeId),
                        $"Resolved NodeId {node.NodeId} does not match the " +
                        $"well-known instance-level NodeId {sdkAddedNodeId} - " +
                        "AddServerSdkOptionalChildren may be missing a NodeId " +
                        "patch (see #3768).");
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
