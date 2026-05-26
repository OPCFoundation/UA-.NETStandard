/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/
using System;
using NUnit.Framework;
using Opc.Ua.Server.Alarms;

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
    }
}