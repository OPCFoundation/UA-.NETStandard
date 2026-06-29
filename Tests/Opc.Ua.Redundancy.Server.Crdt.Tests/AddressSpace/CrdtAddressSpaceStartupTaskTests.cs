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

#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Tests for <see cref="CrdtAddressSpaceStartupTask"/>: it attaches a CRDT
    /// synchronizer to every opted-in node manager and skips the rest.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class CrdtAddressSpaceStartupTaskTests
    {
        private const ushort NamespaceIndex = 1;

        [Test]
        public async Task AttachesSynchronizerToOptedInNodeManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:crdt-startup");
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
            });

            var optedIn = new Mock<INodeManager>();
            optedIn.As<ILocalAddressSpaceSource>()
                .Setup(s => s.CreateLocalAddressSpace())
                .Returns(addressSpace);
            var notOptedIn = new Mock<INodeManager>();

            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.NodeManagers)
                .Returns(new[] { optedIn.Object, notOptedIn.Object });

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);

            await using var task = new CrdtAddressSpaceStartupTask(
                EmptyServices(), new ReplicatedAddressSpaceOptions());

            await task.OnServerStartedAsync(server.Object);

            // The opted-in manager exposed its local address space.
            optedIn.As<ILocalAddressSpaceSource>().Verify(s => s.CreateLocalAddressSpace(), Times.Once);
        }

        [Test]
        public void ConstructorRejectsNullArguments()
        {
            Assert.That(
                () => new CrdtAddressSpaceStartupTask(null!, new ReplicatedAddressSpaceOptions()),
                Throws.ArgumentNullException);
            Assert.That(
                () => new CrdtAddressSpaceStartupTask(EmptyServices(), null!),
                Throws.ArgumentNullException);
        }

        private static IServiceProvider EmptyServices()
        {
            return Mock.Of<IServiceProvider>();
        }
    }
}