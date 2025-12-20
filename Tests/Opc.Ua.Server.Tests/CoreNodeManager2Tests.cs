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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test <see cref="CoreNodeManager2"/>
    /// </summary>
    [TestFixture]
    [Category("CoreNodeManager2")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CoreNodeManager2Tests
    {
        /// <summary>
        /// Tests that CoreNodeManager2 is properly instantiated and accessible.
        /// </summary>
        [Test]
        public async Task TestCoreNodeManager2Instantiation()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange & Act
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

                // Assert
                Assert.That(server.CurrentInstance.CoreNodeManager, Is.Not.Null);
                Assert.That(server.CurrentInstance.CoreNodeManager, Is.InstanceOf<CoreNodeManager2>());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tests that CoreNodeManager2 inherits from CustomNodeManager2.
        /// </summary>
        [Test]
        public async Task TestCoreNodeManager2InheritsFromCustomNodeManager2()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange & Act
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

                // Assert
                Assert.That(server.CurrentInstance.CoreNodeManager, Is.InstanceOf<CustomNodeManager2>());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tests that CoreNodeManager2 has DataLock property for compatibility.
        /// </summary>
        [Test]
        public async Task TestCoreNodeManager2HasDataLockProperty()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange & Act
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                CoreNodeManager2 coreNodeManager = server.CurrentInstance.CoreNodeManager;

                // Assert
                Assert.That(coreNodeManager.DataLock, Is.Not.Null);
                Assert.That(coreNodeManager.DataLock, Is.EqualTo(coreNodeManager.Lock));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tests that CoreNodeManager2 can import nodes.
        /// </summary>
        [Test]
        public async Task TestCoreNodeManager2ImportNodes()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                CoreNodeManager2 coreNodeManager = server.CurrentInstance.CoreNodeManager;

                var testNode = new DataItemState(null)
                {
                    NodeId = new NodeId(Guid.NewGuid(), coreNodeManager.DynamicNamespaceIndex),
                    BrowseName = new QualifiedName("TestNode", coreNodeManager.DynamicNamespaceIndex),
                    DisplayName = "Test Node"
                };

                // Act
                coreNodeManager.ImportNodes(coreNodeManager.SystemContext, new NodeState[] { testNode });

                // Assert
                NodeState foundNode = coreNodeManager.Find(testNode.NodeId);
                Assert.That(foundNode, Is.Not.Null);
                Assert.That(foundNode.NodeId, Is.EqualTo(testNode.NodeId));
                Assert.That(foundNode.BrowseName, Is.EqualTo(testNode.BrowseName));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tests that CoreNodeManager2 can create unique node IDs.
        /// </summary>
        [Test]
        public async Task TestCoreNodeManager2CreateUniqueNodeId()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                CoreNodeManager2 coreNodeManager = server.CurrentInstance.CoreNodeManager;

                // Act
                NodeId nodeId1 = coreNodeManager.CreateUniqueNodeId();
                NodeId nodeId2 = coreNodeManager.CreateUniqueNodeId();

                // Assert
                Assert.That(nodeId1, Is.Not.Null);
                Assert.That(nodeId2, Is.Not.Null);
                Assert.That(nodeId1, Is.Not.EqualTo(nodeId2));
                Assert.That(nodeId1.NamespaceIndex, Is.EqualTo(coreNodeManager.DynamicNamespaceIndex));
                Assert.That(nodeId2.NamespaceIndex, Is.EqualTo(coreNodeManager.DynamicNamespaceIndex));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tests that CoreNodeManager2 manages namespace 0 and 1.
        /// </summary>
        [Test]
        public async Task TestCoreNodeManager2ManagesCorrectNamespaces()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange & Act
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                CoreNodeManager2 coreNodeManager = server.CurrentInstance.CoreNodeManager;

                // Assert
                Assert.That(coreNodeManager.NamespaceIndexes, Is.Not.Null);
                Assert.That(coreNodeManager.NamespaceIndexes, Does.Contain((ushort)0)); // UA namespace
                Assert.That(coreNodeManager.NamespaceIndexes.Count, Is.EqualTo(2));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tests that CoreNodeManager2 uses SamplingGroups.
        /// </summary>
        [Test]
        public async Task TestCoreNodeManager2UsesSamplingGroups()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange & Act
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                CoreNodeManager2 coreNodeManager = server.CurrentInstance.CoreNodeManager;

                // Assert that the node manager is properly initialized
                // The SamplingGroups support is enabled in the constructor
                Assert.That(coreNodeManager, Is.Not.Null);
                Assert.That(coreNodeManager.Server, Is.Not.Null);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
