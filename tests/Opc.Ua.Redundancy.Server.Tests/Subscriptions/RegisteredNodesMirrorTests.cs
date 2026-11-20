/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

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
            var namespaceUris = new NamespaceTable();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            var nodeManagerFactory = new Mock<IMainNodeManagerFactory>();
            var configurationNodeManager = new Mock<IConfigurationNodeManager>();
            configurationNodeManager.Setup(n => n.NamespaceUris).Returns([]);
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
                null);
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
