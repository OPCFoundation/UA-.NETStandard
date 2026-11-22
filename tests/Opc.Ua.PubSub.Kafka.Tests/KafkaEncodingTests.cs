// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Verifies profile and message encoding helpers for Kafka records.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka profile content types")]
    public sealed class KafkaEncodingTests
    {
        [Test]
        [TestCase(KafkaEncoding.Uadp, "uadp", "application/opcua+uadp")]
        [TestCase(KafkaEncoding.Json, "json", "application/json")]
        public void EncodingMapsToTopicSegmentAndContentType(
            KafkaEncoding encoding,
            string expectedSegment,
            string expectedContentType)
        {
            Assert.That(encoding.ToTopicSegment(), Is.EqualTo(expectedSegment));
            Assert.That(encoding.ToContentType(), Is.EqualTo(expectedContentType));
        }

        [Test]
        public void UndefinedEncodingThrowsArgumentOutOfRangeException()
        {
            Assert.That(
                () => ((KafkaEncoding)123).ToContentType(),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(
                () => ((KafkaEncoding)123).ToTopicSegment(),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void KafkaProfilesExposeJsonAndUadpProfileUris()
        {
            Assert.That(KafkaProfiles.PubSubKafkaJsonTransport, Does.Contain("pubsub-kafka-json"));
            Assert.That(KafkaProfiles.PubSubKafkaUadpTransport, Does.Contain("pubsub-kafka-uadp"));
        }
    }
}
