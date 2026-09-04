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

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Allocation and throughput baselines for <see cref="NodeState"/> constructors and the
    /// operations that are affected by later lock/layout changes (Phases 2-4).
    /// </summary>
    /// <remarks>
    /// Each benchmark method is also a NUnit smoke test (dual [Test][Benchmark] pattern).
    /// Run as a BenchmarkDotNet job to capture allocated-bytes-per-operation baselines before
    /// any production edits.  Structural NUnit-only tests (no [Benchmark]) verify stable
    /// semantic facts that are expected to hold across all TFMs.
    /// </remarks>
    [TestFixture]
    [Category("NodeStateAllocation")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [Config(typeof(InProcessBenchmarkConfig))]
    public class NodeStateAllocationBenchmarks
    {
        /// <summary>
        /// In-process toolchain config — skips BDN's auto-generated project rebuild
        /// (which times out for a test assembly that transitively pulls hundreds of
        /// references) and matches the pattern established in
        /// <c>PooledNotificationBenchmarks</c>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by BenchmarkDotNet via reflection")]
        private sealed class InProcessBenchmarkConfig : ManualConfig
        {
            public InProcessBenchmarkConfig()
            {
                AddJob(Job.ShortRun
                    .WithToolchain(InProcessEmitToolchain.Instance)
                    .WithStrategy(RunStrategy.Throughput));
            }
        }

        // Pre-constructed node reused by the operation benchmarks.
        private SystemContext m_context;
        private BaseObjectState m_objectNode;

        /// <summary>
        /// Creates the shared context and the pre-constructed benchmark nodes, then
        /// pre-warms every benchmark path.
        /// </summary>
        /// <remarks>
        /// Called by both BenchmarkDotNet (<c>[GlobalSetup]</c>) and NUnit
        /// (<c>[OneTimeSetUp]</c>).  The pre-warm loop ensures that JIT compilation and
        /// any one-time static initialisation costs do not contaminate the first measured
        /// BDN iteration — a gap that would otherwise make
        /// <see cref="ConstructBaseDataVariableState"/> appear to allocate more than
        /// <see cref="InitializeMinimalVariable"/> in a dry run.
        /// </remarks>
        [GlobalSetup]
        [OneTimeSetUp]
        public void Setup()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.CreateForBenchmarks();
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = new NamespaceTable()
            };
            m_context.NamespaceUris.GetIndexOrAppend("http://test.org/UA/");

            m_objectNode = new BaseObjectState(null)
            {
                NodeId = new NodeId(1, 1),
                BrowseName = new QualifiedName("BenchObject", 1),
                DisplayName = new LocalizedText("BenchObject")
            };

            // Pre-warm all benchmark paths so that JIT compilation and one-time static
            // initialisation of BaseVariableState, BaseDataVariableState and their type
            // dependencies do not contaminate the first measured BDN iteration.
            const int k_warmupRounds = 5;
            for (int i = 0; i < k_warmupRounds; i++)
            {
                _ = new BaseObjectState(null);
                _ = new BaseDataVariableState(null);
                _ = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(1, 1),
                    BrowseName = new QualifiedName("Value", 1),
                    DisplayName = new LocalizedText("Value"),
                    DataType = DataTypeIds.Double,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    Value = 42.0
                };
                m_objectNode.SetAreEventsMonitored(m_context, true, false);
                m_objectNode.SetAreEventsMonitored(m_context, false, false);
                m_objectNode.ReportEvent(m_context, null!);
            }
        }

        /// <summary>Releases the pre-constructed nodes.</summary>
        [GlobalCleanup]
        [OneTimeTearDown]
        public void TearDown()
        {
            m_objectNode = null;
        }

        // ----------------------------------------------------------------
        // Construction benchmarks
        // ----------------------------------------------------------------

        /// <summary>
        /// Measures the per-operation allocation cost of constructing a
        /// <see cref="BaseObjectState"/> with no parent and no further initialization.
        /// After Phase 3, exercises only the two eagerly-allocated
        /// <see cref="System.Threading.Lock"/> instances remaining in
        /// <see cref="NodeState"/> (m_referencesLock and m_childrenLock).
        /// m_notifiersLock and m_browseLock are now lazily published via
        /// Volatile.Read / Interlocked.CompareExchange and are NOT allocated at
        /// construction time.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructBaseObjectState()
        {
            _ = new BaseObjectState(null);
        }

        /// <summary>
        /// Measures the per-operation allocation cost of constructing a
        /// <see cref="BaseDataVariableState"/> with no parent and no further initialization.
        /// After Phase 3, exercises three <see cref="System.Threading.Lock"/> allocations:
        /// two in <see cref="NodeState"/> (m_referencesLock, m_childrenLock) and one
        /// (<c>m_attributeLock</c>) in <see cref="BaseVariableState"/>.
        /// m_notifiersLock and m_browseLock are lazily published and NOT allocated here.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructBaseDataVariableState()
        {
            _ = new BaseDataVariableState(null);
        }

        /// <summary>
        /// Measures the allocation cost of constructing and minimally initializing a
        /// <see cref="BaseDataVariableState"/> with a controlled numeric <see cref="NodeId"/>,
        /// <see cref="QualifiedName"/>, <see cref="LocalizedText"/>, DataType set to
        /// <see cref="DataTypeIds.Double"/>, <see cref="ValueRanks.Scalar"/>,
        /// <see cref="AccessLevels.CurrentRead"/>, and a scalar <see cref="Variant"/> value.
        /// </summary>
        /// <remarks>
        /// Because <see cref="NodeId"/>, <see cref="QualifiedName"/>,
        /// <see cref="LocalizedText"/> and <see cref="Variant"/> are all
        /// <see langword="readonly struct"/> value types in this stack, the property
        /// setters do not perform additional heap allocations beyond the node itself and
        /// its three lock objects.  The steady-state allocated bytes therefore equal those
        /// of <see cref="ConstructBaseDataVariableState"/>.
        /// </remarks>
        [Test]
        [Benchmark]
        public void InitializeMinimalVariable()
        {
            _ = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1, 1),
                BrowseName = new QualifiedName("Value", 1),
                DisplayName = new LocalizedText("Value"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                Value = 42.0
            };
        }

        // ----------------------------------------------------------------
        // Operation benchmarks (use pre-constructed nodes from Setup)
        // ----------------------------------------------------------------

        /// <summary>
        /// Measures the cost of a paired <c>SetAreEventsMonitored(true)</c> /
        /// <c>SetAreEventsMonitored(false)</c> cycle on a node that has no children and no
        /// notifiers.  After Phase 2 each call executes an <see cref="System.Threading.Interlocked"/>
        /// operation without acquiring any lock; the child-list and notifier-list paths are not
        /// taken (<c>includeChildren = false</c>).
        /// The net counter effect per iteration is zero, keeping invocations idempotent.
        /// Expected steady-state allocation: 0 B.
        /// </summary>
        [Test]
        [Benchmark]
        public void SetAreEventsMonitoredToggle()
        {
            m_objectNode.SetAreEventsMonitored(m_context, true, false);
            m_objectNode.SetAreEventsMonitored(m_context, false, false);
        }

        /// <summary>
        /// Measures the cost of <see cref="NodeState.ReportEvent"/> on a node that has no
        /// <see cref="NodeState.OnReportEvent"/> handler, no
        /// <see cref="NodeState.OnReportEventAsync"/> handler, and no notifiers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Steady-state allocation: <b>48 B per call</b>.  Although the
        /// synchronous-handler path (<see cref="NodeState.OnReportEvent"/>) and the
        /// notifier-propagation path are both skipped (null fast-exits), the C# compiler
        /// promotes the <c>context</c>, <c>e</c>, <c>onReportEventAsync</c> and
        /// <c>this</c> locals into a display-class (closure) object for the
        /// <c>Task.Run(() =&gt; …)</c> lambda that lives in the async-sink else-branch.
        /// That display class is allocated on every entry to <see cref="NodeState.ReportEvent"/>
        /// even when <c>OnReportEventAsync</c> is null and the lambda is never actually
        /// invoked.  Display-class layout: 4 × 8-byte captured fields + 16-byte object
        /// header = 48 B.
        /// </para>
        /// <para>
        /// This is a genuine production allocation, not benchmark noise.  Eliminating it
        /// requires restructuring the async-sink path to use a static lambda or a local
        /// function that does not capture outer locals — a candidate optimization tracked
        /// for Phase 2.
        /// </para>
        /// </remarks>
        [Test]
        [Benchmark]
        public void ReportEventNoNotifier()
        {
            // null! — the event argument is only forwarded to callbacks and notifiers,
            // both of which are null here, so no null-dereference occurs.
            m_objectNode.ReportEvent(m_context, null!);
        }

        // ----------------------------------------------------------------
        // NUnit-only structural tests (no [Benchmark] attribute)
        // ----------------------------------------------------------------

        /// <summary>
        /// A freshly constructed <see cref="BaseObjectState"/> reports
        /// <see cref="NodeClass.Object"/> and <see cref="NodeState.AreEventsMonitored"/> is
        /// <see langword="false"/>.
        /// </summary>
        [Test]
        public void ConstructedObjectNodeHasExpectedDefaults()
        {
            var node = new BaseObjectState(null);

            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(node.AreEventsMonitored, Is.False);
        }

        /// <summary>
        /// A freshly constructed <see cref="BaseDataVariableState"/> reports
        /// <see cref="NodeClass.Variable"/> and <see cref="NodeState.AreEventsMonitored"/> is
        /// <see langword="false"/>.
        /// </summary>
        [Test]
        public void ConstructedVariableNodeHasExpectedDefaults()
        {
            var node = new BaseDataVariableState(null);

            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(node.AreEventsMonitored, Is.False);
        }

        /// <summary>
        /// A minimally initialized <see cref="BaseDataVariableState"/> reflects the values
        /// assigned during object-initializer construction.
        /// </summary>
        [Test]
        public void InitializedVariableHasExpectedProperties()
        {
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1, 1),
                BrowseName = new QualifiedName("Value", 1),
                DisplayName = new LocalizedText("Value"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                Value = 42.0
            };

            Assert.That(node.NodeId, Is.EqualTo(new NodeId(1, 1)));
            Assert.That(node.BrowseName, Is.EqualTo(new QualifiedName("Value", 1)));
            Assert.That(node.DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(node.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(node.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(node.Value.GetDouble(), Is.EqualTo(42.0));
        }

        /// <summary>
        /// <see cref="NodeState.SetAreEventsMonitored"/> correctly tracks a paired
        /// increment / decrement: after a single enable call the counter is positive and
        /// <see cref="NodeState.AreEventsMonitored"/> is <see langword="true"/>; after the
        /// matching disable call it returns to zero and the property is
        /// <see langword="false"/>.
        /// </summary>
        [Test]
        public void SetAreEventsMonitoredTransitionsCorrectly()
        {
            var node = new BaseObjectState(null);

            Assert.That(node.AreEventsMonitored, Is.False);

            node.SetAreEventsMonitored(m_context, true, false);
            Assert.That(node.AreEventsMonitored, Is.True);

            node.SetAreEventsMonitored(m_context, false, false);
            Assert.That(node.AreEventsMonitored, Is.False);
        }
    }
}
