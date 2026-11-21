// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Measures actual filtered and unfiltered browsers over the shared reference populations.
    /// </summary>
    /// <remarks>
    /// BenchmarkDotNet requires an unsealed declaring type for its generated harness.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    [MemoryDiagnoser]
    [Config(typeof(NodeStateStorageBenchmarks.StorageBenchmarkConfig))]
    public class NodeStateReferenceBenchmarks
    {
        [Params(0, 1, 2, 4, 8, 16, 128, 1024)]
        public int Degree { get; set; }

        [Params(0, 1, 2)]
        public int ReferenceMode { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            m_node = NodeStateMemoryScenarios.Construct(
                NodeStateMemoryScenarios.Select($"Object.References.{ReferenceMode}.{Degree}"), 17);
        }

        [Benchmark]
        public int FilteredBrowse()
        {
            return Browse(ReferenceTypeIds.HasComponent);
        }

        [Benchmark]
        public int UnfilteredBrowse()
        {
            return Browse(default);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(128)]
        [TestCase(1024)]
        public void BrowserBenchmarksSelectPopulationsAndDoNotMutateReferences(int degree)
        {
            Degree = degree;
            for (int mode = 0; mode < 3; mode++)
            {
                ReferenceMode = mode;
                Setup();
                Assert.That(FilteredBrowse(), Is.EqualTo(degree));
                Assert.That(UnfilteredBrowse(), Is.EqualTo(degree + 1));
                using INodeBrowser intrinsic = m_node.CreateBrowser(m_context, null,
                    ReferenceTypeIds.HasTypeDefinition, false, BrowseDirection.Forward, default, null, false);
                IReference? typeDefinition = intrinsic.Next();
                Assert.That(typeDefinition, Is.Not.Null);
                Assert.That(typeDefinition!.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasTypeDefinition));
                Assert.That(typeDefinition.TargetId, Is.EqualTo(new ExpandedNodeId(ObjectTypeIds.BaseObjectType)));
                Assert.That(typeDefinition.IsInverse, Is.False);
                Assert.That(intrinsic.Next(), Is.Null);
                var references = new List<IReference>();
                m_node.GetReferences(m_context, references);
                Assert.That(references, Has.Count.EqualTo(degree));
                for (int index = 0; index < degree; index++)
                {
                    ExpandedNodeId target = mode == 1
                        ? new ExpandedNodeId(new NodeId((uint)(50000 + index), 2), "urn:memory:targets", 0)
                        : new NodeId((uint)(50000 + index), 2);
                    Assert.That(references[index].TargetId, Is.EqualTo(target));
                    Assert.That(references[index].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                    Assert.That(references[index].IsInverse, Is.False);
                }
            }
        }

        private int Browse(NodeId referenceType)
        {
            using INodeBrowser browser = m_node.CreateBrowser(
                m_context, null, referenceType, false, BrowseDirection.Both, default, null, false);
            int count = 0;
            while (browser.Next() != null)
            {
                count++;
            }
            return count;
        }

        private readonly SystemContext m_context = new(NUnitTelemetryContext.CreateForBenchmarks());
        private NodeState m_node = null!;
    }
}
