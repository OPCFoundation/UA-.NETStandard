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

        /// <summary>
        /// Explicit node ownership survives staging, publication, route removal and registry disposal.
        /// </summary>
        [Test]
        public void ExplicitNodeOwnershipSurvivesCandidateLifecycle(
            [Values("Abort", "Publish", "PublishedRegistration")] string lifecycle,
            [Values] bool unregister,
            [Values] bool ownsProvider)
        {
            var namespaceTable = new NamespaceTable();
            using var serving = new HistorianProviderRegistry(namespaceTable);
            using var candidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            var provider = new TrackingProvider();
            var nodeId = new NodeId("explicit-ownership", 0);
            IHistorianProviderRegistry registry = candidate;
            if (lifecycle == "PublishedRegistration")
            {
                candidate.Publish();
            }

            registry.RegisterForNode(nodeId, provider, ownsProvider);

            Assert.That(candidate.Resolve(nodeId), Is.SameAs(provider));
            if (lifecycle == "PublishedRegistration")
            {
                Assert.That(serving.Resolve(nodeId), Is.SameAs(provider));
            }
            else
            {
                Assert.That(serving.Resolve(nodeId), Is.Null);
            }
            if (lifecycle == "Publish")
            {
                candidate.Publish();
                Assert.That(serving.Resolve(nodeId), Is.SameAs(provider));
            }
            if (unregister)
            {
                Assert.That(candidate.UnregisterForNode(nodeId), Is.True);
                Assert.That(candidate.Resolve(nodeId), Is.Null);
                Assert.That(serving.Resolve(nodeId), Is.Null);
            }

            candidate.Dispose();
            Assert.That(serving.Resolve(nodeId), Is.Null);
            serving.Dispose();
            Assert.That(provider.DisposeCount, Is.EqualTo(ownsProvider ? 1 : 0));
            if (!ownsProvider)
            {
                provider.Dispose();
            }
        }

        /// <summary>
        /// Fluent historization borrows a direct serving default without creating staged state.
        /// </summary>
        [Test]
        public void PlainServingDefaultDoesNotCreateCandidateRegistration()
        {
            var namespaceTable = new NamespaceTable();
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var provider = new TrackingProvider();
            serving.RegisterDefault(provider);
            using var candidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());

            Assert.That(candidate.ResolveForHistorization(new NodeId("node", 0)), Is.SameAs(provider));
            Assert.That(candidate.HasRegistrations, Is.False);

            candidate.Dispose();
            Assert.That(provider.DisposeCount, Is.Zero);
        }

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

        /// <summary>
        /// Explicit candidate scopes override bindings retained by fluent historization regardless of scope.
        /// </summary>
        [Test]
        public void ExplicitCandidateRegistrationsOverrideRetainedHistorizationBindings()
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var servingNodeProvider = new TrackingProvider();
            var servingNamespaceProvider = new TrackingProvider();
            var servingDefaultProvider = new TrackingProvider();
            var explicitNodeProvider = new TrackingProvider();
            var explicitNamespaceProvider = new TrackingProvider();
            var explicitDefaultProvider = new TrackingProvider();
            var nodeId = new NodeId("historized-node", 1);
            var namespaceNodeId = new NodeId("historized-namespace-node", 1);
            var defaultNodeId = new NodeId("historized-default-node", 0);
            serving.RegisterForNode(nodeId, servingNodeProvider);
            serving.RegisterForNamespace(kNamespaceUri, servingNamespaceProvider);
            serving.RegisterDefault(servingDefaultProvider);
            using var candidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());

            Assert.That(candidate.ResolveForHistorization(nodeId), Is.SameAs(servingNodeProvider));
            Assert.That(
                candidate.ResolveForHistorization(namespaceNodeId),
                Is.SameAs(servingNamespaceProvider));
            Assert.That(
                candidate.ResolveForHistorization(defaultNodeId),
                Is.SameAs(servingDefaultProvider));

            Assert.That(() => candidate.RegisterForNode(nodeId, explicitNodeProvider), Throws.Nothing);
            Assert.That(
                () => candidate.RegisterForNamespace(kNamespaceUri, explicitNamespaceProvider),
                Throws.Nothing);
            Assert.That(() => candidate.RegisterDefault(explicitDefaultProvider), Throws.Nothing);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Resolve(nodeId), Is.SameAs(explicitNodeProvider));
                Assert.That(candidate.Resolve(namespaceNodeId), Is.SameAs(explicitNamespaceProvider));
                Assert.That(candidate.Resolve(defaultNodeId), Is.SameAs(explicitDefaultProvider));
            }

            candidate.Publish();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serving.Resolve(nodeId), Is.SameAs(explicitNodeProvider));
                Assert.That(serving.Resolve(namespaceNodeId), Is.SameAs(explicitNamespaceProvider));
                Assert.That(serving.Resolve(defaultNodeId), Is.SameAs(explicitDefaultProvider));
            }
        }

        /// <summary>
        /// Publishing an empty replacement removes a retained serving binding after its predecessor retires.
        /// </summary>
        [Test]
        public void EmptyReplacementPublicationRemovesRetainedServingBinding()
        {
            var namespaceTable = new NamespaceTable();
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var provider = new TrackingProvider();
            using var predecessor =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            using var replacement =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            var nodeId = new NodeId("retained-default", 0);

            predecessor.RegisterDefault(provider);
            predecessor.Publish();
            Assert.That(replacement.ResolveForHistorization(nodeId), Is.SameAs(provider));

            replacement.ClearDefault();
            replacement.Publish();
            predecessor.Retire();

            Assert.That(serving.Resolve(nodeId), Is.Null);
        }

        /// <summary>
        /// A staged provider published by one candidate remains serving-owned when another candidate aborts.
        /// </summary>
        [Test]
        public void PublishedProviderRemainsOwnedWhenAnotherCandidateAborts()
        {
            var namespaceTable = new NamespaceTable();
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var sharedProvider = new TrackingProvider();
            var publishedCandidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            var abortedCandidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            publishedCandidate.RegisterDefault(sharedProvider);
            abortedCandidate.RegisterForNamespace(kNamespaceUri, sharedProvider);

            publishedCandidate.Publish();
            publishedCandidate.Dispose();
            abortedCandidate.Dispose();

            Assert.That(sharedProvider.DisposeCount, Is.Zero);

            serving.Dispose();

            Assert.That(sharedProvider.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Shared providers held only by unpublished candidates are disposed after the last candidate releases them.
        /// </summary>
        [Test]
        public void SharedProviderAcrossAbortedCandidatesIsDisposedOnce()
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var sharedProvider = new TrackingProvider();
            var first = new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            var second = new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            first.RegisterDefault(sharedProvider);
            second.RegisterForNamespace(kNamespaceUri, sharedProvider);

            first.Dispose();

            Assert.That(sharedProvider.DisposeCount, Is.Zero);

            second.Dispose();

            Assert.That(sharedProvider.DisposeCount, Is.EqualTo(1));
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
            Assert.That(
                () => candidate.RegisterForNode(nodeId, lastNode),
                Throws.InvalidOperationException);
            Assert.That(candidate.UnregisterForNode(nodeId), Is.True);
            candidate.RegisterForNode(nodeId, lastNode);
            candidate.RegisterForNamespace(kNamespaceUri, firstNamespace);
            Assert.That(
                () => candidate.RegisterForNamespace(kNamespaceUri, lastNamespace),
                Throws.InvalidOperationException);
            Assert.That(candidate.UnregisterForNamespace(kNamespaceUri), Is.True);
            candidate.RegisterForNamespace(kNamespaceUri, lastNamespace);
            candidate.RegisterDefault(firstDefault);
            Assert.That(
                () => candidate.RegisterDefault(lastDefault),
                Throws.InvalidOperationException);
            candidate.ClearDefault();
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

        /// <summary>
        /// Serving unregistration removes a scope across owners without changing local images or provider ownership.
        /// </summary>
        [TestCase("Node", false)]
        [TestCase("Node", true)]
        [TestCase("Namespace", false)]
        [TestCase("Namespace", true)]
        [TestCase("Default", false)]
        [TestCase("Default", true)]
        public void ServingUnregistrationRemovesOwnedBindingsAndPreservesOtherScopes(string scope, bool direct)
        {
            const string otherNamespaceUri = "urn:test:staged-historian:other";
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);
            namespaceTable.Append(otherNamespaceUri);
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var firstProvider = new TrackingProvider();
            var secondProvider = new TrackingProvider();
            var ownedOtherProvider = new TrackingProvider();
            var directOtherProvider = new TrackingProvider();
            TrackingProvider? directProvider = direct ? new TrackingProvider() : null;
            var targetNode = new NodeId("target", 1);
            var ownedOtherNode = new NodeId("owned-other", 2);
            var directOtherNode = new NodeId("direct-other", 2);
            using var first = new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            using var second = new StagedHistorianProviderRegistry(namespaceTable, serving, new object());

            serving.RegisterForNode(directOtherNode, directOtherProvider);
            first.RegisterForNode(ownedOtherNode, ownedOtherProvider);
            if (directProvider != null)
            {
                RegisterTarget(serving, directProvider);
            }
            RegisterTarget(first, firstProvider);
            RegisterTarget(second, secondProvider);
            first.Publish();
            second.Publish();
            Assert.That(serving.Resolve(targetNode), Is.SameAs(secondProvider));

            switch (scope)
            {
                case "Node":
                    Assert.That(serving.UnregisterForNode(targetNode), Is.True);
                    Assert.That(serving.UnregisterForNode(targetNode), Is.False);
                    break;
                case "Namespace":
                    Assert.That(serving.UnregisterForNamespace(kNamespaceUri), Is.True);
                    Assert.That(serving.UnregisterForNamespace(kNamespaceUri), Is.False);
                    break;
                case "Default":
                    serving.ClearDefault();
                    serving.ClearDefault();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serving.Resolve(targetNode), Is.Null);
                Assert.That(serving.Resolve(ownedOtherNode), Is.SameAs(ownedOtherProvider));
                Assert.That(serving.Resolve(directOtherNode), Is.SameAs(directOtherProvider));
                Assert.That(serving.Providers.Contains(firstProvider), Is.False);
                Assert.That(serving.Providers.Contains(secondProvider), Is.False);
                Assert.That(serving.Providers.Contains(ownedOtherProvider), Is.True);
                if (directProvider != null)
                {
                    Assert.That(serving.Providers.Contains(directProvider), Is.False);
                    Assert.That(directProvider.DisposeCount, Is.Zero);
                }
                Assert.That(first.Resolve(targetNode), Is.SameAs(firstProvider));
                Assert.That(second.Resolve(targetNode), Is.SameAs(secondProvider));
                Assert.That(firstProvider.DisposeCount, Is.Zero);
                Assert.That(secondProvider.DisposeCount, Is.Zero);
            }

            second.Retire();
            first.Retire();
            first.Dispose();
            second.Dispose();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serving.Resolve(targetNode), Is.Null);
                Assert.That(serving.Resolve(ownedOtherNode), Is.Null);
                Assert.That(serving.Resolve(directOtherNode), Is.SameAs(directOtherProvider));
                Assert.That(serving.Providers.Contains(ownedOtherProvider), Is.False);
                Assert.That(serving.Providers.Contains(directOtherProvider), Is.True);
                Assert.That(firstProvider.DisposeCount, Is.Zero);
                Assert.That(secondProvider.DisposeCount, Is.Zero);
            }

            serving.Dispose();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(secondProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(ownedOtherProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(directOtherProvider.DisposeCount, Is.EqualTo(1));
                if (directProvider != null)
                {
                    Assert.That(directProvider.DisposeCount, Is.EqualTo(1));
                }
            }

            void RegisterTarget(IHistorianProviderRegistry registry, IHistorianProvider provider)
            {
                switch (scope)
                {
                    case "Node":
                        registry.RegisterForNode(targetNode, provider);
                        break;
                    case "Namespace":
                        registry.RegisterForNamespace(kNamespaceUri, provider);
                        break;
                    case "Default":
                        registry.RegisterDefault(provider);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(scope));
                }
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

        /// <summary>
        /// Abort preserves serving-owned provider lifetimes and disposes only candidate-exclusive providers.
        /// </summary>
        [Test]
        public void AbortedCandidatePreservesServingOwnedProvidersAndDisposesExclusiveProviderOnce()
        {
            var namespaceTable = new NamespaceTable();
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var boundSharedProvider = new TrackingProvider();
            var unregisteredSharedProvider = new TrackingProvider();
            var exclusiveProvider = new TrackingProvider();
            var unregisteredNode = new NodeId("unregistered-serving-provider", 0);
            var candidateNode = new NodeId("shared-candidate", 0);
            var unregisteredCandidateNode = new NodeId("unregistered-shared-candidate", 0);
            serving.RegisterDefault(boundSharedProvider);
            serving.RegisterForNode(unregisteredNode, unregisteredSharedProvider);
            Assert.That(serving.UnregisterForNode(unregisteredNode), Is.True);
            Assert.That(serving.Providers.Contains(unregisteredSharedProvider), Is.False);

            using var candidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            candidate.RegisterForNode(candidateNode, boundSharedProvider);
            candidate.RegisterForNode(unregisteredCandidateNode, unregisteredSharedProvider);
            candidate.RegisterDefault(exclusiveProvider);

            candidate.Dispose();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serving.Resolve(candidateNode), Is.SameAs(boundSharedProvider));
                Assert.That(serving.Resolve(unregisteredCandidateNode), Is.SameAs(boundSharedProvider));
                Assert.That(serving.Providers.Contains(unregisteredSharedProvider), Is.False);
                Assert.That(boundSharedProvider.DisposeCount, Is.Zero);
                Assert.That(unregisteredSharedProvider.DisposeCount, Is.Zero);
                Assert.That(exclusiveProvider.DisposeCount, Is.EqualTo(1));
            }

            serving.Dispose();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(boundSharedProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(unregisteredSharedProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(exclusiveProvider.DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Duplicate candidate registrations fail; removed providers remain owned until candidate cleanup.
        /// </summary>
        [TestCase("Node", false)]
        [TestCase("Node", true)]
        [TestCase("Namespace", false)]
        [TestCase("Namespace", true)]
        [TestCase("Default", false)]
        [TestCase("Default", true)]
        public void CandidateRetainsSupersededProvidersUntilCleanupAcrossScopes(
            string scope,
            bool unregisterBeforeReplacement)
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var candidateProvider = new TrackingProvider();
            var publishedProvider = new TrackingProvider();
            var nodeId = new NodeId("candidate-superseded", 1);
            using var candidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());

            Register(scope, candidate, candidateProvider);
            if (unregisterBeforeReplacement)
            {
                Assert.That(Unregister(scope, candidate), Is.True);
                Register(scope, candidate, publishedProvider);
            }
            else
            {
                Assert.That(
                    () => Register(scope, candidate, publishedProvider),
                    Throws.InvalidOperationException);
            }
            candidate.Publish();

            Assert.That(
                Resolve(scope, serving, nodeId),
                Is.SameAs(unregisterBeforeReplacement ? publishedProvider : candidateProvider));
            Assert.That(candidateProvider.DisposeCount, Is.Zero);
            Assert.That(publishedProvider.DisposeCount, Is.Zero);

            candidate.Dispose();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidateProvider.DisposeCount, Is.EqualTo(unregisterBeforeReplacement ? 1 : 0));
                Assert.That(publishedProvider.DisposeCount, Is.Zero);
            }

            serving.Dispose();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidateProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(
                    publishedProvider.DisposeCount,
                    Is.EqualTo(unregisterBeforeReplacement ? 1 : 0));
            }

            void Register(
                string registrationScope,
                IHistorianProviderRegistry registry,
                IHistorianProvider provider)
            {
                switch (registrationScope)
                {
                    case "Node":
                        registry.RegisterForNode(nodeId, provider);
                        break;
                    case "Namespace":
                        registry.RegisterForNamespace(kNamespaceUri, provider);
                        break;
                    case "Default":
                        registry.RegisterDefault(provider);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(registrationScope));
                }
            }

            bool Unregister(string registrationScope, IHistorianProviderRegistry registry)
            {
                return registrationScope switch
                {
                    "Node" => registry.UnregisterForNode(nodeId),
                    "Namespace" => registry.UnregisterForNamespace(kNamespaceUri),
                    "Default" => ClearDefaultAndReturnTrue(registry),
                    _ => throw new ArgumentOutOfRangeException(nameof(registrationScope))
                };
            }

            IHistorianProvider? Resolve(
                string registrationScope,
                IHistorianProviderRegistry registry,
                NodeId targetNode)
            {
                return registrationScope switch
                {
                    "Node" => registry.Resolve(targetNode),
                    "Namespace" => registry.Resolve(new NodeId("namespace-scope", 1)),
                    "Default" => registry.Resolve(new NodeId("default-scope", 0)),
                    _ => throw new ArgumentOutOfRangeException(nameof(registrationScope))
                };
            }

            static bool ClearDefaultAndReturnTrue(IHistorianProviderRegistry registry)
            {
                registry.ClearDefault();
                return true;
            }
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

        /// <summary>
        /// Candidate abort must not adopt disposal of a serving DI singleton, in any candidate scope.
        /// </summary>
        [TestCase("Node")]
        [TestCase("Namespace")]
        [TestCase("Default")]
        public async Task AbortedCandidatePreservesNonOwningServingSingletonAndDisposesExclusiveProviderOnceAsync(
            string scope)
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(kNamespaceUri);
            using var sharedProvider = new BorrowedSingletonProvider();
            using var serving = new HistorianProviderRegistry(namespaceTable);
            var exclusiveProvider = new TrackingProvider();
            var sharedNode = new NodeId("borrowed-singleton", 1);
            var exclusiveNode = new NodeId("candidate-exclusive", 1);

            // AddHistorian<TProvider> resolves a DI singleton and registers it with this ownership setting.
            serving.RegisterDefault(sharedProvider, ownsProvider: false);
            using var candidate =
                new StagedHistorianProviderRegistry(namespaceTable, serving, new object());
            switch (scope)
            {
                case "Node":
                    candidate.RegisterForNode(sharedNode, sharedProvider);
                    break;
                case "Namespace":
                    candidate.RegisterForNamespace(kNamespaceUri, sharedProvider);
                    break;
                case "Default":
                    candidate.RegisterDefault(sharedProvider);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope));
            }
            candidate.RegisterForNode(exclusiveNode, exclusiveProvider);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Resolve(sharedNode), Is.SameAs(sharedProvider));
                Assert.That(candidate.Resolve(exclusiveNode), Is.SameAs(exclusiveProvider));
                Assert.That(serving.Resolve(exclusiveNode), Is.SameAs(sharedProvider));
                Assert.That(((IHistorianOwnedProviderRegistry)serving).OwnsProvider(sharedProvider), Is.False);
            }

            candidate.Dispose();
            candidate.Dispose();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sharedProvider.DisposeCount, Is.Zero);
                Assert.That(exclusiveProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(serving.Resolve(sharedNode), Is.SameAs(sharedProvider));
                Assert.That(serving.Resolve(exclusiveNode), Is.SameAs(sharedProvider));
                Assert.That(serving.Providers.Contains(sharedProvider), Is.True);
                Assert.That(serving.Providers.Contains(exclusiveProvider), Is.False);
            }
            IHistorianProvider resolved = serving.Resolve(sharedNode)!;
            HistorianNodeCapabilities capabilities = await resolved.GetCapabilitiesAsync(
                sharedNode, CancellationToken.None)
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(capabilities, Is.EqualTo(HistorianNodeCapabilities.ReadOnly));
                Assert.That(sharedProvider.LastCapabilitiesNode, Is.EqualTo(sharedNode));
                Assert.That(sharedProvider.CapabilitiesCallCount, Is.EqualTo(1));
            }

            serving.Dispose();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sharedProvider.DisposeCount, Is.Zero, "Only the DI owner may dispose the singleton.");
                Assert.That(exclusiveProvider.DisposeCount, Is.EqualTo(1));
            }
        }

        private sealed class BorrowedSingletonProvider : IHistorianProvider, IDisposable
        {
            public int DisposeCount { get; private set; }

            public int CapabilitiesCallCount { get; private set; }

            public NodeId LastCapabilitiesNode { get; private set; } = NodeId.Null;

            public ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                if (DisposeCount != 0)
                {
                    throw new ObjectDisposedException(nameof(BorrowedSingletonProvider));
                }
                LastCapabilitiesNode = nodeId;
                CapabilitiesCallCount++;
                return new ValueTask<HistorianNodeCapabilities>(HistorianNodeCapabilities.ReadOnly);
            }

            public ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken cancellationToken)
            {
                if (DisposeCount != 0)
                {
                    throw new ObjectDisposedException(nameof(BorrowedSingletonProvider));
                }
                return new ValueTask<bool>(true);
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
