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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ModelChangeAggregator")]
    [Parallelizable]
    public class ModelChangeAggregatorTests
    {
        private ModelChangeAggregator m_aggregator;

        [SetUp]
        public void SetUp()
        {
            m_aggregator = new ModelChangeAggregator();
        }

        [Test]
        public void HasPendingReturnsFalseWhenEmpty()
        {
            Assert.That(m_aggregator.HasPending, Is.False);
        }

        [Test]
        public void AddThrowsWhenChangeNull()
        {
            Assert.That(() => m_aggregator.Add(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void AddMakesHasPendingTrue()
        {
            m_aggregator.Add(new ModelChangeStructureDataType
            {
                Affected = new NodeId(1),
                AffectedType = NodeId.Null,
                Verb = (byte)ModelChangeVerbs.NodeAdded
            });

            Assert.That(m_aggregator.HasPending, Is.True);
        }

        [Test]
        public void DrainReturnsEmptyWhenNoPendingChanges()
        {
            var result = m_aggregator.Drain();

            Assert.That(result.ToArray(), Is.Null.Or.Empty);
        }

        [Test]
        public void DrainReturnsAllPendingChangesAndClearsState()
        {
            m_aggregator.Add(new ModelChangeStructureDataType
            {
                Affected = new NodeId(1),
                AffectedType = NodeId.Null,
                Verb = (byte)ModelChangeVerbs.NodeAdded
            });
            m_aggregator.Add(new ModelChangeStructureDataType
            {
                Affected = new NodeId(2),
                AffectedType = NodeId.Null,
                Verb = (byte)ModelChangeVerbs.NodeDeleted
            });

            var result = m_aggregator.Drain();

            Assert.That(result.ToArray(), Has.Length.EqualTo(2));
            Assert.That(m_aggregator.HasPending, Is.False);
        }

        [Test]
        public void DrainAfterDrainReturnsEmpty()
        {
            m_aggregator.Add(new ModelChangeStructureDataType
            {
                Affected = new NodeId(1),
                AffectedType = NodeId.Null,
                Verb = (byte)ModelChangeVerbs.NodeAdded
            });

            m_aggregator.Drain();
            var second = m_aggregator.Drain();

            Assert.That(second.ToArray(), Is.Null.Or.Empty);
        }

        [Test]
        public void RecordNodeAddedThrowsWhenAffectedIsNull()
        {
            Assert.That(
                () => m_aggregator.RecordNodeAdded(NodeId.Null, null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RecordNodeAddedCreatesCorrectEntry()
        {
            var nodeId = new NodeId(10);
            var typeId = new NodeId(20);

            m_aggregator.RecordNodeAdded(nodeId, typeId);
            var result = m_aggregator.Drain().ToArray()!;

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Affected, Is.EqualTo(nodeId));
            Assert.That(result[0].AffectedType, Is.EqualTo(typeId));
            Assert.That(result[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.NodeAdded));
        }

        [Test]
        public void RecordNodeAddedUsesNullNodeIdWhenTypeDefinitionNull()
        {
            var nodeId = new NodeId(10);

            m_aggregator.RecordNodeAdded(nodeId, null);
            var result = m_aggregator.Drain().ToArray()!;

            Assert.That(result[0].AffectedType, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void RecordNodeDeletedThrowsWhenAffectedIsNull()
        {
            Assert.That(
                () => m_aggregator.RecordNodeDeleted(NodeId.Null, null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RecordNodeDeletedCreatesCorrectEntry()
        {
            var nodeId = new NodeId(30);
            var typeId = new NodeId(40);

            m_aggregator.RecordNodeDeleted(nodeId, typeId);
            var result = m_aggregator.Drain().ToArray()!;

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Affected, Is.EqualTo(nodeId));
            Assert.That(result[0].AffectedType, Is.EqualTo(typeId));
            Assert.That(result[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.NodeDeleted));
        }

        [Test]
        public void RecordReferenceAddedThrowsWhenAffectedIsNull()
        {
            Assert.That(
                () => m_aggregator.RecordReferenceAdded(NodeId.Null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RecordReferenceAddedCreatesCorrectEntry()
        {
            var nodeId = new NodeId(50);

            m_aggregator.RecordReferenceAdded(nodeId);
            var result = m_aggregator.Drain().ToArray()!;

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Affected, Is.EqualTo(nodeId));
            Assert.That(result[0].AffectedType, Is.EqualTo(NodeId.Null));
            Assert.That(result[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.ReferenceAdded));
        }

        [Test]
        public void RecordReferenceAddedWithTypeDefinition()
        {
            var nodeId = new NodeId(50);
            var typeId = new NodeId(60);

            m_aggregator.RecordReferenceAdded(nodeId, typeId);
            var result = m_aggregator.Drain().ToArray()!;

            Assert.That(result[0].AffectedType, Is.EqualTo(typeId));
        }

        [Test]
        public void RecordReferenceDeletedThrowsWhenAffectedIsNull()
        {
            Assert.That(
                () => m_aggregator.RecordReferenceDeleted(NodeId.Null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RecordReferenceDeletedCreatesCorrectEntry()
        {
            var nodeId = new NodeId(70);

            m_aggregator.RecordReferenceDeleted(nodeId);
            var result = m_aggregator.Drain().ToArray()!;

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Affected, Is.EqualTo(nodeId));
            Assert.That(result[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.ReferenceDeleted));
        }

        [Test]
        public void RecordDataTypeChangedThrowsWhenAffectedIsNull()
        {
            Assert.That(
                () => m_aggregator.RecordDataTypeChanged(NodeId.Null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RecordDataTypeChangedCreatesCorrectEntry()
        {
            var nodeId = new NodeId(80);
            var typeId = new NodeId(90);

            m_aggregator.RecordDataTypeChanged(nodeId, typeId);
            var result = m_aggregator.Drain().ToArray()!;

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Affected, Is.EqualTo(nodeId));
            Assert.That(result[0].AffectedType, Is.EqualTo(typeId));
            Assert.That(result[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.DataTypeChanged));
        }

        [Test]
        public void MultipleRecordsAccumulateCorrectly()
        {
            m_aggregator.RecordNodeAdded(new NodeId(1), null);
            m_aggregator.RecordNodeDeleted(new NodeId(2), null);
            m_aggregator.RecordReferenceAdded(new NodeId(3));
            m_aggregator.RecordReferenceDeleted(new NodeId(4));
            m_aggregator.RecordDataTypeChanged(new NodeId(5));

            var result = m_aggregator.Drain().ToArray()!;

            Assert.That(result, Has.Length.EqualTo(5));
            Assert.That(result[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.NodeAdded));
            Assert.That(result[1].Verb, Is.EqualTo((byte)ModelChangeVerbs.NodeDeleted));
            Assert.That(result[2].Verb, Is.EqualTo((byte)ModelChangeVerbs.ReferenceAdded));
            Assert.That(result[3].Verb, Is.EqualTo((byte)ModelChangeVerbs.ReferenceDeleted));
            Assert.That(result[4].Verb, Is.EqualTo((byte)ModelChangeVerbs.DataTypeChanged));
        }
    }
}
