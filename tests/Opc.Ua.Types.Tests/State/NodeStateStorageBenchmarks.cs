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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Measures construction with optional storage, callbacks, and references.
    /// </summary>
    /// <remarks>
    /// Each benchmark is also an NUnit smoke test. Cached payloads and delegates isolate
    /// node storage costs. Description remains inline; the three security properties
    /// (RolePermissions, UserRolePermissions, AccessRestrictions) share a bag allocated
    /// on the first non-default assignment, as do the six design-metadata properties.
    /// </remarks>
    [TestFixture]
    [Category("NodeStateStorage")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [Config(typeof(StorageBenchmarkConfig))]
    public class NodeStateStorageBenchmarks
    {
        /// <summary>
        /// Gets stable case names shared with the rooted-memory fixture.
        /// </summary>
        public static IEnumerable<string> MemoryCases => NodeStateMemoryScenarios.All.Select(c => c.Name);

        /// <summary>
        /// Gets the explicit-reference degrees used by lookup and mutation baselines.
        /// </summary>
        public static IEnumerable<int> ReferenceDegrees => s_referenceDegrees;

        /// <summary>
        /// Measures matched cached/unique construction and explicit-reference degrees.
        /// </summary>
        [Benchmark]
        [ArgumentsSource(nameof(MemoryCases))]
        public NodeState ConstructPopulation(string scenario)
        {
            return NodeStateMemoryScenarios.Construct(NodeStateMemoryScenarios.Select(scenario), 17);
        }

        /// <summary>
        /// Measures a last-inserted target lookup (an intentional miss at degree zero).
        /// </summary>
        [Benchmark]
        [ArgumentsSource(nameof(ReferenceDegrees))]
        public bool ReferenceHit(int degree)
        {
            return m_referenceNodes[degree].ReferenceExists(
                ReferenceTypeIds.HasComponent, false, new NodeId((uint)(degree + 49999), 2));
        }

        /// <summary>
        /// Measures a missing-target lookup against a prebuilt node.
        /// </summary>
        [Benchmark]
        [ArgumentsSource(nameof(ReferenceDegrees))]
        public bool ReferenceMiss(int degree)
        {
            return m_referenceNodes[degree].ReferenceExists(ReferenceTypeIds.HasComponent, false, s_missingTarget);
        }

        /// <summary>
        /// Measures enumeration with caller-owned destination storage.
        /// </summary>
        [Benchmark]
        [ArgumentsSource(nameof(ReferenceDegrees))]
        public int EnumerateReferences(int degree)
        {
            m_referenceDestination.Clear();
            m_referenceNodes[degree].GetReferences(null!, m_referenceDestination);
            return m_referenceDestination.Count;
        }

        /// <summary>
        /// Measures steady-state insertion/removal without accumulating references between invocations.
        /// </summary>
        [Benchmark]
        [ArgumentsSource(nameof(ReferenceDegrees))]
        public bool AddRemoveReference(int degree)
        {
            BaseObjectState node = m_referenceNodes[degree];
            node.AddReference(ReferenceTypeIds.HasComponent, false, s_missingTarget);
            return node.RemoveReference(ReferenceTypeIds.HasComponent, false, s_missingTarget);
        }

        /// <summary>
        /// Proves the lookup/mutation benchmarks select prebuilt degrees and leave the graph unchanged.
        /// </summary>
        [Test]
        public void ReferenceBenchmarksSelectDegreesAndRestoreState()
        {
            foreach (int degree in s_referenceDegrees)
            {
                Assert.That(ReferenceHit(degree), Is.EqualTo(degree > 0));
                Assert.That(ReferenceMiss(degree), Is.False);
                Assert.That(EnumerateReferences(degree), Is.EqualTo(degree));
                Assert.That(AddRemoveReference(degree), Is.True);
                Assert.That(ReferenceMiss(degree), Is.False);
                Assert.That(EnumerateReferences(degree), Is.EqualTo(degree));
                if (degree > 0)
                {
                    BaseObjectState node = m_referenceNodes[degree];
                    var last = new NodeId((uint)(degree + 49999), 2);
                    Assert.That(node.RemoveReference(ReferenceTypeIds.HasComponent, false, last), Is.True);
                    Assert.That(ReferenceHit(degree), Is.False);
                    node.AddReference(ReferenceTypeIds.HasComponent, false, last);
                    Assert.That(ReferenceHit(degree), Is.True);
                }
            }
        }

        /// <summary>
        /// Warms the cached payloads and construction paths.
        /// </summary>
        [GlobalSetup]
        [OneTimeSetUp]
        public void Setup()
        {
            foreach (int degree in s_referenceDegrees)
            {
                m_referenceNodes[degree] = (BaseObjectState)NodeStateMemoryScenarios.Construct(
                    NodeStateMemoryScenarios.Select($"Object.References.0.{degree}"), 17);
            }
            m_referenceDestination.Capacity = 1024;
            const int k_warmupRounds = 3;
            for (int i = 0; i < k_warmupRounds; i++)
            {
                ConstructWithDescription();
                ConstructWithRolePermissions();
                ConstructWithDesignMetadata();
                ConstructWithBehaviorCallbacks();
                ConstructWithValueCallbacks();
                ConstructWithStateAndValueCallbacks();
                ConstructWithZeroReferences();
                ConstructWithOneReference();
                ConstructWithFourReferences();
                ConstructWithSixteenReferences();
            }
        }

        /// <summary>
        /// Measures construction with an inline description.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithDescription()
        {
            GC.KeepAlive(ConstructWithDescriptionBody());
        }

        /// <summary>
        /// Measures construction and security-bag allocation using a cached permission array.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithRolePermissions()
        {
            GC.KeepAlive(ConstructWithRolePermissionsBody());
        }

        /// <summary>
        /// Measures construction and design-metadata-bag allocation using cached payloads.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithDesignMetadata()
        {
            GC.KeepAlive(ConstructWithDesignMetadataBody());
        }

        /// <summary>
        /// Measures construction with a cached <see cref="NodeState.OnStateChangedAsync"/> callback.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithBehaviorCallbacks()
        {
            GC.KeepAlive(ConstructWithBehaviorCallbacksBody());
        }

        /// <summary>
        /// Measures variable construction with cached read/write value callbacks.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithValueCallbacks()
        {
            GC.KeepAlive(ConstructWithValueCallbacksBody());
        }

        /// <summary>
        /// Measures variable construction with state-change and read/write value callbacks.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithStateAndValueCallbacks()
        {
            GC.KeepAlive(ConstructWithStateAndValueCallbacksBody());
        }

        /// <summary>
        /// Measures construction without explicit references.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithZeroReferences()
        {
            GC.KeepAlive(ConstructWithReferencesBody(0));
        }

        /// <summary>
        /// Measures construction with one explicit reference.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithOneReference()
        {
            GC.KeepAlive(ConstructWithReferencesBody(1));
        }

        /// <summary>
        /// Measures construction with four explicit references.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithFourReferences()
        {
            GC.KeepAlive(ConstructWithReferencesBody(4));
        }

        /// <summary>
        /// Measures construction with sixteen explicit references.
        /// </summary>
        [Test]
        [Benchmark]
        public void ConstructWithSixteenReferences()
        {
            GC.KeepAlive(ConstructWithReferencesBody(16));
        }

        private static BaseObjectState ConstructWithDescriptionBody()
        {
            return new BaseObjectState(null)
            {
                Description = s_description
            };
        }

        private static BaseObjectState ConstructWithRolePermissionsBody()
        {
            return new BaseObjectState(null)
            {
                RolePermissions = s_rolePermissions
            };
        }

        private static BaseObjectState ConstructWithDesignMetadataBody()
        {
            return new BaseObjectState(null)
            {
                Extensions = s_extensions,
                Categories = s_categories,
                Specification = k_specification,
                NodeSetDocumentation = k_nodeSetDocumentation,
                ReleaseStatus = Export.ReleaseStatus.Draft,
                DesignToolOnly = true
            };
        }

        private static BaseObjectState ConstructWithBehaviorCallbacksBody()
        {
            return new BaseObjectState(null)
            {
                OnStateChangedAsync = s_noopChangedAsync
            };
        }

        private static BaseDataVariableState ConstructWithValueCallbacksBody()
        {
            return new BaseDataVariableState(null)
            {
                OnReadValue = s_noopReadValue,
                OnWriteValue = s_noopWriteValue
            };
        }

        private static BaseDataVariableState ConstructWithStateAndValueCallbacksBody()
        {
            return new BaseDataVariableState(null)
            {
                OnStateChangedAsync = s_noopChangedAsync,
                OnReadValue = s_noopReadValue,
                OnWriteValue = s_noopWriteValue
            };
        }

        private static BaseObjectState ConstructWithReferencesBody(int count)
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(1u, 1)
            };
            for (int i = 0; i < count; i++)
            {
                node.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId((uint)(i + 100), 1));
            }

            return node;
        }

        private static ValueTask NoopChangedAsync(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks masks,
            CancellationToken cancellationToken)
        {
            return default;
        }

        private static ServiceResult NoopFullValue(
            ISystemContext context,
            NodeState node,
            NumericRange range,
            QualifiedName encoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            return ServiceResult.Good;
        }

        private const string k_specification = "http://example.org/spec";
        private const string k_nodeSetDocumentation = "http://example.org/docs";

        private static readonly LocalizedText s_description = new("A representative node description.");

        private static readonly ArrayOf<RolePermissionType> s_rolePermissions =
            ArrayOf.Wrapped(
                new RolePermissionType { RoleId = new NodeId(1u, 0), Permissions = 0xFF });

        private static readonly XmlElement[] s_extensions =
            [XmlElement.From(new System.Xml.XmlDocument().CreateElement("ext"))];

        private static readonly IList<string> s_categories = new List<string> { "Category" }.AsReadOnly();
        private static readonly NodeStateChangedAsyncHandler s_noopChangedAsync = NoopChangedAsync;
        private static readonly NodeValueEventHandler s_noopReadValue = NoopFullValue;
        private static readonly NodeValueEventHandler s_noopWriteValue = NoopFullValue;
        private static readonly int[] s_referenceDegrees = [0, 1, 2, 4, 8, 16, 128, 1024];
        private static readonly ExpandedNodeId s_missingTarget = new NodeId(99999u, 2);
        private readonly Dictionary<int, BaseObjectState> m_referenceNodes = [];
        private readonly List<IReference> m_referenceDestination = [];

        /// <summary>
        /// Configures short in-process benchmark runs.
        /// </summary>
        public sealed class StorageBenchmarkConfig : ManualConfig
        {
            /// <summary>
            /// Initializes the in-process job.
            /// </summary>
            public StorageBenchmarkConfig()
            {
                AddJob(Job.ShortRun
                    .WithToolchain(InProcessEmitToolchain.Instance)
                    .WithStrategy(RunStrategy.Throughput));
            }
        }
    }
}
