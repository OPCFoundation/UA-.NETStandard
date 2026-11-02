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
 *
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

#pragma warning disable CA2000

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [NonParallelizable]
    public sealed class StagedHistorianProviderRegistryTests
    {
        private const string kNamespaceUri = "urn:test:staged-historian";

        [Test]
        public void CandidateRegistrationsRemainPrivateUntilPublishAndPreserveScopePrecedence()
        {
            const string otherNamespaceUri = "urn:test:staged-historian:other";
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);
            namespaceTable.Append(otherNamespaceUri);
            var serving = new HistorianProviderRegistry(namespaceTable);
            var legacyNode = new TrackingProvider();
            var legacyNamespace = new TrackingProvider();
            var legacyOtherNamespace = new TrackingProvider();
            var legacyDefault = new TrackingProvider();
            var candidateNode = new TrackingProvider();
            var candidateNamespace = new TrackingProvider();
            var candidateDefault = new TrackingProvider();
            var exactNode = new NodeId("exact-node", 1);
            var servingExactNode = new NodeId("serving-exact-node", 1);
            var servingOtherNamespaceNode = new NodeId("serving-other-namespace-node", 2);
            object owner = new();
            using var candidate = new StagedHistorianProviderRegistry(namespaceTable, serving, owner);

            serving.RegisterDefault(legacyDefault);
            serving.RegisterForNamespace(kNamespaceUri, legacyNamespace);
            serving.RegisterForNamespace(otherNamespaceUri, legacyOtherNamespace);
            serving.RegisterForNode(exactNode, legacyNode);
            var servingExact = new TrackingProvider();
            serving.RegisterForNode(servingExactNode, servingExact);
            candidate.RegisterDefault(candidateDefault);
            candidate.RegisterForNamespace(kNamespaceUri, candidateNamespace);
            candidate.RegisterForNode(exactNode, candidateNode);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Resolve(exactNode), Is.SameAs(candidateNode));
                Assert.That(candidate.Resolve(servingExactNode), Is.SameAs(servingExact));
                Assert.That(
                    candidate.Resolve(new NodeId("namespace-node", 1)),
                    Is.SameAs(candidateNamespace));
                Assert.That(candidate.Resolve(new NodeId("default-node", 0)), Is.SameAs(candidateDefault));
                Assert.That(
                    candidate.Resolve(servingOtherNamespaceNode),
                    Is.SameAs(legacyOtherNamespace));
                Assert.That(serving.Resolve(exactNode), Is.SameAs(legacyNode));
                Assert.That(
                    serving.Resolve(new NodeId("namespace-node", 1)),
                    Is.SameAs(legacyNamespace));
                Assert.That(serving.Resolve(new NodeId("default-node", 0)), Is.SameAs(legacyDefault));
                Assert.That(candidate.Providers.Contains(candidateNode), Is.True);
                Assert.That(candidate.Providers.Contains(candidateNamespace), Is.True);
                Assert.That(candidate.Providers.Contains(candidateDefault), Is.True);
            }

            candidate.Publish();
            candidate.Publish();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serving.Resolve(exactNode), Is.SameAs(candidateNode));
                Assert.That(serving.Resolve(servingExactNode), Is.SameAs(servingExact));
                Assert.That(
                    serving.Resolve(new NodeId("namespace-node", 1)),
                    Is.SameAs(candidateNamespace));
                Assert.That(serving.Resolve(new NodeId("default-node", 0)), Is.SameAs(candidateDefault));
                Assert.That(
                    serving.Resolve(servingOtherNamespaceNode),
                    Is.SameAs(legacyOtherNamespace));
                Assert.That(serving.Providers.Contains(candidateNode), Is.True);
                Assert.That(serving.Providers.Contains(candidateNamespace), Is.True);
                Assert.That(serving.Providers.Contains(candidateDefault), Is.True);
            }

            candidate.Dispose();
            serving.Dispose();
            Assert.That(candidateNode.DisposeCount, Is.EqualTo(1));
            Assert.That(candidateNamespace.DisposeCount, Is.EqualTo(1));
            Assert.That(candidateDefault.DisposeCount, Is.EqualTo(1));
            Assert.That(legacyNode.DisposeCount, Is.EqualTo(1));
            Assert.That(legacyNamespace.DisposeCount, Is.EqualTo(1));
            Assert.That(legacyOtherNamespace.DisposeCount, Is.EqualTo(1));
            Assert.That(legacyDefault.DisposeCount, Is.EqualTo(1));
            Assert.That(servingExact.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void SameOwnerReplacementRemovalAndClearUpdateCandidateAndServingSnapshots()
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);
            var serving = new HistorianProviderRegistry(namespaceTable);
            var originalNode = new TrackingProvider();
            var originalNamespace = new TrackingProvider();
            var originalDefault = new TrackingProvider();
            var firstNode = new TrackingProvider();
            var lastNode = new TrackingProvider();
            var firstNamespace = new TrackingProvider();
            var lastNamespace = new TrackingProvider();
            var firstDefault = new TrackingProvider();
            var lastDefault = new TrackingProvider();
            var nodeId = new NodeId("duplicate-node", 1);
            using var candidate = new StagedHistorianProviderRegistry(namespaceTable, serving, new object());

            serving.RegisterForNode(nodeId, originalNode);
            serving.RegisterForNamespace(kNamespaceUri, originalNamespace);
            serving.RegisterDefault(originalDefault);
            candidate.RegisterForNode(nodeId, firstNode);
            ArrayOf<IHistorianProvider> firstSnapshot = candidate.Providers;
            candidate.RegisterForNode(nodeId, lastNode);
            candidate.RegisterForNamespace(kNamespaceUri, firstNamespace);
            candidate.RegisterForNamespace(kNamespaceUri, lastNamespace);
            candidate.RegisterDefault(firstDefault);
            candidate.RegisterDefault(lastDefault);
            ArrayOf<IHistorianProvider> latestSnapshot = candidate.Providers;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Resolve(nodeId), Is.SameAs(lastNode));
                Assert.That(
                    candidate.Resolve(new NodeId("namespace-node", 1)),
                    Is.SameAs(lastNamespace));
                Assert.That(candidate.Resolve(new NodeId("default-node", 0)), Is.SameAs(lastDefault));
                Assert.That(firstSnapshot.Contains(firstNode), Is.True);
                Assert.That(firstSnapshot.Contains(lastNode), Is.False);
                Assert.That(latestSnapshot.Contains(firstNode), Is.False);
                Assert.That(latestSnapshot.Contains(lastNode), Is.True);
            }

            candidate.Publish();
            Assert.That(serving.Resolve(nodeId), Is.SameAs(lastNode));
            Assert.That(serving.Resolve(new NodeId("namespace-node", 1)), Is.SameAs(lastNamespace));
            Assert.That(serving.Resolve(new NodeId("default-node", 0)), Is.SameAs(lastDefault));

            Assert.That(candidate.UnregisterForNode(nodeId), Is.True);
            Assert.That(candidate.UnregisterForNode(nodeId), Is.False);
            Assert.That(candidate.UnregisterForNamespace(kNamespaceUri), Is.True);
            Assert.That(candidate.UnregisterForNamespace(kNamespaceUri), Is.False);
            candidate.ClearDefault();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Resolve(nodeId), Is.SameAs(originalNode));
                Assert.That(
                    candidate.Resolve(new NodeId("namespace-node", 1)),
                    Is.SameAs(originalNamespace));
                Assert.That(candidate.Resolve(new NodeId("default-node", 0)), Is.SameAs(originalDefault));
                Assert.That(candidate.Providers.Contains(firstNode), Is.False);
                Assert.That(candidate.Providers.Contains(lastNode), Is.False);
                Assert.That(candidate.Providers.Contains(firstNamespace), Is.False);
                Assert.That(candidate.Providers.Contains(lastNamespace), Is.False);
                Assert.That(candidate.Providers.Contains(firstDefault), Is.False);
                Assert.That(candidate.Providers.Contains(lastDefault), Is.False);
                Assert.That(latestSnapshot.Contains(lastNode), Is.True);
                Assert.That(latestSnapshot.Contains(lastNamespace), Is.True);
                Assert.That(latestSnapshot.Contains(lastDefault), Is.True);
                Assert.That(serving.Resolve(nodeId), Is.SameAs(originalNode));
                Assert.That(
                    serving.Resolve(new NodeId("namespace-node", 1)),
                    Is.SameAs(originalNamespace));
                Assert.That(serving.Resolve(new NodeId("default-node", 0)), Is.SameAs(originalDefault));
            }

            candidate.Dispose();
            serving.Dispose();
            Assert.That(lastNode.DisposeCount, Is.EqualTo(1));
            Assert.That(lastNamespace.DisposeCount, Is.EqualTo(1));
            Assert.That(lastDefault.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void RetiringPredecessorPreservesReplacementAndDirectBindingsAndKeepsLocalResolution()
        {
            const string directNamespaceUri = "urn:test:staged-historian:direct";
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);
            namespaceTable.Append(directNamespaceUri);
            var serving = new HistorianProviderRegistry(namespaceTable);
            var directNodeProvider = new TrackingProvider();
            var directNamespaceProvider = new TrackingProvider();
            var predecessorNode = new TrackingProvider();
            var predecessorNamespace = new TrackingProvider();
            var predecessorDefault = new TrackingProvider();
            var replacementNode = new TrackingProvider();
            var replacementNamespace = new TrackingProvider();
            var replacementDefault = new TrackingProvider();
            var overlapNode = new NodeId("overlap", 1);
            var directNode = new NodeId("direct", 2);
            var predecessorOwner = new object();
            using var predecessor =
                new StagedHistorianProviderRegistry(namespaceTable, serving, predecessorOwner);
            using var replacement =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());

            serving.RegisterForNode(directNode, directNodeProvider);
            serving.RegisterForNamespace(directNamespaceUri, directNamespaceProvider);
            predecessor.RegisterForNode(overlapNode, predecessorNode);
            predecessor.RegisterForNamespace(kNamespaceUri, predecessorNamespace);
            predecessor.RegisterDefault(predecessorDefault);
            predecessor.Publish();
            replacement.RegisterForNode(overlapNode, replacementNode);
            replacement.RegisterForNamespace(kNamespaceUri, replacementNamespace);
            replacement.RegisterDefault(replacementDefault);
            replacement.Publish();

            predecessor.Retire();
            predecessor.Retire();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serving.Resolve(overlapNode), Is.SameAs(replacementNode));
                Assert.That(
                    serving.Resolve(new NodeId("namespace-node", 1)),
                    Is.SameAs(replacementNamespace));
                Assert.That(serving.Resolve(new NodeId("default-node", 0)), Is.SameAs(replacementDefault));
                Assert.That(serving.Resolve(directNode), Is.SameAs(directNodeProvider));
                Assert.That(
                    serving.Resolve(new NodeId("direct-namespace-node", 2)),
                    Is.SameAs(directNamespaceProvider));
                Assert.That(predecessor.Resolve(overlapNode), Is.SameAs(predecessorNode));
                Assert.That(
                    predecessor.Resolve(new NodeId("namespace-node", 1)),
                    Is.SameAs(predecessorNamespace));
                Assert.That(
                    predecessor.Resolve(new NodeId("default-node", 0)),
                    Is.SameAs(predecessorDefault));
                Assert.That(predecessorNode.DisposeCount, Is.Zero);
                Assert.That(replacementNode.DisposeCount, Is.Zero);
            }

            predecessor.Dispose();
            predecessor.Dispose();
            Assert.That(serving.Resolve(overlapNode), Is.SameAs(replacementNode));
            Assert.That(serving.Resolve(directNode), Is.SameAs(directNodeProvider));
            Assert.That(replacementNode.DisposeCount, Is.Zero);

            serving.Dispose();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(predecessorNode.DisposeCount, Is.EqualTo(1));
                Assert.That(predecessorNamespace.DisposeCount, Is.EqualTo(1));
                Assert.That(predecessorDefault.DisposeCount, Is.EqualTo(1));
                Assert.That(replacementNode.DisposeCount, Is.EqualTo(1));
                Assert.That(replacementNamespace.DisposeCount, Is.EqualTo(1));
                Assert.That(replacementDefault.DisposeCount, Is.EqualTo(1));
                Assert.That(directNodeProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(directNamespaceProvider.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void RepeatedDisposalDoesNotDisposeProviderStillReferencedByDirectRegistration()
        {
            var namespaceTable = new NamespaceTable();
            var serving = new HistorianProviderRegistry(namespaceTable);
            var sharedProvider = new TrackingProvider();
            var candidateNode = new NodeId("shared-candidate", 0);
            serving.RegisterDefault(sharedProvider);
            using var candidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            candidate.RegisterForNode(candidateNode, sharedProvider);
            candidate.Publish();

            candidate.Dispose();
            candidate.Dispose();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serving.Resolve(new NodeId("still-direct", 0)), Is.SameAs(sharedProvider));
                Assert.That(serving.Resolve(candidateNode), Is.SameAs(sharedProvider));
                Assert.That(sharedProvider.DisposeCount, Is.Zero);
            }

            serving.Dispose();
            Assert.That(sharedProvider.DisposeCount, Is.EqualTo(1));
        }

        private sealed class TrackingProvider : IHistorianProvider, IDisposable
        {
            public int DisposeCount { get; private set; }

            public ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<HistorianNodeCapabilities>(HistorianNodeCapabilities.ReadOnly);
            }

            public ValueTask<bool> IsHistorizingAsync(
                NodeId nodeId,
                CancellationToken cancellationToken)
            {
                return new ValueTask<bool>(true);
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
