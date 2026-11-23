// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    public sealed class NodeIdReservationsTests
    {
        [Test]
        public void NamespaceAliasesConflictWithoutReservingAnyOtherId()
        {
            var reservations = new NodeIdReservations();
            var first = new NamespaceTable();
            var second = new NamespaceTable();
            second.GetIndexOrAppend("urn:reservations:padding");
            ushort firstIndex = first.GetIndexOrAppend("urn:reservations:shared");
            ushort secondIndex = second.GetIndexOrAppend("urn:reservations:shared");
            NodeManagerGeneration owner = CreateGeneration(new NodeManagerSlot(), 1);
            NodeManagerGeneration contender = CreateGeneration(new NodeManagerSlot(), 1);
            NodeManagerGeneration independent = CreateGeneration(new NodeManagerSlot(), 1);
            reservations.Reserve(owner, first, [new NodeId("shared", firstIndex)]);

            var free = new NodeId("free", secondIndex);
            Assert.That(() => reservations.Reserve(contender, second, [free, new NodeId("shared", secondIndex)]),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNodeIdExists));
            Assert.That(() => reservations.Reserve(independent, second, [free]), Throws.Nothing);
        }

        /// <summary>
        /// Verifies that cleaning up an aborted candidate leaves the serving claims intact.
        /// </summary>
        [Test]
        public async Task AbortingReplacementDoesNotReleaseServingClaimsAsync()
        {
            var reservations = new NodeIdReservations();
            var namespaces = new NamespaceTable();
            ushort index = namespaces.GetIndexOrAppend("urn:reservations:replacement");
            var id = new NodeId("unit", index);
            var slot = new NodeManagerSlot();
            NodeManagerGeneration serving = CreateGeneration(slot, 1);
            slot.Publish(serving);
            NodeManagerGeneration candidate = CreateGeneration(slot, 2, NodeManagerGenerationState.Prepared);
            NodeManagerGeneration competingCandidate = CreateGeneration(slot, 2, NodeManagerGenerationState.Prepared);
            NodeManagerGeneration foreign = CreateGeneration(new NodeManagerSlot(), 1);

            reservations.Reserve(serving, namespaces, [id]);
            reservations.Reserve(candidate, namespaces, [id]);
            Assert.That(() => reservations.Reserve(competingCandidate, namespaces, [id]),
                Throws.TypeOf<ServiceResultException>());

            candidate.Gate.Abort();
            await CompletePhysicalCleanupAsync(candidate).ConfigureAwait(false);
            Assert.That(() => reservations.Reserve(foreign, namespaces, [id]),
                Throws.TypeOf<ServiceResultException>());
            serving.Gate.Retire();
            Assert.That(() => reservations.Reserve(foreign, namespaces, [id]),
                Throws.TypeOf<ServiceResultException>());
            await CompletePhysicalCleanupAsync(serving).ConfigureAwait(false);
            Assert.That(() => reservations.Reserve(foreign, namespaces, [id]), Throws.Nothing);
        }

        /// <summary>
        /// Verifies that cleaning up a retired generation leaves the successor's claim intact.
        /// </summary>
        [Test]
        public async Task CleaningReplacedGenerationPreservesSuccessorClaimAsync()
        {
            var reservations = new NodeIdReservations();
            var namespaces = new NamespaceTable();
            var id = new NodeId("unit", namespaces.GetIndexOrAppend("urn:reservations:handoff"));
            var slot = new NodeManagerSlot();
            NodeManagerGeneration original = CreateGeneration(slot, 1);
            NodeManagerGeneration successor = CreateGeneration(slot, 2, NodeManagerGenerationState.Prepared);
            NodeManagerGeneration foreign = CreateGeneration(new NodeManagerSlot(), 1);
            slot.Publish(original);
            reservations.Reserve(original, namespaces, [id]);
            reservations.Reserve(successor, namespaces, [id]);

            successor.Gate.Activate();
            slot.Publish(successor);
            original.Gate.Retire();
            await CompletePhysicalCleanupAsync(original).ConfigureAwait(false);

            Assert.That(() => reservations.Reserve(foreign, namespaces, [id]),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNodeIdExists));

            successor.Gate.Retire();
            await CompletePhysicalCleanupAsync(successor).ConfigureAwait(false);
            Assert.That(() => reservations.Reserve(foreign, namespaces, [id]), Throws.Nothing);
        }

        [Test]
        public void ReleasedRollbackCannotAffectOtherClaimsOrLaterReservations()
        {
            var reservations = new NodeIdReservations();
            var namespaces = new NamespaceTable();
            ushort index = namespaces.GetIndexOrAppend("urn:reservations:rollback");
            var retainedId = new NodeId("retained", index);
            var retryId = new NodeId("retry", index);
            NodeManagerGeneration owner = CreateGeneration(new NodeManagerSlot(), 1);
            NodeManagerGeneration foreign = CreateGeneration(new NodeManagerSlot(), 1);
            reservations.Reserve(owner, namespaces, [retainedId]);
            NodeIdReservations.Reservation rolledBack = reservations.Reserve(owner, namespaces, [retryId]);

            rolledBack.ReleaseAfterRollback();
            Assert.That(() => reservations.Reserve(foreign, namespaces, [retainedId]),
                Throws.TypeOf<ServiceResultException>());
            reservations.Reserve(owner, namespaces, [retryId]);
            rolledBack.ReleaseAfterRollback();
            Assert.That(() => reservations.Reserve(foreign, namespaces, [retryId]),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ReservedNodeCanOnlyBeIndexedByItsOwnerAndExactInstance()
        {
            var reservations = new NodeIdReservations();
            var namespaces = new NamespaceTable();
            var id = new NodeId("reserved", namespaces.GetIndexOrAppend("urn:reservations:index"));
            var node = new BaseObjectState(null) { NodeId = id };
            var impostor = new BaseObjectState(null) { NodeId = id };
            NodeManagerGeneration owner = CreateGeneration(new NodeManagerSlot(), 1);
            var index = new PredefinedNodeDictionary();
            reservations.Reserve(owner, namespaces, [id], new Dictionary<NodeId, NodeState> { [id] = node });

            Assert.That(() => reservations.Index(Mock.Of<IAsyncNodeManager>(), namespaces, index, node, default),
                Throws.TypeOf<ServiceResultException>());
            reservations.Index(owner.NodeManager, namespaces, index, node, default);
            Assert.That(() => reservations.Index(owner.NodeManager, namespaces, index, impostor, default),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(index[id], Is.SameAs(node));
        }

        private static async Task CompletePhysicalCleanupAsync(NodeManagerGeneration owner)
        {
            using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease cleanup =
                await owner.ResourceOwnership.EnterCleanupAsync(owner).ConfigureAwait(false);
            await cleanup.WaitForQuiescenceAsync().ConfigureAwait(false);
            owner.CleanupRecoveryGate.DisposeGeneration();
            NodeManagerGenerationRegistry.CompleteCleanup(owner);
        }

        private static NodeManagerGeneration CreateGeneration(
            NodeManagerSlot slot,
            long generation,
            NodeManagerGenerationState state = NodeManagerGenerationState.Live)
        {
            return new NodeManagerGeneration(slot, generation, Mock.Of<IAsyncNodeManager>(), state);
        }
    }
}
