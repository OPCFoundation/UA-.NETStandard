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
            for (int i = 1; i < ushort.MaxValue; i++)
            {
                namespaces.Append("urn:test:padding");
            }
            const string LastValidUri = "urn:test:last-valid";
            Assert.That(namespaces.Append(LastValidUri), Is.EqualTo(ushort.MaxValue));
            Assert.That(WotConModelPartition.GetRequiredNamespaceIndex(namespaces, LastValidUri),
                Is.EqualTo(ushort.MaxValue));
            const string OverflowUri = "urn:test:overflow";
            Assert.That(namespaces.Append(OverflowUri), Is.EqualTo(ushort.MaxValue + 1));

            Assert.That(
                () => WotConModelPartition.GetRequiredNamespaceIndex(namespaces, OverflowUri),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadConfigurationError));
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
