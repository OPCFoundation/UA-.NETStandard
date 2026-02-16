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

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("CoreNodeManager2")]
    [Parallelizable]
    public class CoreNodeManager2Tests
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
                var nodeManager = new CoreNodeManager2(server.CurrentInstance, config);
                nodeManager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

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
                nodeManager.ImportNodes(server.CurrentInstance.DefaultSystemContext, [serverNode], false);

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

                nodeManager.ImportNodes(server.CurrentInstance.DefaultSystemContext, [serverNode2], true);

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
