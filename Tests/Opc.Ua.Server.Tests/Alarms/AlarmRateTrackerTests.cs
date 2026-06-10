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
using NUnit.Framework;
using Opc.Ua.Server.Alarms;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Server.Tests.Alarms
{
    [TestFixture, Category("AlarmMetrics"), Parallelizable]
    public class AlarmRateTrackerTests
    {
        [Test]
        public void CurrentAlarmRateStartsAtZero()
        {
            var tracker = new AlarmRateTracker();
            Assert.That(tracker.CurrentAlarmRate, Is.Zero);
            Assert.That(tracker.MaximumAlarmRate, Is.Zero);
        }

        [Test]
        public void RecordActivationIncrementsCurrentRate()
        {
            var tracker = new AlarmRateTracker();
            tracker.RecordActivation();
            tracker.RecordActivation();

            Assert.That(tracker.CurrentAlarmRate, Is.EqualTo(2L));
            Assert.That(tracker.MaximumAlarmRate, Is.EqualTo(2L));
        }

        [Test]
        public void OldActivationsAreTrimmedFromWindow()
        {
            var tracker = new AlarmRateTracker(TimeSpan.FromMilliseconds(100));
            DateTime old = DateTime.UtcNow.AddSeconds(-10);
            tracker.RecordActivation(old);

            Assert.That(tracker.CurrentAlarmRate, Is.Zero);
        }

        [Test]
        public void MaximumRateRetainsHighestObserved()
        {
            var tracker = new AlarmRateTracker(TimeSpan.FromMilliseconds(100));
            tracker.RecordActivation();
            tracker.RecordActivation();
            tracker.RecordActivation();

            System.Threading.Thread.Sleep(150);
            tracker.RecordActivation();

            Assert.That(tracker.CurrentAlarmRate, Is.EqualTo(1L));
            Assert.That(tracker.MaximumAlarmRate, Is.EqualTo(3L));
        }

        [Test]
        public void ResetClearsAllCounters()
        {
            var tracker = new AlarmRateTracker();
            tracker.RecordActivation();
            tracker.RecordActivation();

            tracker.Reset();

            Assert.That(tracker.CurrentAlarmRate, Is.Zero);
            Assert.That(tracker.MaximumAlarmRate, Is.Zero);
        }
    }

    [TestFixture, Category("ModelChangeAggregator"), Parallelizable]
    public class ModelChangeAggregatorTests
    {
        [Test]
        public void NewAggregatorHasNoPending()
        {
            var agg = new ModelChangeAggregator();
            Assert.That(agg.HasPending, Is.False);
        }

        [Test]
        public void DrainEmptyReturnsEmpty()
        {
            var agg = new ModelChangeAggregator();
            ArrayOf<ModelChangeStructureDataType> changes = agg.Drain();
            Assert.That(changes.Count, Is.Zero);
        }

        [Test]
        public void RecordNodeAddedAddsPending()
        {
            var agg = new ModelChangeAggregator();
            agg.RecordNodeAdded(new NodeId(1), new NodeId(2));

            Assert.That(agg.HasPending, Is.True);

            ArrayOf<ModelChangeStructureDataType> changes = agg.Drain();
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.NodeAdded));
            Assert.That(agg.HasPending, Is.False);
        }

        [Test]
        public void MultipleRecordsAggregateIntoSingleDrain()
        {
            var agg = new ModelChangeAggregator();
            agg.RecordNodeAdded(new NodeId(1), new NodeId(2));
            agg.RecordNodeDeleted(new NodeId(3), new NodeId(2));
            agg.RecordReferenceAdded(new NodeId(4));

            ArrayOf<ModelChangeStructureDataType> changes = agg.Drain();
            Assert.That(changes.Count, Is.EqualTo(3));
        }

        [Test]
        public void AddWithNullEntryThrowsArgumentNullException()
        {
            var agg = new ModelChangeAggregator();
            Assert.That(() => agg.Add(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [TestCase("NodeAdded")]
        [TestCase("NodeDeleted")]
        [TestCase("ReferenceAdded")]
        [TestCase("ReferenceDeleted")]
        [TestCase("DataTypeChanged")]
        public void RecordFamilyWithNullNodeIdThrowsArgumentNullException(string family)
        {
            var agg = new ModelChangeAggregator();
            switch (family)
            {
                case "NodeAdded":
                    Assert.That(() => agg.RecordNodeAdded(NodeId.Null, null),
                        Throws.InstanceOf<ArgumentNullException>());
                    break;
                case "NodeDeleted":
                    Assert.That(() => agg.RecordNodeDeleted(NodeId.Null, null),
                        Throws.InstanceOf<ArgumentNullException>());
                    break;
                case "ReferenceAdded":
                    Assert.That(() => agg.RecordReferenceAdded(NodeId.Null),
                        Throws.InstanceOf<ArgumentNullException>());
                    break;
                case "ReferenceDeleted":
                    Assert.That(() => agg.RecordReferenceDeleted(NodeId.Null),
                        Throws.InstanceOf<ArgumentNullException>());
                    break;
                case "DataTypeChanged":
                    Assert.That(() => agg.RecordDataTypeChanged(NodeId.Null),
                        Throws.InstanceOf<ArgumentNullException>());
                    break;
            }
        }

        [Test]
        public void RecordReferenceDeletedProducesEntryWithReferenceDeletedVerb()
        {
            var agg = new ModelChangeAggregator();
            agg.RecordReferenceDeleted(new NodeId(50));

            ArrayOf<ModelChangeStructureDataType> changes = agg.Drain();
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.ReferenceDeleted));
            Assert.That(changes[0].Affected, Is.EqualTo(new NodeId(50)));
        }

        [Test]
        public void RecordDataTypeChangedProducesEntryWithDataTypeChangedVerb()
        {
            var agg = new ModelChangeAggregator();
            agg.RecordDataTypeChanged(new NodeId(60));

            ArrayOf<ModelChangeStructureDataType> changes = agg.Drain();
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.DataTypeChanged));
            Assert.That(changes[0].Affected, Is.EqualTo(new NodeId(60)));
        }

        [Test]
        public void DrainEmptiesPendingListSoSecondDrainIsEmpty()
        {
            var agg = new ModelChangeAggregator();
            agg.RecordNodeAdded(new NodeId(70), new NodeId(71));

            ArrayOf<ModelChangeStructureDataType> first = agg.Drain();
            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(agg.HasPending, Is.False);

            ArrayOf<ModelChangeStructureDataType> second = agg.Drain();
            Assert.That(second.Count, Is.Zero);
            Assert.That(agg.HasPending, Is.False);
        }

        [Test]
        public void RecordNodeAddedWithNullTypeDefinitionResolvesToNodeIdNull()
        {
            var agg = new ModelChangeAggregator();
            agg.RecordNodeAdded(new NodeId(80), null);

            ArrayOf<ModelChangeStructureDataType> changes = agg.Drain();
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].AffectedType.IsNull, Is.True);
            Assert.That(changes[0].Verb, Is.EqualTo((byte)ModelChangeVerbs.NodeAdded));
        }
    }
}
