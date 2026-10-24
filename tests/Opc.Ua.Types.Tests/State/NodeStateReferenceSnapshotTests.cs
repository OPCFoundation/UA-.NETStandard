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
    }
}
