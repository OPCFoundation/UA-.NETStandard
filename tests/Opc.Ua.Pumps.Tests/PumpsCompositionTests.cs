/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Exercises direct composition, namespace ownership and lifecycle through the public runtime.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [NonParallelizable]
    public sealed class PumpsCompositionTests
    {
        [Test]
        public async Task StockOwnerLoadsDependenciesAndKeepsOptionalGroupsLazyAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            PumpsRuntime runtime = fixture.Manager.CreatePumpsRuntime();
            Assert.That(fixture.Manager.CreatePumpsRuntime(), Is.SameAs(runtime));

            IPumpBuilder pump = await runtime.CreatePumpAsync(
                new QualifiedName("Feed", runtime.InstanceNamespaceIndex));
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Manager, Is.SameAs(fixture.Manager));
                Assert.That(pump.NodeId.NamespaceIndex, Is.EqualTo(fixture.Manager.MachineryInstanceNamespaceIndex));
                Assert.That(pump.Pump.Identification, Is.Not.Null);
                Assert.That(pump.Pump.Operational, Is.Null);
                Assert.That(pump.Pump.Configuration, Is.Null);
                Assert.That(pump.Pump.Documentation, Is.Null);
                Assert.That(fixture.Manager.FindPredefinedNode(pump.NodeId), Is.SameAs(pump.Pump));
                Assert.That(fixture.Manager.Server.ServerObject.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.HasNotifier, false, pump.NodeId), Is.True);
                Assert.That(fixture.Manager.Server.TypeTree.IsTypeOf(
                    pump.Pump.TypeDefinitionId,
                    ExpandedNodeId.ToNodeId(Di.ObjectTypeIds.TopologyElementType, runtime.SystemContext.NamespaceUris)),
                    Is.True);
            });

            pump.Measurements.SetAnalog(BrowseNames.MassFlow, 3.0);
            runtime.Pump(pump.NodeId)!.Measurements.SetAnalog(BrowseNames.MassFlow, 4.5);
            NodeState measurements = pump.Measurements.Node;
            var massFlow = (BaseVariableState)measurements.FindChild(
                runtime.SystemContext, new QualifiedName(BrowseNames.MassFlow, runtime.NamespaceIndices.Pumps))!;
            Assert.That(massFlow.WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(4.5));
            Assert.That(fixture.Manager.FindPredefinedNode(measurements.NodeId), Is.SameAs(measurements));
            AssertOwnedSubtree(fixture.Manager, pump.Pump);

            IMachineryBuildContext machinery = fixture.Manager.CreateMachineryBuildContext();
            Assert.Multiple(() =>
            {
                Assert.That(machinery.DeviceSet.FindChild(runtime.SystemContext, pump.Pump.BrowseName),
                    Is.SameAs(pump.Pump));
                Assert.That(machinery.MachinesFolder.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.Organizes, false, pump.NodeId), Is.True);
            });
        }

        [Test]
        public async Task CreationHonorsCancellationAndDuplicateNamesOnTheSharedOwnerAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            PumpsRuntime runtime = fixture.Manager.CreatePumpsRuntime();
            var name = new QualifiedName("Feed", runtime.InstanceNamespaceIndex);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(async () => await runtime.CreatePumpAsync(name, cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
            await runtime.CreatePumpAsync(name);
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await runtime.CreatePumpAsync(name)))!;
            Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(runtime.Pumps, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task OwnerDisposalStopsItsSharedRuntimeAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            PumpsRuntime runtime = fixture.Manager.CreatePumpsRuntime();
            await runtime.CreatePumpAsync(new QualifiedName("Feed", runtime.InstanceNamespaceIndex));

            await fixture.Manager.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await runtime.CreatePumpAsync(new QualifiedName("AfterShutdown", runtime.InstanceNamespaceIndex)));
            await runtime.DisposeAsync();
        }

        [Test]
        public async Task DirectCompositionRequiresThePumpsModelAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync(loadPumps: false);
            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => fixture.Manager.CreatePumpsRuntime())!;
            Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Checks that lazily created children belong to the same application namespace.
        /// </summary>
        private static void AssertOwnedSubtree(MachineryNodeManager manager, NodeState node)
        {
            Assert.That(node.NodeId.NamespaceIndex, Is.EqualTo(manager.MachineryInstanceNamespaceIndex));
            Assert.That(manager.FindPredefinedNode(node.NodeId), Is.SameAs(node));
            var children = new List<BaseInstanceState>();
            node.GetChildren(manager.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                AssertOwnedSubtree(manager, child);
            }
        }

        /// <summary>
        /// Hosts the stock owner directly, with providers deliberately supplied out of order.
        /// </summary>
        private sealed class ComposedOwner : IAsyncDisposable
        {
            /// <summary>
            /// Gets the stock Machinery owner.
            /// </summary>
            public MachineryNodeManager Manager { get; private set; } = null!;

            /// <summary>
            /// Loads the real models without DI hosting or a second DI manager.
            /// </summary>
            public async Task StartAsync(bool loadPumps = true)
            {
                m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                StandardServer server = await m_fixture.StartAsync();
                ArrayOf<IMachineryModelProvider> providers = loadPumps
                    ? [new PumpsMachineryModelProvider(),
                        new MachineryModelProvider(MachineryParts.BuildingBlocks),
                        new PumpsMachineryModelProvider()]
                    : [new MachineryModelProvider(MachineryParts.BuildingBlocks)];
                Manager = new MachineryNodeManager(server.CurrentInstance, m_fixture.Config, providers,
                    new MachineryServerOptions
                    {
                        Parts = MachineryParts.BuildingBlocks,
                        InstanceNamespaceUri = "urn:tests:pumps:composed:instances"
                    });
                await Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>());
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (Manager != null)
                {
                    await Manager.DisposeAsync();
                }
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync();
                }
            }

            /// <summary>The hosting server fixture.</summary>
            private ServerFixture<StandardServer>? m_fixture;
        }
    }
}
