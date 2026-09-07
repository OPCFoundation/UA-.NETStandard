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
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

#if NET10_0_OR_GREATER
using System.Runtime.CompilerServices;
using ObjectLayoutInspector;
#endif

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Validates the measurement inputs independently and exports an opt-in cumulative baseline.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("NodeStateMemory")]
    public sealed class NodeStateMemoryBaselineTests
    {
        /// <summary>
        /// Bare and minimally initialized cases have distinct identities but the requested concrete node kind.
        /// </summary>
        [TestCase("Object", NodeClass.Object)]
        [TestCase("Variable", NodeClass.Variable)]
        public void BareAndMinimalCasesHaveDistinctInitialization(string kind, NodeClass nodeClass)
        {
            NodeState bare = NodeStateMemoryScenarios.Construct(
                NodeStateMemoryScenarios.Select($"{kind}.Bare"), 17);
            NodeState minimal = NodeStateMemoryScenarios.Construct(
                NodeStateMemoryScenarios.Select($"{kind}.Minimal"), 17);
            Assert.That(bare.NodeClass, Is.EqualTo(nodeClass));
            Assert.That(minimal.NodeClass, Is.EqualTo(nodeClass));
            Assert.That(bare.NodeId.IsNull, Is.True);
            Assert.That(bare.BrowseName.IsNull, Is.True);
            Assert.That(minimal.NodeId, Is.EqualTo(new NodeId(1017u, 2)));
            Assert.That(minimal.BrowseName, Is.EqualTo(new QualifiedName("0123456789ABCDEF", 2)));
            Assert.That(minimal.DisplayName.Text, Is.EqualTo("0123456789ABCDEF"));
        }

        /// <summary>
        /// Pins case selection, including all reference degrees and both wrapper construction controls.
        /// </summary>
        [Test]
        public void CatalogSelectsExactCasesAndRejectsUnknownNames()
        {
            Assert.That(NodeStateMemoryScenarios.All.Select(c => c.Name), Is.Unique);
            foreach (int count in new[] { 0, 1, 2, 4, 8, 16, 128, 1024 })
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    NodeStateMemoryScenario scenario =
                        NodeStateMemoryScenarios.Select($"Object.References.{mode}.{count}");
                    Assert.That(scenario.References, Is.EqualTo(count));
                    Assert.That(scenario.ReferenceMode, Is.EqualTo(mode));
                    NodeState node = NodeStateMemoryScenarios.Construct(scenario, 17);
                    var references = new List<IReference>();
                    node.GetReferences(null!, references);
                    Assert.That(references, Has.Count.EqualTo(count));
                    for (int i = 0; i < count; i++)
                    {
                        ExpandedNodeId expected = mode == 1
                            ? new ExpandedNodeId(new NodeId((uint)(50000 + i), 2), "urn:memory:targets", 0)
                            : new NodeId((uint)(50000 + i), 2);
                        Assert.That(references[i].TargetId, Is.EqualTo(expected));
                        Assert.That(references[i].IsInverse, Is.False);
                        Assert.That(references[i].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                    }
                }
            }
            Assert.Throws<InvalidOperationException>(() => NodeStateMemoryScenarios.Select("not-a-case"));
        }

        /// <summary>
        /// Proves identifiers/text share one string per node, but unique nodes do not share that string.
        /// </summary>
        [TestCase("Cached", "0123456789ABCDEF")]
        [TestCase("Unique", "0000000000000011")]
        public void TextAndValueInputsMatchTheDeclaredSharing(string sharing, string expected)
        {
            NodeStateMemoryScenario scenario = NodeStateMemoryScenarios.Select($"Variable.String.{sharing}");
            NodeState first = NodeStateMemoryScenarios.Construct(scenario, 17);
            NodeState second = NodeStateMemoryScenarios.Construct(scenario, 17);
            Assert.That(first.NodeId.TryGetValue(out string? identifier), Is.True);
            Assert.That(identifier, Is.EqualTo(expected));
            Assert.That(first.DisplayName.Text, Is.SameAs(identifier));
            Assert.That(first.BrowseName.Name, Is.SameAs(identifier));
            Assert.That(first.Description.Text, Is.SameAs(identifier));
            Assert.That(ReferenceEquals(first.DisplayName.Text, second.DisplayName.Text),
                Is.EqualTo(sharing == "Cached"));
            Assert.That(first, Is.TypeOf<BaseDataVariableState>());
            var variable = (BaseDataVariableState)first;
            Assert.That(variable.Value.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(42.0));
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
        }

        /// <summary>
        /// Checks optional-group presence, concrete payloads and reset semantics rather than bag byte sizes.
        /// </summary>
        [TestCase("Neither.Cached", false, false)]
        [TestCase("Metadata.Cached", true, false)]
        [TestCase("Security.Cached", false, true)]
        [TestCase("Both.Cached", true, true)]
        [TestCase("Neither.Unique", false, false)]
        [TestCase("Metadata.Unique", true, false)]
        [TestCase("Security.Unique", false, true)]
        [TestCase("Both.Unique", true, true)]
        [TestCase("Metadata.Reset", false, false)]
        [TestCase("Security.Reset", false, false)]
        [TestCase("Both.Reset", false, false)]
        public void OptionalCasesHaveTheSpecifiedEffectiveValues(string suffix, bool metadata, bool security)
        {
            foreach (string kind in new[] { "Object", "Variable" })
            {
                NodeState node = NodeStateMemoryScenarios.Construct(
                    NodeStateMemoryScenarios.Select($"{kind}.{suffix}"), 17);
                Assert.That(node.DesignToolOnly, Is.EqualTo(metadata));
                Assert.That(node.Specification, Is.EqualTo(metadata ? "urn:memory:spec" : null));
                Assert.That(node.NodeSetDocumentation, Is.EqualTo(metadata ? "urn:memory:docs" : null));
                Assert.That(node.ReleaseStatus,
                    Is.EqualTo(metadata ? Export.ReleaseStatus.Draft : Export.ReleaseStatus.Released));
                if (metadata)
                {
                    Assert.That(node.Extensions, Has.Length.EqualTo(1));
                    Assert.That(node.Categories, Is.EqualTo(s_categories));
                }
                else
                {
                    Assert.That(node.Extensions, Is.Null);
                    Assert.That(node.Categories, Is.Null);
                }
                Assert.That(node.RolePermissions.IsNull, Is.EqualTo(!security));
                Assert.That(node.UserRolePermissions.IsNull, Is.EqualTo(!security));
                Assert.That(node.AccessRestrictions,
                    Is.EqualTo(security ? AccessRestrictionType.SigningRequired : (AccessRestrictionType?)null));
                if (security)
                {
                    Assert.That(node.RolePermissions.Count, Is.EqualTo(1));
                    Assert.That(node.UserRolePermissions.Count, Is.EqualTo(1));
                    Assert.That(node.RolePermissions[0].RoleId, Is.EqualTo(new NodeId(1u)));
                    Assert.That(node.RolePermissions[0].Permissions, Is.EqualTo((uint)PermissionType.Read));
                    Assert.That(node.UserRolePermissions[0].RoleId, Is.EqualTo(new NodeId(1u)));
                    Assert.That(node.UserRolePermissions[0].Permissions, Is.EqualTo((uint)PermissionType.Read));
                }
            }
        }

        /// <summary>
        /// Pins group co-occurrence, event subscriptions and callback removal independently.
        /// </summary>
        [Test]
        public void OccupancyCountsSlotsNotInvocationListEntries()
        {
            var node = new BaseDataVariableState(null);
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(node), Is.EqualTo((string.Empty, 0)));
            node.OnStateChangedAsync = static (_, _, _, _) => default;
            node.OnReadValue = ReadValue;
            node.OnWriteValue = ReadValue;
            node.StateChangedAsync += static (_, _, _, _) => default;
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(node),
                Is.EqualTo(("Behavior+EventSubscription+Value", 4)));
            node.OnReadValue += ReadValue;
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(node).Slots, Is.EqualTo(4));
            node.OnStateChangedAsync = null;
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(node), Is.EqualTo(("EventSubscription+Value", 3)));
            NodeState synthetic = NodeStateMemoryScenarios.Construct(
                NodeStateMemoryScenarios.Select("Variable.Synthetic.Callbacks"), 17);
            Assert.That(NodeStateMemoryEvidence.CallbackGroups(synthetic), Is.EqualTo(("Behavior+Value", 5)));
        }

        /// <summary>
        /// Checks hashes against a known SHA-256 vector and pins the actual loaded module, not a version label.
        /// </summary>
        [Test]
        public void ProvenanceHashesBytesAndIdentifiesTheLoadedAssembly()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "abc", new System.Text.UTF8Encoding(false));
                Assert.That(NodeStateMemoryEvidence.HashFile(path),
                    Is.EqualTo("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
                File.WriteAllText(path, string.Empty, new System.Text.UTF8Encoding(false));
                Assert.That(NodeStateMemoryEvidence.HashFile(path),
                    Is.EqualTo("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"));
                string row = NodeStateMemoryEvidence.AssemblyRow(typeof(NodeState).Assembly);
                Assert.That(row, Does.Contain("\"Opc.Ua.Types\""));
                Assert.That(row, Does.Contain(typeof(NodeState).Module.ModuleVersionId.ToString("D")));
                Assert.That(row, Does.Contain(typeof(NodeState).Assembly.Location));
                Assert.That(NodeStateMemoryEvidence.Csv("a,\"b\""), Is.EqualTo("\"a,\"\"b\"\"\""));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Ensures source fingerprints include build inputs, but not old compiled or generated output.
        /// </summary>
        [Test]
        public void SourceManifestExcludesBuildOutputs()
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            string source = Path.Combine(root, "src", "Node.cs");
            string project = Path.Combine(root, "Library.csproj");
            string generated = Path.Combine(root, "obj", "Node.g.cs");
            try
            {
                File.WriteAllText(source, "source");
                File.WriteAllText(project, "project");
                File.WriteAllText(generated, "generated");
                Assert.That(NodeStateMemoryEvidence.SourceFiles(root), Is.EquivalentTo([source, project]));
            }
            finally
            {
                File.Delete(source);
                File.Delete(project);
                File.Delete(generated);
                Directory.Delete(Path.Combine(root, "src"));
                Directory.Delete(Path.Combine(root, "obj"));
                Directory.Delete(root);
            }
        }

        /// <summary>
        /// Explicit edges, synthesized type/child edges and dynamic children are separate observables.
        /// </summary>
        [Test]
        public void OccupancySeparatesExplicitAndSynthesizedReferences()
        {
            var context = new SystemContext(NUnitTelemetryContext.CreateForBenchmarks());
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(1u, 2),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            var child = new BaseObjectState(parent)
            {
                NodeId = new NodeId(2u, 2),
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            parent.AddChild(child);
            parent.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(3u, 2));
            string[] row = NodeStateMemoryEvidence.OccupancyRow("Fixed", parent, context).Split(',');
            Assert.That(row.Skip(10), Is.EqualTo(s_parentOccupancy));
            parent.RemoveChild(child);
            row = NodeStateMemoryEvidence.OccupancyRow("Removed", parent, context).Split(',');
            Assert.That(row.Skip(10), Is.EqualTo(s_removedChildOccupancy));
        }

        /// <summary>
        /// Payload cases distinguish heap-owned arrays without changing the semantic value.
        /// </summary>
        [TestCase("Cached")]
        [TestCase("Unique")]
        public void PayloadCasesUseByteStringsWithTheSpecifiedOwnership(string sharing)
        {
            NodeStateMemoryScenario scenario = NodeStateMemoryScenarios.Select($"Variable.Payload.{sharing}");
            var first = (BaseDataVariableState)NodeStateMemoryScenarios.Construct(scenario, 17);
            var second = (BaseDataVariableState)NodeStateMemoryScenarios.Construct(scenario, 18);
            Assert.That(first.Value.TryGetValue(out ByteString bytes), Is.True);
            Assert.That(second.Value.TryGetValue(out ByteString other), Is.True);
            Assert.That(bytes.Length, Is.EqualTo(64));
            Assert.That(bytes.ToArray(), Is.All.Zero);
            Assert.That(first.DataType, Is.EqualTo(DataTypeIds.ByteString));
            Assert.That(bytes.Span.Overlaps(other.Span), Is.EqualTo(sharing == "Cached"));
        }

        /// <summary>
        /// The logger case must enter the actual telemetry seam; bare Create does not initialize this logger.
        /// </summary>
        [Test]
        public void TelemetryCaseCallsTheFactoryWithTheVariableCategory()
        {
            var factory = new Mock<ILoggerFactory>(MockBehavior.Strict);
            factory.Setup(f => f.CreateLogger("Opc.Ua.BaseVariableState")).Returns(NullLogger.Instance);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(t => t.LoggerFactory).Returns(factory.Object);
            var node = (BaseVariableState)NodeStateMemoryScenarios.Construct(
                NodeStateMemoryScenarios.Select("Variable.Telemetry"), 17, telemetry.Object);
            factory.Verify(f => f.CreateLogger("Opc.Ua.BaseVariableState"), Times.Once);
            Assert.That(node.Value.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(42.0));
            Assert.That(node.NodeId, Is.EqualTo(new NodeId(1017u, 2)));
        }

#if NET10_0_OR_GREATER
        /// <summary>
        /// The counter observes actual per-thread allocation, not the number of assigned roots.
        /// </summary>
        [Test]
        public void AllocationCounterDistinguishesNewAndSharedObjects()
        {
            byte[] shared = new byte[64];
            object Reuse(int _) => shared;
            static object AllocateNew(int _) => new byte[64];
            _ = Allocate(Reuse, 1024);
            _ = Allocate(AllocateNew, 1024);
            Assert.That(Allocate(Reuse, 1024), Is.Zero);
            Assert.That(Allocate(AllocateNew, 1024), Is.GreaterThanOrEqualTo(64 * 1024));
        }

        /// <summary>
        /// Exports warmed synchronous allocation traffic separately from rooted live-heap estimates.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        [Explicit("Run alone on a quiet host with NODESTATE_MEMORY_* provenance variables; see State readme.")]
        public void ExportBaseline()
        {
            string output = Environment.GetEnvironmentVariable("NODESTATE_MEMORY_OUTPUT")
                ?? throw new InvalidOperationException("Set NODESTATE_MEMORY_OUTPUT.");
            string root = Environment.GetEnvironmentVariable("NODESTATE_SOURCE_ROOT")
                ?? throw new InvalidOperationException("Set NODESTATE_SOURCE_ROOT.");
            using var evidence = new NodeStateMemoryEvidence(output, root);
            var context = new SystemContext(NUnitTelemetryContext.CreateForBenchmarks());
            foreach (Type type in new[] { typeof(BaseObjectState), typeof(BaseDataVariableState), typeof(MethodState) })
            {
                evidence.Sample("ShallowLayout", type.Name, 1, 1, TypeLayout.GetLayout(type).FullSize);
                Measure(evidence, "ShallowUninitialized." + type.Name,
                    _ => RuntimeHelpers.GetUninitializedObject(type), 4096);
            }
            evidence.Sample("ArrayElementStride", nameof(NodeId), 1, 1, Stride<NodeId>());
            evidence.Sample("ArrayElementStride", nameof(ExpandedNodeId), 1, 1, Stride<ExpandedNodeId>());
            evidence.Sample("ArrayElementStride", nameof(Variant), 1, 1, Stride<Variant>());
            evidence.Sample("ArrayElementStride", nameof(QualifiedName), 1, 1, Stride<QualifiedName>());
            evidence.Sample("ArrayElementStride", nameof(LocalizedText), 1, 1, Stride<LocalizedText>());
            foreach (NodeStateMemoryScenario scenario in NodeStateMemoryScenarios.All)
            {
                int count = scenario.References >= 128 ? 64 : 2048;
                Measure(evidence, scenario.Name, i => NodeStateMemoryScenarios.Construct(scenario, i), count);
                // Browsing itself touches lazy state; occupancy must not precede memory measurement.
                evidence.Occupancy(scenario.Name, NodeStateMemoryScenarios.Construct(scenario, 17), context);
            }
            var shared = new BaseObjectState(null);
            Measure(evidence, "Control.SharedRoot", _ => shared, 2048);
            Measure(evidence, "Component.String16", NodeStateMemoryScenarios.Text, 2048);
            Measure(evidence, "Component.Payload64", _ => new byte[64], 2048);
            Measure(evidence, "Component.PermissionEntryAndArray", _ =>
                new[] { new RolePermissionType { RoleId = new NodeId(1u), Permissions = (uint)PermissionType.Read } },
                2048);
            Measure(evidence, "Component.DelegateAndClosure", i => new Action(() => GC.KeepAlive(i)), 2048);
            Measure(evidence, "Component.RootArray2048", _ => new NodeState[2048], 64);
            NodeState[] roots = [.. Enumerable.Range(0, 2048).Select(i => NodeStateMemoryScenarios.Construct(
                NodeStateMemoryScenarios.Select("Variable.Minimal"), i))];
            Measure(evidence, "Component.Index2048", _ =>
            {
                var index = new NodeIdDictionary<NodeState>();
                foreach (NodeState node in roots)
                {
                    index.Add(node.NodeId, node);
                }
                return index;
            }, 16);
            GC.KeepAlive(roots);
            TestContext.Out.WriteLine($"NodeState baseline: {output}");
        }

        private static void Measure(NodeStateMemoryEvidence evidence, string name, Func<int, object> factory, int count)
        {
            _ = Allocate(factory, count);
            for (int sample = 1; sample <= 3; sample++)
            {
                evidence.Sample("AllocatedCurrentThread", name, sample, count, Allocate(factory, count));
                s_sink = null;
                object[] roots = new object[count];
                long before = NodeStateMemoryEvidence.FullHeap();
                Fill(roots, factory);
                long after = NodeStateMemoryEvidence.FullHeap();
                GC.KeepAlive(roots);
                evidence.Sample("RootedLiveEstimate", name, sample, count, after - before);
                Array.Clear(roots);
                GC.KeepAlive(roots);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long Allocate(Func<int, object> factory, int count)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++)
            {
                s_sink = factory(i);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(s_sink);
            return allocated;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Fill(object[] roots, Func<int, object> factory)
        {
            for (int i = 0; i < roots.Length; i++)
            {
                roots[i] = factory(i);
            }
        }

        private static long Stride<T>()
        {
            var elements = new T[2];
            return Unsafe.ByteOffset(ref elements[0], ref elements[1]);
        }
#endif

        private static ServiceResult ReadValue(
            ISystemContext context,
            NodeState node,
            NumericRange range,
            QualifiedName encoding,
            ref Variant value,
            ref StatusCode status,
            ref DateTimeUtc timestamp)
        {
            return ServiceResult.Good;
        }

#if NET10_0_OR_GREATER
        private static object? s_sink;
#endif
        private static readonly string[] s_parentOccupancy = ["1", "1", "1", "3", "2", "0"];
        private static readonly string[] s_removedChildOccupancy = ["0", "0", "1", "2", "1", "0"];
        private static readonly string[] s_categories = ["Category"];
    }
}
