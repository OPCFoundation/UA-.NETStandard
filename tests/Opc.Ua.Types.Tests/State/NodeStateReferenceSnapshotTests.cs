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
using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Verifies ordered publication of plain node-owned reference storage.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class NodeStateReferenceSnapshotTests
    {
        /// <summary>
        /// Proves removals precede target withdrawal and additions follow target publication without callbacks.
        /// </summary>
        [Test]
        public void OrderedReferenceEditsDoNotNotifyUntilAfterPublication()
        {
            var node = new BaseObjectState(null);
            var oldTarget = new NodeId(1000);
            var nextTarget = new NodeId(1001);
            node.AddReference(ReferenceTypeIds.Organizes, false, oldTarget);
            var notifications = new List<bool>();
            bool published = false;
            node.OnReferenceRemoved = (_, _, _, _) => notifications.Add(published);
            node.OnReferenceAdded = (_, _, _, _) => notifications.Add(published);
            using NodeState.ReferenceUpdate update = node.PrepareReferences(
                [new NodeStateReference(ReferenceTypeIds.Organizes, false, nextTarget)],
                [new NodeStateReference(ReferenceTypeIds.Organizes, false, oldTarget)]);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, oldTarget), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, nextTarget), Is.False);
            update.Reserve();
            update.Remove();
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, oldTarget), Is.False);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, nextTarget), Is.False);
            Assert.That(notifications, Is.Empty);
            published = true;
            update.Complete();
            update.Dispose();
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, nextTarget), Is.True);
            update.Notify(failure => Assert.Fail(failure.ToString()));
            update.Notify(failure => Assert.Fail(failure.ToString()));
            Assert.That(notifications, Has.Count.EqualTo(2));
            Assert.That(notifications, Is.All.True);
        }

        /// <summary>
        /// Proves stale preparation is rejected before a decision and ordinary mutations remain usable.
        /// </summary>
        [Test]
        public void ChangedReferencesRejectReservationBeforePublication()
        {
            var node = new BaseObjectState(null);
            using NodeState.ReferenceUpdate update = node.PrepareReferences(
                [new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1000))], []);
            node.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId(1001));
            Assert.Throws<InvalidOperationException>(update.Reserve);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(1000)), Is.False);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.HasComponent, false, new NodeId(1001)), Is.True);
        }

        /// <summary>
        /// Empty edits, absent removals and duplicate additions cannot invalidate a prepared publication.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void NoOpReferencePublicationPreservesPreparedEdits(int operation, bool existing)
        {
            var node = new BaseObjectState(null);
            var retained = new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1000));
            var missing = new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1001));
            if (existing)
            {
                node.AddReference(retained.ReferenceTypeId, retained.IsInverse, retained.TargetId);
            }
            int notifications = 0;
            node.OnReferenceAdded = (_, _, _, _) => notifications++;
            node.OnReferenceRemoved = (_, _, _, _) => notifications++;
            using NodeState.ReferenceUpdate prepared = node.PrepareReferences([missing], []);
            using (NodeState.ReferenceUpdate noOp = node.PrepareReferences(
                operation == 2 ? [retained] : [], operation == 1 ? [missing] : []))
            {
                noOp.Reserve();
                noOp.Remove();
                noOp.Complete();
                noOp.Dispose();
                noOp.Notify(failure => Assert.Fail(failure.ToString()));
            }
            Assert.That(notifications, Is.Zero);
            Assert.That(node.ReferenceExists(retained.ReferenceTypeId, retained.IsInverse, retained.TargetId),
                Is.EqualTo(existing));
            Assert.That(node.ReferenceExists(missing.ReferenceTypeId, missing.IsInverse, missing.TargetId), Is.False);
            prepared.Reserve();
            prepared.Remove();
            prepared.Complete();
            prepared.Dispose();
            prepared.Notify(failure => Assert.Fail(failure.ToString()));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(node.ReferenceExists(missing.ReferenceTypeId, missing.IsInverse, missing.TargetId), Is.True);
        }

        /// <summary>
        /// Real removals and additions still invalidate a competing prepared publication.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ChangedReferencePublicationRejectsCompetingPreparedEdits(bool removal)
        {
            var node = new BaseObjectState(null);
            var changed = new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1000));
            var pending = new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1001));
            if (removal)
            {
                node.AddReference(changed.ReferenceTypeId, changed.IsInverse, changed.TargetId);
            }
            using NodeState.ReferenceUpdate prepared = node.PrepareReferences([pending], []);
            using (NodeState.ReferenceUpdate update = node.PrepareReferences(
                removal ? [] : [changed], removal ? [changed] : []))
            {
                update.Reserve();
                update.Remove();
                update.Complete();
            }
            Assert.That(prepared.Reserve, Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("Serving references changed during preparation."));
            Assert.That(node.ReferenceExists(changed.ReferenceTypeId, changed.IsInverse, changed.TargetId),
                Is.EqualTo(!removal));
            Assert.That(node.ReferenceExists(pending.ReferenceTypeId, pending.IsInverse, pending.TargetId), Is.False);
        }

        /// <summary>
        /// Proves abandoning a reserved update preserves serving references and releases mutation admission.
        /// </summary>
        [Test]
        public void AbandonedReferenceUpdatePreservesServingReferences()
        {
            var node = new BaseObjectState(null);
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(1000));
            using (NodeState.ReferenceUpdate update = node.PrepareReferences(
                [], [new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1000))]))
            {
                update.Reserve();
                Assert.Throws<InvalidOperationException>(() =>
                    node.RemoveReference(ReferenceTypeIds.Organizes, false, new NodeId(1000)));
                Assert.Throws<InvalidOperationException>(() => update.Notify(_ => Assert.Fail()));
            }
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(1000)), Is.True);
            Assert.That(node.RemoveReference(ReferenceTypeIds.Organizes, false, new NodeId(1000)), Is.True);
        }

        /// <summary>
        /// Proves unreserved, out-of-order and abandoned edits cannot overwrite node-owned storage.
        /// </summary>
        [Test]
        public void ReferencePublicationRequiresReservedOrderedEdits()
        {
            var node = new BaseObjectState(null);
            var target = new NodeId(1000);
            using NodeState.ReferenceUpdate update = node.PrepareReferences(
                [new NodeStateReference(ReferenceTypeIds.Organizes, false, target)], []);
            Assert.Throws<InvalidOperationException>(update.Remove);
            Assert.Throws<InvalidOperationException>(update.Complete);
            update.Reserve();
            Assert.Throws<InvalidOperationException>(update.Complete);
            update.Dispose();
            Assert.Throws<InvalidOperationException>(update.Remove);
            Assert.Throws<InvalidOperationException>(update.Complete);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, target), Is.False);
        }

        /// <summary>
        /// Proves caller-owned reference changes cannot alter the precomputed publication.
        /// </summary>
        [Test]
        public void PreparedReferenceEditsOwnTheirKeys()
        {
            var node = new BaseObjectState(null);
            var reference = new ReferenceNode(ReferenceTypeIds.Organizes, false, new NodeId(1000));
            using NodeState.ReferenceUpdate update = node.PrepareReferences([reference], []);
            reference.TargetId = new NodeId(1001);
            update.Reserve();
            update.Remove();
            update.Complete();
            update.Dispose();
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(1000)), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(1001)), Is.False);
        }

        /// <summary>
        /// Proves invalid references fail during preparation, never after a decision.
        /// </summary>
        [Test]
        public void InvalidReferenceFailsBeforeReservation()
        {
            var node = new BaseObjectState(null);
            Assert.Throws<ArgumentException>(() =>
                node.PrepareReferences([new NodeStateReference(NodeId.Null, false, new NodeId(1000))], []));
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(1001));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(1001)), Is.True);
        }

        /// <summary>
        /// Proves each observer failure is reported after successful publication, without suppressing other observers.
        /// </summary>
        [Test]
        public void ObserverFailuresDoNotRollBackPublishedReferenceEdits()
        {
            var node = new BaseObjectState(null);
            node.OnReferenceAdded = (_, _, _, _) => throw new InvalidOperationException("Observer failure.");
            var failures = new List<Exception>();
            using NodeState.ReferenceUpdate update = node.PrepareReferences(
                [new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1000))], []);
            update.Reserve();
            update.Remove();
            update.Complete();
            update.Dispose();
            update.Notify(failures.Add);
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(1000)), Is.True);
        }

        /// <summary>
        /// Retrying a partial observer fan-out preserves every successful observer and exact failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RetryingReferenceNotificationsInvokesOnlyFailedObservers(bool removal)
        {
            var node = new BaseObjectState(null);
            var reference = new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(1000));
            if (removal)
            {
                node.AddReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
            }
            int firstCount = 0;
            int failedCount = 0;
            int lastCount = 0;
            bool fail = true;
            var original = new InvalidOperationException("Partial reference observer failure.");
            void First() => firstCount++;
            void Failed()
            {
                failedCount++;
                if (fail)
                {
                    throw original;
                }
            }
            void Last() => lastCount++;
            if (removal)
            {
                node.OnReferenceRemoved += (_, _, _, _) => First();
                node.OnReferenceRemoved += (_, _, _, _) => Failed();
                node.OnReferenceRemoved += (_, _, _, _) => Last();
            }
            else
            {
                node.OnReferenceAdded += (_, _, _, _) => First();
                node.OnReferenceAdded += (_, _, _, _) => Failed();
                node.OnReferenceAdded += (_, _, _, _) => Last();
            }
            using NodeState.ReferenceUpdate update = node.PrepareReferences(
                removal ? [] : [reference], removal ? [reference] : []);
            update.Reserve();
            update.Remove();
            update.Complete();
            update.Dispose();
            var failures = new List<Exception>();
            update.Notify(failures.Add);
            Assert.That(failures, Is.EqualTo(new[] { original }));
            Assert.That(lastCount, Is.EqualTo(1), "One failed subscriber cannot suppress later observers.");
            fail = false;
            failures.Clear();
            update.Notify(failures.Add);
            update.Notify(failures.Add);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(failures, Is.Empty);
                Assert.That(firstCount, Is.EqualTo(1));
                Assert.That(failedCount, Is.EqualTo(2));
                Assert.That(lastCount, Is.EqualTo(1));
                Assert.That(node.ReferenceExists(
                    reference.ReferenceTypeId, reference.IsInverse, reference.TargetId), Is.EqualTo(!removal));
            }
        }
    }
}
