/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Actual fluent registration, generated structured binding and MonitoredNode2 lifecycle populations.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("NodeStateOccupancy")]
    public sealed class NodeStateRegisteredPopulationTests
    {
        /// <summary>
        /// Verifies registered membership and callback co-occurrence, including removal with a live companion.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegisteredCompanionMonitoringLifecycleAsync(bool companion)
        {
            await ExerciseAsync(companion, null).ConfigureAwait(false);
        }

#if NET10_0_OR_GREATER
        /// <summary>
        /// Records node occupancy and a separately labelled whole test-manager rooted-heap estimate.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        [Explicit("Run alone with NODESTATE_MEMORY_* provenance variables; see State readme.")]
        public async Task ExportRegisteredBaselineAsync()
        {
            string output = Environment.GetEnvironmentVariable("NODESTATE_MEMORY_OUTPUT")
                ?? throw new InvalidOperationException("Set NODESTATE_MEMORY_OUTPUT.");
            string root = Environment.GetEnvironmentVariable("NODESTATE_SOURCE_ROOT")
                ?? throw new InvalidOperationException("Set NODESTATE_SOURCE_ROOT.");
            // Warm generation, registration, Moq proxies and removal outside both memory and occupancy output.
            await ExerciseAsync(false, null).ConfigureAwait(false);
            await ExerciseAsync(true, null).ConfigureAwait(false);
            using var evidence = new NodeStateMemoryEvidence(output, root);
            await ExerciseAsync(false, evidence).ConfigureAwait(false);
            await ExerciseAsync(true, evidence).ConfigureAwait(false);
            for (int sample = 1; sample <= 3; sample++)
            {
                // Root slots exist at the baseline. Includes manager, mocks, context, indexes and authored nodes.
                var managers = new PopulationManager[16];
                long before = NodeStateMemoryEvidence.FullHeap();
                for (int i = 0; i < managers.Length; i++)
                {
                    var manager = new PopulationManager();
                    managers[i] = manager;
                    manager.Author();
                    await manager.RegisterAsync().ConfigureAwait(false);
                }
                long after = NodeStateMemoryEvidence.FullHeap();
                evidence.Sample("WholeTestManagerRootedEstimate", "Registered.Fluent.Generated", sample,
                    managers.Length, after - before);
                GC.KeepAlive(managers);
                foreach (PopulationManager manager in managers)
                {
                    await manager.RemoveAsync().ConfigureAwait(false);
                    manager.ReleaseObservationRoots();
                }
                long removed = NodeStateMemoryEvidence.FullHeap();
                evidence.Sample("WholeTestManagerRootedEstimate", "Removed.Fluent.Generated", sample,
                    managers.Length, removed - before);
                GC.KeepAlive(managers);
                foreach (PopulationManager manager in managers)
                {
                    manager.Dispose();
                }
                Array.Clear(managers);
            }
            TestContext.Out.WriteLine($"Registered node baseline: {output}");
        }
#endif

        private static async Task ExerciseAsync(bool companion, NodeStateMemoryEvidence? evidence)
        {
            using var manager = new PopulationManager();
            manager.Author();
            string label = companion ? "ActualCompanion" : "NoCompanion";
            NodeState[] nodes = manager.Graph();
            Record("Authored");
            Assert.That(manager.RegisteredCount, Is.Zero);
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(manager.Vector).Slots, Is.Zero);

            await manager.RegisterAsync().ConfigureAwait(false);
            Assert.That(manager.RegisteredCount, Is.EqualTo(nodes.Length));
            Assert.That(manager.Contains(manager.Vector.NodeId), Is.True);
            Assert.That(manager.Contains(manager.Method.NodeId), Is.True);
            Assert.That(manager.Plain.DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(nodes, Has.Length.EqualTo(7), "Object, scalar, vector with X/Y/Z, method.");
            var outputs = new List<Variant>();
            ServiceResult callResult = await manager.Method.OnCallMethod2Async!(
                manager.SystemContext, manager.Method, manager.Root.NodeId, default, outputs).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(callResult), Is.True);
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out int methodValue), Is.True);
            Assert.That(methodValue, Is.EqualTo(7));
            Record("Registered");

            TestData.VectorVariableValue? binding = null;
            if (companion)
            {
                binding = new TestData.VectorVariableValue(
                    manager.Vector, new TestData.Vector { X = 1, Y = 2, Z = 3 }, null!);
                Assert.That(NodeStateMemoryEvidence.CallbackGroups(manager.Vector), Is.EqualTo(("Value", 2)));
                Variant value = default;
                StatusCode status = default;
                DateTimeUtc timestamp = default;
                ServiceResult result = manager.Vector.X!.OnReadValue!(
                    manager.SystemContext, manager.Vector.X, default, default, ref value, ref status, ref timestamp);
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(value.TryGetValue(out double x), Is.True);
                Assert.That(x, Is.EqualTo(1.0));
            }
            Record("Bound");
            var data = new Mock<IDataChangeMonitoredItem2>();
            data.SetupGet(i => i.Id).Returns(10u);
            var events = new Mock<IEventMonitoredItem>();
            events.SetupGet(i => i.Id).Returns(20u);
            using var monitored = new MonitoredNode2(manager, manager.TestServer, manager.Vector);
            using var eventSource = new MonitoredNode2(manager, manager.TestServer, manager.Root);
            monitored.Add(data.Object);
            eventSource.Add(events.Object);
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(manager.Vector),
                Is.EqualTo(companion ? ("Behavior+Value", 3) : ("Behavior", 1)));
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(manager.Root), Is.EqualTo(("Behavior", 1)));
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(manager.Method), Is.EqualTo(("Method", 1)));
            Record("Monitored");

            monitored.Remove(data.Object);
            eventSource.Remove(events.Object);
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(manager.Vector),
                Is.EqualTo(companion ? ("Value", 2) : (string.Empty, 0)));
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(manager.Root).Slots, Is.Zero);
            Record("Unsubscribed");
            await manager.RemoveAsync().ConfigureAwait(false);
            Assert.That(manager.RegisteredCount, Is.Zero);
            Assert.That(manager.Contains(manager.Vector.NodeId), Is.False);
            Record("Removed");
            GC.KeepAlive(binding);

            void Record(string phase)
            {
                if (evidence is not null)
                {
                    foreach (NodeState node in nodes)
                    {
                        evidence.Occupancy($"{label}.{phase}", node, manager.SystemContext);
                    }
                }
            }
        }

        private sealed class PopulationManager : FluentNodeManagerBase
        {
            public PopulationManager()
                : this(CreateServer())
            {
            }

            private PopulationManager(IServerInternal server)
                : base(server, "urn:memory:registered")
            {
                TestServer = server;
            }

            public IServerInternal TestServer { get; }
            public BaseObjectState Root { get; private set; } = null!;
            public BaseVariableState Plain { get; private set; } = null!;
            public TestData.VectorVariableState Vector { get; private set; } = null!;
            public MethodState Method { get; private set; } = null!;
            public int RegisteredCount => PredefinedNodes.Count;

            public void Author()
            {
                m_builder = CreateFluentBuilder(NamespaceIndexes[0]);
                Root = m_builder.AddObject("Population").Node;
                Root.EventNotifier = EventNotifiers.SubscribeToEvents;
                Plain = m_builder.AddVariable<double>("Plain", Root.NodeId).Node;
                Vector = TestData.TestDataExtensions.CreateInstanceOfVectorVariableType(
                    SystemContext, Root, new QualifiedName("Vector", NamespaceIndexes[0]));
                m_builder.Add(Vector, Root.NodeId);
                INodeBuilder<MethodState> method = m_builder.AddMethod("Method", Root.NodeId);
                method.OnCall(static (_, _, _, _, outputs, _) =>
                {
                    outputs.Add(Variant.From(7));
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
                Method = method.Node;
            }

            public async ValueTask RegisterAsync()
            {
                await RegisterAuthoredNodesAsync(m_builder!).ConfigureAwait(false);
                await CompleteConfigureAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
                m_builder!.Seal();
                m_builder = null;
            }

            public ValueTask RemoveAsync()
            {
                return RemovePredefinedNodeAsync(SystemContext, Root, []);
            }

            public bool Contains(NodeId id)
            {
                return PredefinedNodes.ContainsKey(id);
            }

            public void ReleaseObservationRoots()
            {
                Root = null!;
                Plain = null!;
                Vector = null!;
                Method = null!;
            }

            public NodeState[] Graph()
            {
                var nodes = new List<NodeState> { Root };
                for (int i = 0; i < nodes.Count; i++)
                {
                    var children = new List<BaseInstanceState>();
                    nodes[i].GetChildren(SystemContext, children);
                    nodes.AddRange(children);
                }
                return [.. nodes];
            }

            private static IServerInternal CreateServer()
            {
                var telemetry = new Mock<ITelemetryContext>();
                telemetry.SetupGet(t => t.LoggerFactory).Returns(NullLoggerFactory.Instance);
                var namespaceUris = new NamespaceTable();
                namespaceUris.GetIndexOrAppend(TestData.Namespaces.TestData);
                var server = new Mock<IServerInternal>();
                server.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
                server.SetupGet(s => s.ServerUris).Returns(new StringTable());
                server.SetupGet(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
                server.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
                server.SetupGet(s => s.Telemetry).Returns(telemetry.Object);
                server.SetupGet(s => s.MessageContext).Returns(ServiceMessageContext.Create(telemetry.Object));
                server.SetupGet(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
                return server.Object;
            }

            private NodeManagerBuilder? m_builder;
        }
    }
}
