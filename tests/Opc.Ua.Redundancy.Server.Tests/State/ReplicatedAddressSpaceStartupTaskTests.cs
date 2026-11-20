// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#pragma warning disable CA2007

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Tests for <see cref="ReplicatedAddressSpaceStartupTask"/>: it attaches a CRDT
    /// synchronizer to every opted-in node manager and skips the rest.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class ReplicatedAddressSpaceStartupTaskTests
    {
        private const ushort NamespaceIndex = 2;

        [Test]
        public async Task AttachesSynchronizerToOptedInNodeManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:crdt-application");
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:crdt-startup");
            var identity = new ReplicaNodeIdFactory("crdt-startup", ["urn:test:crdt-startup"]);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };

            var addressSpace = new DictionaryAddressSpace(systemContext);
            await addressSpace.AddOrUpdateNodeAsync(new BaseDataVariableState(null)
            {
                NodeId = new NodeId("seeded", NamespaceIndex),
                BrowseName = new QualifiedName("Seeded", NamespaceIndex),
                DisplayName = new LocalizedText("Seeded"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(1.0)
            }).ConfigureAwait(false);

            var optedIn = new Mock<INodeManager>();
            optedIn.Setup(s => s.NamespaceUris).Returns(["urn:test:crdt-startup"]);
            optedIn.As<ILocalAddressSpaceSource>()
                .Setup(s => s.CreateLocalAddressSpace())
                .Returns(addressSpace);
            var notOptedIn = new Mock<INodeManager>();

            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.NodeManagers)
                .Returns([optedIn.Object, notOptedIn.Object]);

            var server = new Mock<IServerInternal>();
            server.As<INodeIdFactoryProvider>().Setup(s => s.NodeIdFactory).Returns(identity);
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server.Setup(s => s.FindNodeManagers<ILocalAddressSpaceSource>())
                .Returns([optedIn.As<ILocalAddressSpaceSource>().Object]);

            await using var task = new ReplicatedAddressSpaceStartupTask(
                EmptyServices(), new ReplicatedAddressSpaceOptions());

            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            // The opted-in manager exposed its local address space.
            optedIn.As<ILocalAddressSpaceSource>().Verify(s => s.CreateLocalAddressSpace(), Times.Once);
        }

        [Test]
        public void ConstructorRejectsNullArguments()
        {
            Assert.That(
                () => new ReplicatedAddressSpaceStartupTask(null!, new ReplicatedAddressSpaceOptions()),
                Throws.ArgumentNullException);
            Assert.That(
                () => new ReplicatedAddressSpaceStartupTask(EmptyServices(), null!),
                Throws.ArgumentNullException);
        }

        private static IServiceProvider EmptyServices()
        {
            return Mock.Of<IServiceProvider>();
        }
    }
}
