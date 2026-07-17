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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Verifies deterministic in-memory Kafka producer to subscriber round trips.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka transport in-memory round trips")]
    [CancelAfter(10000)]
    public sealed class KafkaBrokerTransportRoundTripTests
    {
        [Test]
        [TestCase(KafkaProfiles.PubSubKafkaJsonTransport, KafkaTestHelper.JsonTopic, "application/json")]
        [TestCase(KafkaProfiles.PubSubKafkaUadpTransport, KafkaTestHelper.UadpTopic, "application/opcua+uadp")]
        public async Task PublisherAndSubscriberRoundTripPayloadAsync(
            string profile,
            string topic,
            string contentType)
        {
            var bus = new FakeKafkaBus();
            var publisherAdapter = new FakeKafkaClientAdapter(bus);
            var subscriberAdapter = new FakeKafkaClientAdapter(bus);
            var publisherFactory = new FakeKafkaClientFactory(publisherAdapter);
            var subscriberFactory = new FakeKafkaClientFactory(subscriberAdapter);
            PubSubConnectionDataType pubConnection = KafkaTestHelper.NewConnection(profile: profile);
            PubSubConnectionDataType subConnection = KafkaTestHelper.NewConnection(
                writer: false,
                reader: true,
                profile: profile,
                dataTopic: topic,
                metadataTopic: KafkaTestHelper.MetadataTopic);
            await using KafkaBrokerTransport publisher = KafkaTestHelper.NewTransport(
                publisherFactory,
                PubSubTransportDirection.Send,
                connection: pubConnection);
            await using KafkaBrokerTransport subscriber = KafkaTestHelper.NewTransport(
                subscriberFactory,
                PubSubTransportDirection.Receive,
                connection: subConnection);
            subscriber.Subscriptions.Add(topic);

            await subscriber.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await publisher.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            byte[] payload = [0xCA, 0xFE, 0xBA, 0xBE];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Task<PubSubTransportFrame?> receiveTask = KafkaTestHelper.ReceiveOneAsync(subscriber, cts.Token);

            await publisher.SendAsync(payload, topic).ConfigureAwait(false);
            PubSubTransportFrame? frame = await receiveTask.ConfigureAwait(false);

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Topic, Is.EqualTo(topic));
            Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(payload));
            KafkaMessage produced = KafkaTestHelper.FirstProduced(publisherAdapter);
            Assert.That(produced.ContentType, Is.EqualTo(contentType));
        }

        [Test]
        public async Task SubscriberReceivesDefaultReaderGroupSubscriptionsAsync()
        {
            var bus = new FakeKafkaBus();
            var publisherAdapter = new FakeKafkaClientAdapter(bus);
            var subscriberAdapter = new FakeKafkaClientAdapter(bus);
            await using KafkaBrokerTransport publisher = KafkaTestHelper.NewTransport(
                new FakeKafkaClientFactory(publisherAdapter));
            await using KafkaBrokerTransport subscriber = KafkaTestHelper.NewTransport(
                new FakeKafkaClientFactory(subscriberAdapter),
                PubSubTransportDirection.Receive,
                connection: KafkaTestHelper.NewConnection(
                    writer: false,
                    reader: true,
                    dataTopic: KafkaTestHelper.JsonTopic,
                    metadataTopic: KafkaTestHelper.MetadataTopic));

            await subscriber.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await publisher.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(subscriberAdapter.Subscriptions, Does.Contain(KafkaTestHelper.JsonTopic));
            Assert.That(subscriberAdapter.Subscriptions, Does.Contain(KafkaTestHelper.MetadataTopic));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Task<PubSubTransportFrame?> receiveTask = KafkaTestHelper.ReceiveOneAsync(subscriber, cts.Token);
            await publisher.SendAsync(new byte[] { 0x11, 0x22 }, KafkaTestHelper.MetadataTopic)
                .ConfigureAwait(false);
            PubSubTransportFrame? frame = await receiveTask.ConfigureAwait(false);

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Topic, Is.EqualTo(KafkaTestHelper.MetadataTopic));
            Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(new byte[] { 0x11, 0x22 }));
        }
    }
}
