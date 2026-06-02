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
        /// Issue #3768: ServerConfiguration's Optional children that the SDK does not
        /// implement (ApplicationUri, ProductUri, ApplicationType, HasSecureElement,
        /// CancelChanges, ResetToServerDefaults) must not be exposed as null-valued
        /// nodes in the address space.
        /// Also asserts that the SDK-added Optional children at the well-known
        /// singleton-instance NodeIds (Server.GetMonitoredItems / ResendData /
        /// Namespaces, and HistoryServerCapabilities.ServerTimestampSupported,
        /// plus their Method InputArguments / OutputArguments where applicable)
        /// resolve to the correct instance-level NodeIds — guarding against the
        /// "Add{Child} uses the type-level factory" footgun described in
        /// DiagnosticsNodeManager.AddServerSdkOptionalChildren.
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
                    VariableIds.ServerConfiguration_ApplicationUri,
                    VariableIds.ServerConfiguration_ProductUri,
                    VariableIds.ServerConfiguration_ApplicationType,
                    VariableIds.ServerConfiguration_HasSecureElement,
                    MethodIds.ServerConfiguration_CancelChanges,
                    MethodIds.ServerConfiguration_ResetToServerDefaults
                ];

                foreach (NodeId suppressedNodeId in suppressedNodeIds)
                {
                    NodeState node = await serverInternal.NodeManager
                        .FindNodeInAddressSpaceAsync(suppressedNodeId)
                        .ConfigureAwait(false);
                    Assert.That(node, Is.Null,
                        $"NodeId {suppressedNodeId} should not be exposed as an SDK does not " +
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
                    VariableIds.Server_GetMonitoredItems_InputArguments,
                    VariableIds.Server_GetMonitoredItems_OutputArguments,
                    MethodIds.Server_ResendData,
                    VariableIds.Server_ResendData_InputArguments,
                    VariableIds.HistoryServerCapabilities_ServerTimestampSupported
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
