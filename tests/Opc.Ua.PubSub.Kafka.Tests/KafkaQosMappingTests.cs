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

using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Verifies Part 14 Annex B.2 delivery guarantees map to Kafka producer settings.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka QoS to acks and idempotence mapping")]
    public sealed class KafkaQosMappingTests
    {
        [Test]
        [TestCase(KafkaQualityOfService.BestEffort, KafkaAcks.None, false)]
        [TestCase(KafkaQualityOfService.AtMostOnce, KafkaAcks.Leader, false)]
        [TestCase(KafkaQualityOfService.AtLeastOnce, KafkaAcks.All, true)]
        [TestCase(KafkaQualityOfService.ExactlyOnce, KafkaAcks.All, true)]
        public void QualityOfServiceMapsToKafkaDeliveryGuarantee(
            KafkaQualityOfService qos,
            KafkaAcks expectedAcks,
            bool expectedIdempotence)
        {
            KafkaDeliveryGuarantee guarantee = qos.ToDeliveryGuarantee();

            Assert.That(guarantee.Acks, Is.EqualTo(expectedAcks));
            Assert.That(guarantee.EnableIdempotence, Is.EqualTo(expectedIdempotence));
        }

        [Test]
        [TestCase(BrokerTransportQualityOfService.NotSpecified, KafkaQualityOfService.AtLeastOnce)]
        [TestCase(BrokerTransportQualityOfService.BestEffort, KafkaQualityOfService.BestEffort)]
        [TestCase(BrokerTransportQualityOfService.AtMostOnce, KafkaQualityOfService.AtMostOnce)]
        [TestCase(BrokerTransportQualityOfService.AtLeastOnce, KafkaQualityOfService.AtLeastOnce)]
        [TestCase(BrokerTransportQualityOfService.ExactlyOnce, KafkaQualityOfService.ExactlyOnce)]
        public void BrokerGuaranteeMapsAllPart14Values(
            BrokerTransportQualityOfService brokerGuarantee,
            KafkaQualityOfService expected)
        {
            KafkaQualityOfService actual = brokerGuarantee.FromBrokerGuarantee(
                KafkaQualityOfService.AtLeastOnce);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void UndefinedQualityOfServiceThrowsArgumentOutOfRangeException()
        {
            Assert.That(
                () => ((KafkaQualityOfService)123).ToDeliveryGuarantee(),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
