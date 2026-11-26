// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Exercises <see cref="MqttTopicBuilder"/> against the topic
    /// schemas defined in Part 14 §7.3.4.7.3 (data topics) and
    /// §7.3.4.7.4 (metadata topics).
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4.7.3")]
    [TestSpec("7.3.4.7.4")]
    public sealed class MqttTopicBuilderTests
    {
        [Test]
        public void BuildDataTopic_UInt32PublisherId_ProducesExpectedShape()
        {
            string topic = MqttTopicBuilder.BuildDataTopic(
                "opcua/pubsub",
                MqttEncoding.Uadp,
                new Variant((uint)42),
                100,
                null);

            Assert.That(topic, Is.EqualTo("opcua/pubsub/uadp/data/42/100"));
        }

        [Test]
        public void BuildDataTopic_WithDataSetWriterId_AppendsWriterSegment()
        {
            string topic = MqttTopicBuilder.BuildDataTopic(
                "opcua/pubsub",
                MqttEncoding.Json,
                new Variant("Publisher1"),
                writerGroupId: 1,
                dataSetWriterId: 200);

            Assert.That(topic, Is.EqualTo("opcua/pubsub/json/data/Publisher1/1/200"));
        }

        [Test]
        public void BuildDataTopic_GuidPublisherId_UsesNFormat()
        {
            var guid = Guid.NewGuid();
            string topic = MqttTopicBuilder.BuildDataTopic(
                "opcua/pubsub",
                MqttEncoding.Json,
                new Variant(new Uuid(guid)),
                10,
                null);

            string expected = $"opcua/pubsub/json/data/{guid:N}/10";
            Assert.That(topic, Is.EqualTo(expected));
        }

        [Test]
        public void BuildMetaDataTopic_AllArguments_ProducesMetadataShape()
        {
            string topic = MqttTopicBuilder.BuildMetaDataTopic(
                "opcua/pubsub",
                MqttEncoding.Uadp,
                new Variant((ushort)5),
                writerGroupId: 7,
                dataSetWriterId: 99);

            Assert.That(topic, Is.EqualTo("opcua/pubsub/uadp/metadata/5/7/99"));
        }

        [Test]
        public void BuildPublisherTopic_BuildsStatusTopic()
        {
            string topic = MqttTopicBuilder.BuildPublisherTopic(
                "opcua",
                MqttEncoding.Json,
                MqttTopicBuilder.StatusSegment,
                new Variant("PubOne"));

            Assert.That(
                topic,
                Is.EqualTo("opcua/json/status/PubOne"));
        }

        [Test]
        [TestCase("opcua/#")]
        [TestCase("opcua/pub+sub")]
        [TestCase("/opcua")]
        [TestCase("opcua/")]
        public void BuildDataTopic_RejectsInvalidPrefix(string prefix)
        {
            Assert.That(
                () => MqttTopicBuilder.BuildDataTopic(
                    prefix,
                    MqttEncoding.Uadp,
                    new Variant((uint)1),
                    1,
                    null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void BuildDataTopic_RejectsWildcardInPublisherId()
        {
            Assert.That(
                () => MqttTopicBuilder.BuildDataTopic(
                    "opcua/pubsub",
                    MqttEncoding.Uadp,
                    new Variant("pub+lisher"),
                    1,
                    null),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => MqttTopicBuilder.BuildDataTopic(
                    "opcua/pubsub",
                    MqttEncoding.Uadp,
                    new Variant("pub#lisher"),
                    1,
                    null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void BuildDataTopic_RejectsForwardSlashInPublisherId()
        {
            Assert.That(
                () => MqttTopicBuilder.BuildDataTopic(
                    "opcua/pubsub",
                    MqttEncoding.Uadp,
                    new Variant("pub/lisher"),
                    1,
                    null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ToPublisherIdToken_NullVariant_ReturnsZero()
        {
            string token = MqttTopicBuilder.ToPublisherIdToken(Variant.Null);
            Assert.That(token, Is.EqualTo("0"));
        }

        [Test]
        public void ToPublisherIdToken_UInt64_FormatsAsString()
        {
            string token = MqttTopicBuilder.ToPublisherIdToken(new Variant((ulong)123456789));
            Assert.That(token, Is.EqualTo("123456789"));
        }

        [Test]
        public void ToPublisherIdToken_Byte_FormatsAsString()
        {
            string token = MqttTopicBuilder.ToPublisherIdToken(new Variant((byte)7));
            Assert.That(token, Is.EqualTo("7"));
        }

        [Test]
        public void ToPublisherIdToken_StringVariant_PassesThrough()
        {
            string token = MqttTopicBuilder.ToPublisherIdToken(new Variant("MyPublisher"));
            Assert.That(token, Is.EqualTo("MyPublisher"));
        }

        [Test]
        public void MqttEncoding_ToTopicSegmentProducesLowercase()
        {
            Assert.That(MqttEncoding.Uadp.ToTopicSegment(), Is.EqualTo("uadp"));
            Assert.That(MqttEncoding.Json.ToTopicSegment(), Is.EqualTo("json"));
        }

        [Test]
        public void MqttEncoding_ToContentTypeProducesPart14Values()
        {
            Assert.That(MqttEncoding.Uadp.ToContentType(), Is.EqualTo("application/opcua+uadp"));
            Assert.That(MqttEncoding.Json.ToContentType(), Is.EqualTo("application/json"));
        }
    }
}
