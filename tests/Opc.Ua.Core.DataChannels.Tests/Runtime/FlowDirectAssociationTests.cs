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

using System;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    /// <summary>
    /// Exact association transactions, direction ownership, nonreuse and readiness independently of transport I/O.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowDirectAssociationTests
    {
        /// <summary>
        /// Every permitted direction needs its own exact acknowledgement, then READY, before use.
        /// </summary>
        [TestCase(DataChannelDirection.Duplex, true)]
        [TestCase(DataChannelDirection.Duplex, false)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(DataChannelDirection.AcceptorToOpener, false)]
        public void EveryPermittedDirectionRequiresAcknowledgementAndReady(
            DataChannelDirection direction,
            bool localOpener)
        {
            var registry = new FlowDirectAssociationRegistry(2);
            FlowState flow = CreateFlow(direction, localOpener);
            Assert.That(registry.CanBecomeReady(flow), Is.False);
            FlowDirectAssociation? first = null;
            FlowDirectAssociation? second = null;
            for (byte sendingDirection = 0; sendingDirection <= 1; sendingDirection++)
            {
                if ((direction == DataChannelDirection.OpenerToAcceptor && sendingDirection == 1) ||
                    (direction == DataChannelDirection.AcceptorToOpener && sendingDirection == 0))
                {
                    continue;
                }
                uint operation = (uint)sendingDirection + 2;
                var proposal = new FlowAssociation(
                    FlowOperation.Associate, flow.Id, sendingDirection, Guid.NewGuid());
                Assert.That(registry.Admit(flow, proposal, 1, operation, out FlowDirectAssociation? admitted),
                    Is.EqualTo(DataChannelResult.Good));
                if (sendingDirection == 0)
                {
                    first = admitted;
                }
                else
                {
                    second = admitted;
                }
                Assert.Multiple(() =>
                {
                    Assert.That(registry.TryGetUsable(flow, true, out _), Is.False);
                    Assert.That(registry.TryGetUsable(flow, false, out _), Is.False);
                    Assert.That(registry.CanBecomeReady(flow), Is.False);
                });
                Assert.That(registry.Acknowledge(flow, Ack(proposal), 1, operation),
                    Is.EqualTo(DataChannelResult.Good));
            }
            Assert.That(registry.CanBecomeReady(flow), Is.True);
            Assert.That(registry.TryGetUsable(flow, true, out _), Is.False);
            flow.Ready = true;
            flow.State = DataChannelState.Open;
            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetUsable(flow, true, out FlowDirectAssociation? sending),
                    Is.EqualTo(flow.MaySend));
                Assert.That(registry.TryGetUsable(flow, false, out FlowDirectAssociation? receiving),
                    Is.EqualTo(flow.MayReceive));
                if (flow.MaySend)
                {
                    Assert.That(sending, Is.SameAs(localOpener ? first : second));
                }
                if (flow.MayReceive)
                {
                    Assert.That(receiving, Is.SameAs(localOpener ? second : first));
                }
            });
        }

        /// <summary>
        /// A recorded transaction can be retried exactly without resetting its outcome or consuming history.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ExactRetryReturnsOriginalAcknowledgedOwnerAtHistoryCapacity(bool afterReady)
        {
            var registry = new FlowDirectAssociationRegistry(1);
            FlowState flow = CreateFlow(DataChannelDirection.OpenerToAcceptor);
            var proposal = new FlowAssociation(FlowOperation.Associate, flow.Id, 0, Guid.NewGuid());
            Assert.That(registry.Admit(flow, proposal, 1, 2, out FlowDirectAssociation? original),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(registry.Acknowledge(flow, Ack(proposal), 1, 2), Is.EqualTo(DataChannelResult.Good));
            if (afterReady)
            {
                flow.Ready = true;
                flow.State = DataChannelState.Open;
                flow.Revision = 2;
            }
            var retry = new FlowAssociation(FlowOperation.Associate, flow.Id, 0, proposal.Token);
            Assert.That(registry.Admit(flow, retry, 1, 2, out FlowDirectAssociation? repeated),
                Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(repeated, Is.SameAs(original));
                Assert.That(repeated!.Acknowledged, Is.True);
                Assert.That(registry.Acknowledge(flow, Ack(retry), 1, 2), Is.EqualTo(DataChannelResult.Good));
                Assert.That(registry.CanBecomeReady(flow), Is.True);
            });
        }

        /// <summary>
        /// Each independently mismatched acknowledgement field leaves the original transaction unconfirmed.
        /// </summary>
        [TestCase("flow")]
        [TestCase("owner")]
        [TestCase("direction")]
        [TestCase("token")]
        [TestCase("revision")]
        [TestCase("operation")]
        [TestCase("opcode")]
        public void MismatchedAcknowledgementCannotConfirmTransaction(string changed)
        {
            var registry = new FlowDirectAssociationRegistry(2);
            FlowState flow = CreateFlow();
            var proposal = new FlowAssociation(FlowOperation.Associate, flow.Id, 0, Guid.NewGuid());
            Assert.That(registry.Admit(flow, proposal, 1, 2, out FlowDirectAssociation? association),
                Is.EqualTo(DataChannelResult.Good));
            var acknowledgement = new FlowAssociation(
                changed == "opcode" ? FlowOperation.Associate : FlowOperation.AssociateAck,
                changed == "flow" ? flow.Id + 2 : flow.Id,
                changed == "direction" ? (byte)1 : (byte)0,
                changed == "token" ? Guid.NewGuid() : proposal.Token);
            Assert.That(registry.Acknowledge(
                changed == "owner" ? CreateFlow() : flow,
                acknowledgement,
                changed == "revision" ? 2u : 1u,
                changed == "operation" ? 3u : 2u), Is.EqualTo(DataChannelResult.InvalidState));
            Assert.Multiple(() =>
            {
                Assert.That(association!.Acknowledged, Is.False);
                Assert.That(registry.CanBecomeReady(flow), Is.False);
            });
            Assert.That(registry.Acknowledge(flow, Ack(proposal), 1, 2), Is.EqualTo(DataChannelResult.Good));
        }

        /// <summary>
        /// Tokens, directions and operation IDs cannot be reassigned to another transaction.
        /// </summary>
        [TestCase("token")]
        [TestCase("direction")]
        [TestCase("operation")]
        [TestCase("owner")]
        public void ConflictingAssociationDoesNotReplaceOriginalOwner(string conflict)
        {
            var registry = new FlowDirectAssociationRegistry(2);
            FlowState flow = CreateFlow();
            Guid token = Guid.NewGuid();
            var original = new FlowAssociation(FlowOperation.Associate, flow.Id, 0, token);
            Assert.That(registry.Admit(flow, original, 1, 2, out FlowDirectAssociation? admitted),
                Is.EqualTo(DataChannelResult.Good));
            var proposal = new FlowAssociation(FlowOperation.Associate, flow.Id,
                conflict == "direction" ? (byte)0 : (byte)1,
                conflict is "token" or "owner" ? token : Guid.NewGuid());
            Assert.That(registry.Admit(conflict == "owner" ? CreateFlow() : flow,
                proposal, 1, conflict == "operation" ? 2u : 3u, out _),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(registry.Admit(flow, original, 1, 2, out FlowDirectAssociation? retried),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(retried, Is.SameAs(admitted));
        }

        /// <summary>
        /// Invalid initial fields do not consume a history slot or create usable state.
        /// </summary>
        [TestCase("flow")]
        [TestCase("revision")]
        [TestCase("zeroRevision")]
        [TestCase("operation")]
        [TestCase("token")]
        [TestCase("direction")]
        [TestCase("forbiddenDirection")]
        [TestCase("opcode")]
        [TestCase("ready")]
        [TestCase("terminal")]
        public void InvalidAdmissionDoesNotConsumeHistory(string invalid)
        {
            var registry = new FlowDirectAssociationRegistry(1);
            FlowState flow = CreateFlow(DataChannelDirection.OpenerToAcceptor);
            if (invalid == "ready")
            {
                flow.State = DataChannelState.Open;
            }
            else if (invalid == "terminal")
            {
                flow.State = DataChannelState.Faulted;
            }
            var proposal = new FlowAssociation(
                invalid == "opcode" ? FlowOperation.AssociateAck : FlowOperation.Associate,
                invalid == "flow" ? 3u : flow.Id,
                invalid == "direction" ? (byte)2 : invalid == "forbiddenDirection" ? (byte)1 : (byte)0,
                invalid == "token" ? Guid.Empty : Guid.NewGuid());
            Assert.That(registry.Admit(flow, proposal,
                invalid == "revision" ? 2u : invalid == "zeroRevision" ? 0u : 1u,
                invalid == "operation" ? 0u : 2u, out FlowDirectAssociation? rejected),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(rejected, Is.Null);
            FlowState fresh = CreateFlow(DataChannelDirection.OpenerToAcceptor);
            var valid = new FlowAssociation(FlowOperation.Associate, fresh.Id, 0, Guid.NewGuid());
            Assert.That(registry.Admit(fresh, valid, 1, 2, out _), Is.EqualTo(DataChannelResult.Good));
        }

        /// <summary>
        /// Termination prevents use and cannot free history for another flow, token or operation.
        /// </summary>
        [Test]
        public void ClosedFlowCannotRecycleHistoryOrBecomeReady()
        {
            var registry = new FlowDirectAssociationRegistry(1);
            FlowState flow = CreateFlow(DataChannelDirection.OpenerToAcceptor);
            var original = new FlowAssociation(FlowOperation.Associate, flow.Id, 0, Guid.NewGuid());
            Assert.That(registry.Admit(flow, original, 1, 2, out _), Is.EqualTo(DataChannelResult.Good));
            Assert.That(registry.Acknowledge(flow, Ack(original), 1, 2), Is.EqualTo(DataChannelResult.Good));
            flow.Ready = true;
            flow.State = DataChannelState.Closed;
            FlowState replacement = CreateFlow(DataChannelDirection.OpenerToAcceptor, id: 3);
            var reused = new FlowAssociation(FlowOperation.Associate, replacement.Id, 0, original.Token);
            var fresh = new FlowAssociation(FlowOperation.Associate, replacement.Id, 0, Guid.NewGuid());
            Assert.Multiple(() =>
            {
                Assert.That(registry.CanBecomeReady(flow), Is.False);
                Assert.That(registry.TryGetUsable(flow, true, out _), Is.False);
                Assert.That(registry.Acknowledge(flow, Ack(original), 1, 2),
                    Is.EqualTo(DataChannelResult.InvalidState));
                Assert.That(registry.Admit(replacement, reused, 1, 2, out _),
                    Is.EqualTo(DataChannelResult.InvalidState));
                Assert.That(registry.Admit(replacement, fresh, 1, 2, out _),
                    Is.EqualTo(DataChannelResult.WouldBlock));
            });
        }

        /// <summary>
        /// Unknown acknowledgements cannot create implicit state, including in another context's history.
        /// </summary>
        [Test]
        public void UnknownAndForeignContextAcknowledgementsAreRefused()
        {
            var registry = new FlowDirectAssociationRegistry(1);
            var foreign = new FlowDirectAssociationRegistry(1);
            FlowState flow = CreateFlow(DataChannelDirection.OpenerToAcceptor);
            var original = new FlowAssociation(FlowOperation.Associate, flow.Id, 0, Guid.NewGuid());
            Assert.That(registry.Admit(flow, original, 1, 2, out _), Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(foreign.Acknowledge(flow, Ack(original), 1, 2),
                    Is.EqualTo(DataChannelResult.InvalidState));
                Assert.That(foreign.CanBecomeReady(flow), Is.False);
            });
        }

        /// <summary>
        /// History must be positive and finite before admitting transactions.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidHistoryBoundIsRejected(int history)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlowDirectAssociationRegistry(history));
        }

        /// <summary>
        /// History consumes aggregate bytes and operations until context close, without retaining retired flows.
        /// </summary>
        [Test]
        public void RetiredTokenHistoryIsBudgetedWithoutRetainingFlowOwners()
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits { MaxOperations = 1 });
            var registry = new FlowDirectAssociationRegistry(4, budget);
            FlowState flow = CreateFlow(DataChannelDirection.OpenerToAcceptor);
            var first = new FlowAssociation(FlowOperation.Associate, flow.Id, 0, Guid.NewGuid());
            Assert.That(registry.Admit(flow, first, 1, 2, out FlowDirectAssociation? admitted),
                Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(budget.Usage.Operations, Is.EqualTo(1));
                Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(FlowDirectAssociationRegistry.HistoryEntryBytes));
            });
            registry.Retire(flow);
            Assert.That(admitted!.Flow, Is.Null);
            FlowState replacement = CreateFlow(DataChannelDirection.OpenerToAcceptor, id: 3);
            var reused = new FlowAssociation(FlowOperation.Associate, replacement.Id, 0, first.Token);
            var fresh = new FlowAssociation(FlowOperation.Associate, replacement.Id, 0, Guid.NewGuid());
            Assert.Multiple(() =>
            {
                Assert.That(registry.Admit(replacement, reused, 1, 2, out _),
                    Is.EqualTo(DataChannelResult.InvalidState));
                Assert.That(registry.Admit(replacement, fresh, 1, 2, out _),
                    Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(budget.Usage.Operations, Is.EqualTo(1));
            });
            registry.Clear();
            registry.Clear();
            Assert.Multiple(() =>
            {
                Assert.That(budget.Usage.BufferedBytes, Is.Zero);
                Assert.That(budget.Usage.Operations, Is.Zero);
            });
        }

        /// <summary>
        /// Creates an independently owned provisional flow with an admitted descriptor.
        /// </summary>
        private static FlowState CreateFlow(
            DataChannelDirection direction = DataChannelDirection.Duplex,
            bool localOpener = true,
            uint id = 1)
        {
            return new FlowState(id, localOpener, FlowLoopback.Parameters(direction),
                DataChannelAuthorizationMode.Configured, null, 0,
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <summary>
        /// Confirms all proposal fields without regenerating its token or transaction identity.
        /// </summary>
        private static FlowAssociation Ack(FlowAssociation proposal)
        {
            return new FlowAssociation(
                FlowOperation.AssociateAck, proposal.Flow, proposal.Direction, proposal.Token);
        }
    }
}
