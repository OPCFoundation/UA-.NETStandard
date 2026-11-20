// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("CoreNodeManager")]
    [Parallelizable]
    public class CoreNodeManagerTests
    {
        [Test]
        public async Task ImportNodes_IsInternal_UpdatesDiagnosticsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

                // Create a CoreNodeManager2
                var config = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 100
                    }
                };
                using var nodeManager = new CoreNodeManager(server.CurrentInstance, config);
                await nodeManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

                // Create a node in Namespace 0 that also exists in DiagnosticsNodeManager (e.g. Server Object)
                // Note: We need a node that exists in DiagnosticsNodeManager. StandardServer populates it with BaseNodes.
                // Let's use ObjectIds.Server.
                var serverNode = new BaseObjectState(null)
                {
                    NodeId = ObjectIds.Server,
                    BrowseName = new QualifiedName(BrowseNames.Server, 0),
                    DisplayName = new LocalizedText("Server")
                };

                // Add a reference that we want to check
                var targetNodeId = new NodeId(1234, 1); // Some random target
                serverNode.AddReference(ReferenceTypeIds.HasComponent, false, targetNodeId);

                // Act - isInternal = false
                await nodeManager.ImportNodesAsync(server.CurrentInstance.DefaultSystemContext, [serverNode], false).ConfigureAwait(false);

                // Assert
                // Check if DiagnosticsNodeManager has the reference
                NodeState diagNode = server.CurrentInstance.DiagnosticsNodeManager.FindPredefinedNode<NodeState>(ObjectIds.Server);
                Assert.That(diagNode, Is.Not.Null, "Diagnostics node should exist");
                Assert.That(diagNode.ReferenceExists(ReferenceTypeIds.HasComponent, false, targetNodeId), Is.True, "Reference should be added to diagnostics");

                // Cleanup reference to verify isInternal = true
                diagNode.RemoveReference(ReferenceTypeIds.HasComponent, false, targetNodeId);
                Assert.That(diagNode.ReferenceExists(ReferenceTypeIds.HasComponent, false, targetNodeId), Is.False, "Reference should be removed");

                // Act - isInternal = true
                var serverNode2 = new BaseObjectState(null)
                {
                    NodeId = ObjectIds.Server,
                    BrowseName = new QualifiedName(BrowseNames.Server, 0),
                    DisplayName = new LocalizedText("Server")
                };
                serverNode2.AddReference(ReferenceTypeIds.HasComponent, false, targetNodeId);

                await nodeManager.ImportNodesAsync(server.CurrentInstance.DefaultSystemContext, [serverNode2], true).ConfigureAwait(false);

                // Assert
                Assert.That(diagNode.ReferenceExists(ReferenceTypeIds.HasComponent, false, targetNodeId), Is.False,
                    "Reference should NOT be added to diagnostics when isInternal=true");
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
