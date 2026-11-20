/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic unit tests for
    /// <see cref="SubscriptionManager.CalculateRevisedSamplingInterval(double, double, double, double)"/>
    /// which implements the interplay between the sampling interval requested by a client,
    /// the MinimumSamplingInterval declared by a node and the server wide
    /// MinSupportedSamplingInterval.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Category("SamplingInterval")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SamplingIntervalRevisionTests
    {
        private const double kPublishingInterval = 500;
        private const double kMinSupportedSamplingInterval = 2000;

        [Test]
        public void NegativeSamplingIntervalResolvesToPublishingInterval()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                -1,
                kPublishingInterval,
                MinimumSamplingIntervals.Continuous,
                0);

            Assert.That(revised, Is.EqualTo(kPublishingInterval));
        }

        [Test]
        public void NegativeSamplingIntervalResolvesToPublishingIntervalThenFloor()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                -1,
                kPublishingInterval,
                MinimumSamplingIntervals.Indeterminate,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(kMinSupportedSamplingInterval));
        }

        [Test]
        public void ContinuousNodeBypassesMinSupportedSamplingInterval()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                MinimumSamplingIntervals.Continuous,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(10.0),
                "Nodes reporting by exception are not bound by the minimum supported sampling interval.");
        }

        [Test]
        public void ContinuousNodeWithZeroRequestStaysZero()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                0,
                kPublishingInterval,
                MinimumSamplingIntervals.Continuous,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.Zero);
        }

        [Test]
        public void IndeterminateNodeIsRaisedToMinSupportedSamplingInterval()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                MinimumSamplingIntervals.Indeterminate,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(kMinSupportedSamplingInterval));
        }

        [Test]
        public void IndeterminateNodeWithoutFloorKeepsRequestedInterval()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                MinimumSamplingIntervals.Indeterminate,
                0);

            Assert.That(revised, Is.EqualTo(10.0));
        }

        [Test]
        public void MinSupportedSamplingIntervalWinsWhenAboveNodeMinimum()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                500,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(kMinSupportedSamplingInterval));
        }

        [Test]
        public void NodeMinimumWinsWhenAboveMinSupportedSamplingInterval()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                5000,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(5000.0));
        }

        [Test]
        public void NodeMinimumIsHonoredWithoutMinSupportedSamplingInterval()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                5000,
                0);

            Assert.That(revised, Is.EqualTo(5000.0));
        }

        [Test]
        public void RequestedIntervalAboveAllMinimumsIsKept()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10000,
                kPublishingInterval,
                5000,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(10000.0));
        }

        [Test]
        public void MaxValueSamplingIntervalIsCappedToTimerRange()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                double.MaxValue,
                kPublishingInterval,
                MinimumSamplingIntervals.Indeterminate,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo((double)int.MaxValue));
        }

        [Test]
        public void MaxValueSamplingIntervalIsCappedForContinuousNodes()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                double.MaxValue,
                kPublishingInterval,
                MinimumSamplingIntervals.Continuous,
                0);

            Assert.That(revised, Is.EqualTo((double)int.MaxValue));
        }

        [TestCase(double.NaN, 500)]
        [TestCase(double.PositiveInfinity, int.MaxValue)]
        [TestCase(double.NegativeInfinity, 500)]
        [TestCase(3_000_000_000d, int.MaxValue)]
        [TestCase(2_147_483_648d, int.MaxValue)]
        [TestCase(2_147_483_647d, int.MaxValue)]
        [TestCase(2_147_483_646d, 2_147_483_646d)]
        public void NonFiniteAndLargeRequestsProduceTimerSafeIntervals(double requested, double expected)
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                requested, 500, MinimumSamplingIntervals.Continuous, 0);

            Assert.That(revised, Is.EqualTo(expected));
        }

        [Test]
        public void ValueAttributeUsesNodeMinimumSamplingInterval()
        {
            var variable = new BaseDataVariableState(null)
            {
                MinimumSamplingInterval = 5000
            };

            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                variable,
                Attributes.Value,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(5000.0));
        }

        [Test]
        public void ValueAttributeOfContinuousVariableBypassesFloor()
        {
            var variable = new BaseDataVariableState(null)
            {
                MinimumSamplingInterval = MinimumSamplingIntervals.Continuous
            };

            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                variable,
                Attributes.Value,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(10.0));
        }

        [Test]
        public void NonValueAttributeIgnoresNodeMinimumAndAppliesFloor()
        {
            var variable = new BaseDataVariableState(null)
            {
                MinimumSamplingInterval = MinimumSamplingIntervals.Continuous
            };

            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                variable,
                Attributes.Description,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(kMinSupportedSamplingInterval),
                "Only the Value Attribute of a Variable carries a MinimumSamplingInterval.");
        }

        [Test]
        public void NonVariableNodeAppliesFloor()
        {
            var objectState = new BaseObjectState(null);

            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                objectState,
                Attributes.Value,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(kMinSupportedSamplingInterval));
        }

        [Test]
        public void NullNodeAppliesFloor()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                10,
                kPublishingInterval,
                null,
                Attributes.Value,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(kMinSupportedSamplingInterval));
        }
    }
}
