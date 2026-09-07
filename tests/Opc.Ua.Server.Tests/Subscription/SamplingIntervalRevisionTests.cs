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
    public class SamplingIntervalRevisionTests
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
        public void MaxValueSamplingIntervalIsCappedToOneYear()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                double.MaxValue,
                kPublishingInterval,
                MinimumSamplingIntervals.Indeterminate,
                kMinSupportedSamplingInterval);

            Assert.That(revised, Is.EqualTo(365 * 24 * 3600 * 1000.0));
        }

        [Test]
        public void MaxValueSamplingIntervalIsCappedForContinuousNodes()
        {
            double revised = SubscriptionManager.CalculateRevisedSamplingInterval(
                double.MaxValue,
                kPublishingInterval,
                MinimumSamplingIntervals.Continuous,
                0);

            Assert.That(revised, Is.EqualTo(365 * 24 * 3600 * 1000.0));
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
