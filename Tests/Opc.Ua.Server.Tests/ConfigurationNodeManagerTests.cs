using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Server;
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
            NamespaceMetadataState metadata = configManager.CreateNamespaceMetadataState(namespaceUri);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.NamespaceUri.Value, Is.EqualTo(namespaceUri));

            // Act 2: GetNamespaceMetadataState
            NamespaceMetadataState metadataGet = configManager.GetNamespaceMetadataState(namespaceUri);
            Assert.That(metadataGet, Is.SameAs(metadata));

            // Act 3: Verify Event Subscription
            bool eventRaised = false;
            configManager.DefaultPermissionsChanged += (s, e) => { eventRaised = true; };

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
        public async Task ConfigurationNodeManager_ServerNamespaces_Changed_TriggersSubscription()
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
            var result = await serverInternal.NodeManager
                .FindNodeInAddressSpaceAsync(ObjectIds.Server_Namespaces)
                .ConfigureAwait(false);
            var serverNamespacesNode = result as NamespacesState;
            Assert.That(serverNamespacesNode, Is.Not.Null, "ServerNamespaces node not found in address space");

            bool eventRaised = false;
            configManager.DefaultPermissionsChanged += (s, e) => { eventRaised = true; };

            string manualNamespaceUri = "http://manual.org/UA/Test";

            // Act: Manually add a new metadata node to ServerNamespaces
            // This bypasses CreateNamespaceMetadataState to ensure the event handler picks it up
            var manualMetadata = new NamespaceMetadataState(serverNamespacesNode);
            // Assign a NodeId
            manualMetadata.NodeId = new NodeId(Guid.NewGuid(), 1);
            manualMetadata.BrowseName = new QualifiedName("ManualMetadata", 1);
            // NamespaceUri property is required for it to be useful, though strictly not for the subscription
            manualMetadata.NamespaceUri = new PropertyState<string>(manualMetadata) { Value = manualNamespaceUri };
            
            // Create DefaultRolePermissions property
            manualMetadata.DefaultRolePermissions = new PropertyState<RolePermissionType[]>(manualMetadata);
            manualMetadata.DefaultRolePermissions.NodeId = new NodeId(Guid.NewGuid(), 1);
            manualMetadata.DefaultRolePermissions.BrowseName = new QualifiedName(BrowseNames.DefaultRolePermissions, manualMetadata.NodeId.NamespaceIndex);
            manualMetadata.DefaultRolePermissions.Value = []; // Empty initially
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
    }
}
