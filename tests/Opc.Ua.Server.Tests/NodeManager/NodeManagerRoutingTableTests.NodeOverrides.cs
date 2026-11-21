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
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerRoutingTableTests
    {
        /// <summary>
        /// Each routing snapshot retains the manager that owns its exact override.
        /// </summary>
        [Test]
        public void NodeOverrideIsCapturedPerRoutingSnapshot()
        {
            var nodeId = new NodeId(29878u);
            IAsyncNodeManager first = CreateOverrideManager(nodeId);
            IAsyncNodeManager second = CreateOverrideManager(nodeId);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var previous = new NodeManagerRoutingTable.RoutingSnapshot([first], routes, []);
            var next = new NodeManagerRoutingTable.RoutingSnapshot([second], routes, []);

            Assert.That(previous.NodeOverrides[nodeId], Is.SameAs(first));
            Assert.That(next.NodeOverrides[nodeId], Is.SameAs(second));
        }

        /// <summary>
        /// A candidate snapshot cannot publish the same overridden node from two managers.
        /// </summary>
        [Test]
        public void DuplicateNodeOverridesAreRejectedBeforePublication()
        {
            var nodeId = new NodeId(29878u);
            IAsyncNodeManager first = CreateOverrideManager(nodeId);
            IAsyncNodeManager second = CreateOverrideManager(nodeId);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();

            Assert.Throws<InvalidOperationException>(() =>
                new NodeManagerRoutingTable.RoutingSnapshot([first, second], routes, []));
        }

        /// <summary>
        /// Hidden managers do not expose their exact node overrides in the routing snapshot.
        /// </summary>
        [Test]
        public void HiddenNodeOverridesAreNotPublished()
        {
            var nodeId = new NodeId(29878u);
            IAsyncNodeManager owner = CreateOverrideManager(nodeId);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var snapshot = new NodeManagerRoutingTable.RoutingSnapshot([owner], routes, [owner]);

            Assert.That(snapshot.NodeOverrides.ContainsKey(nodeId), Is.False);
        }

        /// <summary>
        /// Publication rejects an override when a visible browser owner lacks the safety capability.
        /// </summary>
        [Test]
        public void OverridePublicationRejectsAnUncertifiedBrowserOwner()
        {
            var target = new NodeId(29878u);
            IAsyncNodeManager owner = CreateOverrideManager(target);
            var source = new Mock<IAsyncNodeManager>();
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var current = new NodeManagerRoutingTable.RoutingSnapshot([source.Object], routes, []);
            var next = new NodeManagerRoutingTable.RoutingSnapshot([source.Object, owner], routes, []);

            Assert.That(() => OverrideBrowsePublicationValidator.Validate(current, next),
                Throws.TypeOf<NotSupportedException>());
        }

        /// <summary>
        /// Publication asks certified managers to accept the exact changed override targets.
        /// </summary>
        [Test]
        public void OverridePublicationRejectsACapabilityThatDeclinesTheChangedTarget()
        {
            var target = new NodeId(29878u);
            var browser = new Mock<IAsyncNodeManager>();
            var capability = browser.As<INodeManagerOverrideBrowseCapability>();
            capability.Setup(owner => owner.SupportsOverrideBrowse(It.IsAny<ArrayOf<NodeId>>())).Returns(false);
            IAsyncNodeManager candidate = CreateCertifiedOverrideManager(target);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var current = new NodeManagerRoutingTable.RoutingSnapshot([browser.Object], routes, []);
            var next = new NodeManagerRoutingTable.RoutingSnapshot([browser.Object, candidate], routes, []);

            Assert.That(() => OverrideBrowsePublicationValidator.Validate(current, next),
                Throws.TypeOf<NotSupportedException>());
            capability.Verify(owner => owner.SupportsOverrideBrowse(
                It.Is<ArrayOf<NodeId>>(targets => targets.Count == 1 && targets[0] == target)), Times.Once);
            Assert.That(current.NodeOverrides.ContainsKey(target), Is.False);
        }

        /// <summary>
        /// Ordinary routing changes remain available to managers without override support.
        /// </summary>
        [Test]
        public void OrdinaryPublicationDoesNotRequireOverrideBrowseCapability()
        {
            var source = new Mock<IAsyncNodeManager>();
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var current = new NodeManagerRoutingTable.RoutingSnapshot([source.Object], routes, []);
            var next = new NodeManagerRoutingTable.RoutingSnapshot([source.Object], routes, []);

            Assert.DoesNotThrow(() => OverrideBrowsePublicationValidator.Validate(current, next));
        }

        /// <summary>
        /// New visible managers must certify browsing for overrides that are already published.
        /// </summary>
        [Test]
        public void NewBrowserOwnerMustSupportAlreadyPublishedOverrides()
        {
            var target = new NodeId(29878u);
            IAsyncNodeManager owner = CreateCertifiedOverrideManager(target);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var current = new NodeManagerRoutingTable.RoutingSnapshot([owner], routes, []);
            var unsafeSource = new Mock<IAsyncNodeManager>();
            var rejected = new NodeManagerRoutingTable.RoutingSnapshot([owner, unsafeSource.Object], routes, []);

            Assert.That(() => OverrideBrowsePublicationValidator.Validate(current, rejected),
                Throws.TypeOf<NotSupportedException>());

            var safeSource = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerOverrideBrowseCapability> capability =
                safeSource.As<INodeManagerOverrideBrowseCapability>();
            capability.Setup(source => source.SupportsOverrideBrowse(It.IsAny<ArrayOf<NodeId>>())).Returns(true);
            var accepted = new NodeManagerRoutingTable.RoutingSnapshot([owner, safeSource.Object], routes, []);
            Assert.DoesNotThrow(() => OverrideBrowsePublicationValidator.Validate(current, accepted));
            capability.Verify(source => source.SupportsOverrideBrowse(
                It.Is<ArrayOf<NodeId>>(targets => targets.Count == 1 && targets[0] == target)), Times.Once);
        }

        /// <summary>
        /// Replacing or removing an override requires browser safety in both routing views.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OverrideReplacementAndRemovalRequireCapabilityFromBothViews(bool remove)
        {
            var target = new NodeId(29878u);
            IAsyncNodeManager first = CreateCertifiedOverrideManager(target);
            IAsyncNodeManager second = CreateCertifiedOverrideManager(target);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var current = new NodeManagerRoutingTable.RoutingSnapshot([first], routes, []);
            var next = new NodeManagerRoutingTable.RoutingSnapshot(remove ? [] : [second], routes, []);

            Assert.DoesNotThrow(() => OverrideBrowsePublicationValidator.Validate(current, next));
            var uncertified = CreateOverrideManager(target);
            var unsafeView = new NodeManagerRoutingTable.RoutingSnapshot([uncertified], routes, []);
            Assert.That(() => OverrideBrowsePublicationValidator.Validate(unsafeView, next),
                Throws.TypeOf<NotSupportedException>());
        }

        /// <summary>
        /// A continuation remains valid for an unchanged override and rejects its replacement.
        /// </summary>
        [Test]
        public void ContinuationRejectsOnlyChangedOverrideTargets()
        {
            var nodeId = new NodeId(29878u);
            IAsyncNodeManager first = CreateOverrideManager(nodeId);
            IAsyncNodeManager second = CreateOverrideManager(nodeId);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var previous = new NodeManagerRoutingTable.RoutingSnapshot([first], routes, []);
            var unchanged = new NodeManagerRoutingTable.RoutingSnapshot([first], routes, []);
            var replaced = new NodeManagerRoutingTable.RoutingSnapshot([second], routes, []);

            using var point = new ContinuationPoint
            {
                OverrideOwners = new Dictionary<NodeId, NodeManagerGenerationReference>
                {
                    [nodeId] = previous.GetGeneration(first).Reference
                }
            };
            Assert.DoesNotThrow(() => point.ValidateOverrideTarget(unchanged, nodeId));
            Assert.DoesNotThrow(() => point.ValidateOverrideTarget(replaced, new NodeId(42u)));
            Assert.That(() => point.ValidateOverrideTarget(replaced, nodeId),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        /// <summary>
        /// Continuations reject adding or removing an override for a target they will read.
        /// </summary>
        [Test]
        public void ContinuationRejectsAddingOrRemovingAnOverrideForItsTarget()
        {
            var nodeId = new NodeId(29878u);
            IAsyncNodeManager owner = CreateOverrideManager(nodeId);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var without = new NodeManagerRoutingTable.RoutingSnapshot([], routes, []);
            var with = new NodeManagerRoutingTable.RoutingSnapshot([owner], routes, []);
            using var before = new ContinuationPoint
            {
                OverrideOwners = new Dictionary<NodeId, NodeManagerGenerationReference>()
            };
            using var after = new ContinuationPoint
            {
                OverrideOwners = new Dictionary<NodeId, NodeManagerGenerationReference>
                {
                    [nodeId] = with.GetGeneration(owner).Reference
                }
            };

            Assert.That(() => before.ValidateOverrideTarget(with, nodeId),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(() => after.ValidateOverrideTarget(without, nodeId),
                Throws.TypeOf<ServiceResultException>());
            Assert.DoesNotThrow(() => before.ValidateOverrideTarget(with, new NodeId(42u)));
            Assert.DoesNotThrow(() => after.ValidateOverrideTarget(without, new NodeId(42u)));
        }

        /// <summary>
        /// Resuming a continuation revalidates every override target already observed.
        /// </summary>
        [Test]
        public void ContinuationRevalidatesPreviouslySeenTargetsBeforeResuming()
        {
            var observedId = new NodeId(29878u);
            var unrelatedId = new NodeId(29879u);
            IAsyncNodeManager first = CreateOverrideManager(observedId);
            IAsyncNodeManager replacement = CreateOverrideManager(observedId);
            IAsyncNodeManager unrelated = CreateOverrideManager(unrelatedId);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var original = new NodeManagerRoutingTable.RoutingSnapshot([first], routes, []);
            var unrelatedChange = new NodeManagerRoutingTable.RoutingSnapshot([first, unrelated], routes, []);
            var changed = new NodeManagerRoutingTable.RoutingSnapshot([replacement], routes, []);
            using var point = new ContinuationPoint
            {
                OverrideOwners = new Dictionary<NodeId, NodeManagerGenerationReference>
                {
                    [observedId] = original.GetGeneration(first).Reference
                }
            };
            point.ValidateOverrideTarget(original, observedId);

            Assert.DoesNotThrow(() => point.ValidateOverrideTargets(unrelatedChange));
            Assert.That(() => point.ValidateOverrideTargets(changed),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        /// <summary>
        /// Continuations reject override changes before lazily reading previously unseen targets.
        /// </summary>
        [Test]
        public void ContinuationRejectsOverrideChangesBeforeUnseenLazyTargetsAreRead()
        {
            var targetId = new NodeId(29878u);
            IAsyncNodeManager owner = CreateOverrideManager(targetId);
            IAsyncNodeManager replacement = CreateOverrideManager(targetId);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var empty = new NodeManagerRoutingTable.RoutingSnapshot([], routes, []);
            var original = new NodeManagerRoutingTable.RoutingSnapshot([owner], routes, []);
            var unchanged = new NodeManagerRoutingTable.RoutingSnapshot([owner], routes, []);
            var changed = new NodeManagerRoutingTable.RoutingSnapshot([replacement], routes, []);
            using var before = new ContinuationPoint
            {
                OverrideOwners = new Dictionary<NodeId, NodeManagerGenerationReference>()
            };
            using var after = new ContinuationPoint
            {
                OverrideOwners = new Dictionary<NodeId, NodeManagerGenerationReference>
                {
                    [targetId] = original.GetGeneration(owner).Reference
                }
            };

            Assert.DoesNotThrow(() => before.ValidateOverrideOwners(empty));
            Assert.DoesNotThrow(() => after.ValidateOverrideOwners(unchanged));
            Assert.That(() => before.ValidateOverrideOwners(original),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(() => after.ValidateOverrideOwners(changed),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(() => after.ValidateOverrideOwners(empty),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        /// <summary>
        /// Continuations reject changed source references that could expose an overridden target.
        /// </summary>
        [Test]
        public void ContinuationRejectsChangedSourceReferencesForOverrideTargets()
        {
            var targetId = new NodeId(29878u);
            var unrelatedId = new NodeId(42u);
            IAsyncNodeManager owner = CreateOverrideManager(targetId);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var snapshot = new NodeManagerRoutingTable.RoutingSnapshot([owner], routes, []);
            var source = new BaseObjectState(null);
            source.AddReference(ReferenceTypeIds.Organizes, false, targetId);
            ISystemContext context = new Mock<ISystemContext>().Object;
            using var point = new ContinuationPoint
            {
                OverrideOwners = new Dictionary<NodeId, NodeManagerGenerationReference>
                {
                    [targetId] = snapshot.GetGeneration(owner).Reference
                }
            };
            point.ValidateOverrideReferences(snapshot, source, context);

            source.AddReference(ReferenceTypeIds.Organizes, false, unrelatedId);
            Assert.DoesNotThrow(() => point.ValidateOverrideReferences(snapshot, source, context));
            source.RemoveReference(ReferenceTypeIds.Organizes, false, targetId);
            Assert.That(() => point.ValidateOverrideReferences(snapshot, source, context),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        /// <summary>
        /// The browser wrapper defers local overridden targets and preserves push and disposal behavior.
        /// </summary>
        [Test]
        public async Task BrowserDefersLocalTargetsAndPreservesPushAndAsyncDisposalAsync()
        {
            var nodeId = new NodeId(29878u);
            IAsyncNodeManager owner = CreateOverrideManager(nodeId);
            var snapshot = new NodeManagerRoutingTable.RoutingSnapshot(
                [owner], new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(), []);
            var target = new BaseObjectState(null) { NodeId = nodeId };
            var original = new NodeStateReference(ReferenceTypeIds.HasComponent, false, target);
            var inner = new Mock<INodeBrowser>();
            inner.Setup(browser => browser.NextAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReference?>(original));
            var disposable = inner.As<IAsyncDisposable>();
            disposable.Setup(browser => browser.DisposeAsync()).Returns(default(ValueTask));

            INodeBrowser browser = OverrideNodeBrowser.Wrap(inner.Object, snapshot);
            IReference? visible = await browser.NextAsync().ConfigureAwait(false);
            Assert.That(visible, Is.TypeOf<NodeStateReference>());
            Assert.That(((NodeStateReference)visible!).Target, Is.Null);
            Assert.That(visible.TargetId, Is.EqualTo((ExpandedNodeId)nodeId));
            browser.Push(visible);
            inner.Verify(item => item.Push(original), Times.Once);
            await ((IAsyncDisposable)browser).DisposeAsync().ConfigureAwait(false);
            disposable.Verify(item => item.DisposeAsync(), Times.Once);
            inner.Verify(item => item.Dispose(), Times.Never);
        }

        /// <summary>
        /// A routing snapshot without overrides leaves the original browser unchanged.
        /// </summary>
        [Test]
        public void BrowserWithoutOverridesKeepsTheOriginalBrowser()
        {
            var snapshot = new NodeManagerRoutingTable.RoutingSnapshot(
                [], new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(), []);
            var browser = new Mock<INodeBrowser>();
            Assert.That(OverrideNodeBrowser.Wrap(browser.Object, snapshot), Is.SameAs(browser.Object));
        }

        /// <summary>
        /// The built-in browser path accepts valid exact target lists and rejects null or malformed lists.
        /// </summary>
        [Test]
        public void BuiltInBrowserCapabilityRequiresNonNullTargets()
        {
            Assert.That(OverrideNodeBrowser.Supports([new NodeId(29878u)]), Is.True);
            Assert.That(OverrideNodeBrowser.Supports(ArrayOf.Empty<NodeId>()), Is.True);
            Assert.That(OverrideNodeBrowser.Supports(default), Is.False);
            Assert.That(OverrideNodeBrowser.Supports([NodeId.Null]), Is.False);
        }

        /// <summary>
        /// Frozen reference images remain independent when routing snapshots are created.
        /// </summary>
        [Test]
        public void FrozenReferenceImagesRemainIndependentAcrossViews()
        {
            var source = new BaseObjectState(null);
            var target = new NodeId(42u, 1);
            var original = new NodeStateReference(ReferenceTypeIds.Organizes, false, target);
            var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
            var oldView = new NodeManagerRoutingTable.RoutingSnapshot([], routes, []);
            var newView = new NodeManagerRoutingTable.RoutingSnapshot([], routes, []);
            oldView.InstallReferenceImages(new Dictionary<NodeState, ArrayOf<IReference>>
            {
                [source] = [original]
            });
            newView.InstallReferenceImages(new Dictionary<NodeState, ArrayOf<IReference>>
            {
                [source] = []
            });

            Assert.That(oldView.TryGetReferences(source, out ArrayOf<IReference> oldReferences), Is.True);
            Assert.That(newView.TryGetReferences(source, out ArrayOf<IReference> newReferences), Is.True);
            Assert.That(oldReferences.Count, Is.EqualTo(1));
            Assert.That(oldReferences[0].TargetId, Is.EqualTo((ExpandedNodeId)target));
            Assert.That(newReferences.Count, Is.Zero);
        }

        /// <summary>
        /// Retirement preserves pinned routing while discarding unpublished preparation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OverrideRetirementPreservesPinnedViewAndDiscardedPreparation(bool publish)
        {
            var nodeId = new NodeId(29878u);
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager owner = CreateOverrideManager(nodeId);
            table.Add(owner, InitialNamespaceIndexes);
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            context.PinRouting(table);
            NodeManagerRoutingTable.RoutingSnapshot previous = context.RoutingSnapshot!;

            using (NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                [], [owner], _ => InitialNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create()))
            {
                routes.Reserve();
                Assert.That(table.Revision.NodeOverrides[nodeId], Is.SameAs(owner));
                if (publish)
                {
                    routes.Publish();
                }
            }

            Assert.That(previous.NodeOverrides[nodeId], Is.SameAs(owner));
            Assert.That(table.Revision.NodeOverrides.ContainsKey(nodeId), Is.EqualTo(!publish));
        }

        private static IAsyncNodeManager CreateOverrideManager(NodeId nodeId)
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.As<INodeManagerNodeOverrides>()
                .SetupGet(owner => owner.OverriddenNodeIds).Returns((ArrayOf<NodeId>)[nodeId]);
            return manager.Object;
        }

        private static IAsyncNodeManager CreateCertifiedOverrideManager(NodeId nodeId)
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.As<INodeManagerNodeOverrides>()
                .SetupGet(owner => owner.OverriddenNodeIds).Returns((ArrayOf<NodeId>)[nodeId]);
            manager.As<INodeManagerOverrideBrowseCapability>()
                .Setup(owner => owner.SupportsOverrideBrowse(It.IsAny<ArrayOf<NodeId>>())).Returns(true);
            return manager.Object;
        }
    }
}
