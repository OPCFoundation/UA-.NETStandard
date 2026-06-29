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

#nullable enable

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Verifies that registered node ids are already replica-stable.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("RegisteredNodes")]
    public class RegisteredNodesMirrorTests
    {
        [Test]
        public void RegisterNodesReturnsInputNodeIdsWithoutReplicaLocalAliases()
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            var nodeManagerFactory = new Mock<IMainNodeManagerFactory>();
            var configurationNodeManager = new Mock<IConfigurationNodeManager>();
            configurationNodeManager.Setup(n => n.NamespaceUris).Returns(Array.Empty<string>());
            var coreNodeManager = new Mock<ICoreNodeManager>();
            nodeManagerFactory
                .Setup(f => f.CreateConfigurationNodeManager())
                .Returns(configurationNodeManager.Object);
            nodeManagerFactory
                .Setup(f => f.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(coreNodeManager.Object);
            server.Setup(s => s.MainNodeManagerFactory).Returns(nodeManagerFactory.Object);
            var manager = new MasterNodeManager(
                server.Object,
                new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxBrowseContinuationPoints = 10
                    }
                },
                null,
                Array.Empty<IAsyncNodeManager>());
            ArrayOf<NodeId> input =
            [
                new NodeId("Temperature", 2),
                ObjectIds.Server
            ];

            manager.RegisterNodes(
                new OperationContext(new Mock<ISession>().Object, DiagnosticsMasks.None),
                input,
                out ArrayOf<NodeId> registered);

            Assert.That(registered, Is.EqualTo(input));
        }
    }
}