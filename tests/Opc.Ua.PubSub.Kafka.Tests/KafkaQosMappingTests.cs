/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
