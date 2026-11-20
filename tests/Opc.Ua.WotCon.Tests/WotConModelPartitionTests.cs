/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Validates the namespace indexes used by WoT address-space ownership.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotConModelPartitionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PartitionRejectsMissingNamespaceWithoutMutatingNodes(bool registry)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable()
            };
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(WotConModelPartition.FirstRegistryNodeId, ushort.MaxValue)
            };
            NodeStateCollection nodes = [node];

            Assert.That(
                () => RetainPartition(registry, nodes, context),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(nodes, Has.Count.EqualTo(1));
            Assert.That(nodes[0], Is.SameAs(node));
        }

        [TestCase("urn:test:assets")]
        [TestCase(Namespaces.WotCon)]
        [TestCase(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri)]
        public void RequiredNamespaceIndexPreservesRegisteredSlotAndRejectsAbsence(string namespaceUri)
        {
            var namespaces = new NamespaceTable();
            Assert.That(
                () => WotConModelPartition.GetRequiredNamespaceIndex(namespaces, namespaceUri),
                Throws.TypeOf<ServiceResultException>());
            int index = namespaces.Append(namespaceUri);

            Assert.That(WotConModelPartition.GetRequiredNamespaceIndex(namespaces, namespaceUri), Is.EqualTo(index));
        }

        [Test]
        public void RequiredNamespaceIndexRejectsOverflowWithoutWrapping()
        {
            var namespaces = new NamespaceTable();
            // 0xFFFF is the "not mapped" marker, so 0xFFFE is the last usable index.
            const int LastValidIndex = ushort.MaxValue - 1;
            for (int i = 1; i < LastValidIndex; i++)
            {
                namespaces.Append("urn:test:padding");
            }
            const string LastValidUri = "urn:test:last-valid";
            Assert.That(namespaces.Append(LastValidUri), Is.EqualTo(LastValidIndex));
            Assert.That(WotConModelPartition.GetRequiredNamespaceIndex(namespaces, LastValidUri),
                Is.EqualTo(LastValidIndex));
            const string OverflowUri = "urn:test:overflow";

            // The table itself refuses indexes beyond the UInt16 range, so an
            // overflowing namespace can no longer wrap to a small index.
            Assert.That(
                () => namespaces.Append(OverflowUri),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(
                () => WotConModelPartition.GetRequiredNamespaceIndex(namespaces, OverflowUri),
                Throws.TypeOf<ServiceResultException>());
        }

        private static NodeStateCollection RetainPartition(
            bool registry,
            NodeStateCollection nodes,
            ISystemContext context)
        {
            return registry
                ? WotConModelPartition.RetainRegistryNodes(nodes, context)
                : WotConModelPartition.RetainLegacyNodes(nodes, context);
        }
    }
}
