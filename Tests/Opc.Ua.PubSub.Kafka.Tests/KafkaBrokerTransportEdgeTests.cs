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
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Edge-case coverage for Kafka transport guard rails and topic routing.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka broker transport edge cases")]
    [CancelAfter(10000)]
    public sealed class KafkaBrokerTransportEdgeTests
    {
        [Test]
        public void ConstructorRejectsInvalidArguments()
        {
            KafkaEndpoint endpoint = KafkaEndpointParser.Parse(KafkaTestHelper.EndpointUrl);
            PubSubConnectionDataType connection = KafkaTestHelper.NewConnection();
            var options = new KafkaConnectionOptions { Endpoint = KafkaTestHelper.EndpointUrl };

            Assert.That(() => new KafkaBrokerTransport(
                null!, endpoint, PubSubTransportDirection.Send, options, new FakeKafkaClientFactory(),
                NUnitTelemetryContext.Create(), TimeProvider.System), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new KafkaBrokerTransport(
                connection, endpoint, PubSubTransportDirection.Send, null!, new FakeKafkaClientFactory(),
                NUnitTelemetryContext.Create(), TimeProvider.System), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new KafkaBrokerTransport(
                connection, endpoint, PubSubTransportDirection.Send, options, null!,
                NUnitTelemetryContext.Create(), TimeProvider.System), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new KafkaBrokerTransport(
                connection, endpoint, PubSubTransportDirection.Send, options, new FakeKafkaClientFactory(),
                null!, TimeProvider.System), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new KafkaBrokerTransport(
                connection, endpoint, PubSubTransportDirection.Send, options, new FakeKafkaClientFactory(),
                NUnitTelemetryContext.Create(), null!), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SendBeforeOpenThrowsInvalidOperationException()
        {
            var factory = new FakeKafkaClientFactory();
            KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);

            Assert.That(
                async () => await transport.SendAsync(new byte[] { 0x01 }, KafkaTestHelper.JsonTopic),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task SendWithEmptyOrInvalidTopicThrowsAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(async () => await transport.SendAsync(new byte[] { 0x01 }, string.Empty),
                Throws.TypeOf<ArgumentException>());
            Assert.That(async () => await transport.SendAsync(new byte[] { 0x01 }, "bad\0topic"),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task SendCancellationAndDisposeAreObservedAsync()
        {
            var factory = new FakeKafkaClientFactory();
            KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(async () => await transport.SendAsync(
                    new byte[] { 0x01 },
                    KafkaTestHelper.JsonTopic,
                    cts.Token),
                Throws.InstanceOf<OperationCanceledException>());

            await transport.DisposeAsync().ConfigureAwait(false);
            Assert.That(async () => await transport.SendAsync(new byte[] { 0x01 }, KafkaTestHelper.JsonTopic),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(async () => await transport.OpenAsync(CancellationToken.None),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task DisposeDuringSendDisconnectsCleanlyAsync()
        {
            var gate = new TaskCompletionSource<bool>();
            var factory = new FakeKafkaClientFactory();
            factory.Adapter.OnProduce = async (_, _) => await gate.Task.ConfigureAwait(false);
            KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Task sendTask = transport.SendAsync(new byte[] { 0x01 }, KafkaTestHelper.JsonTopic).AsTask();
            Task disposeTask = transport.DisposeAsync().AsTask();
            gate.SetResult(true);
            await Task.WhenAll(sendTask, disposeTask).ConfigureAwait(false);

            Assert.That(factory.Adapter.DisconnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReceiveWithoutChannelYieldsNoFramesAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(
                factory,
                PubSubTransportDirection.Send);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            int frames = 0;

            await foreach (PubSubTransportFrame _ in transport.ReceiveAsync(cts.Token).ConfigureAwait(false))
            {
                frames++;
            }

            Assert.That(frames, Is.Zero);
        }

        [Test]
        public async Task UnsubscribeStopsFakeBusDeliveryAsync()
        {
            var bus = new FakeKafkaBus();
            var subscriberAdapter = new FakeKafkaClientAdapter(bus);
            var publisherAdapter = new FakeKafkaClientAdapter(bus);
            var subscriberFactory = new FakeKafkaClientFactory(subscriberAdapter);
            var publisherFactory = new FakeKafkaClientFactory(publisherAdapter);
            await using KafkaBrokerTransport subscriber = KafkaTestHelper.NewTransport(
                subscriberFactory,
                PubSubTransportDirection.Receive,
                connection: KafkaTestHelper.NewConnection(writer: false, reader: true));
            await using KafkaBrokerTransport publisher = KafkaTestHelper.NewTransport(publisherFactory);
            subscriber.Subscriptions.Add(KafkaTestHelper.JsonTopic);
            await subscriber.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await publisher.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await subscriberAdapter.UnsubscribeAsync([KafkaTestHelper.JsonTopic], CancellationToken.None)
                .ConfigureAwait(false);

            await publisher.SendAsync(new byte[] { 0x01 }, KafkaTestHelper.JsonTopic).ConfigureAwait(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            PubSubTransportFrame? frame = await KafkaTestHelper.ReceiveOneAsync(subscriber, cts.Token)
                .ConfigureAwait(false);

            Assert.That(frame, Is.Null);
            Assert.That(subscriberAdapter.Unsubscriptions, Does.Contain(KafkaTestHelper.JsonTopic));
        }

        [Test]
        public async Task ProducedRecordCarriesContentTypeHeaderAndPartitionKeyAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport.SendAsync(new byte[] { 0x01 }, KafkaTestHelper.JsonTopic).ConfigureAwait(false);

            KafkaMessage message = KafkaTestHelper.FirstProduced(factory.Adapter);
            Assert.That(message.ContentType, Is.EqualTo("application/json"));
            Assert.That(message.Key.ToArray(), Is.EqualTo(System.Text.Encoding.UTF8.GetBytes("42")));
        }

        [Test]
        public void TopicProviderUsesExplicitAndFallbackTopics()
        {
            PubSubConnectionDataType connection = KafkaTestHelper.NewConnection(
                dataTopic: "custom.data",
                metadataTopic: "custom.metadata");
            var factory = new FakeKafkaClientFactory();
            KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory, connection: connection);
            WriterGroupDataType writerGroup = connection.WriterGroups[0];
            PublisherId publisherId = PublisherId.From(new Variant((uint)42));

            Assert.That(transport.BuildDataTopic(publisherId, writerGroup, 3), Is.EqualTo("custom.data"));
            Assert.That(transport.BuildMetaDataTopic(publisherId, 1, 3), Is.EqualTo("custom.metadata"));
            Assert.That(transport.BuildDiscoveryTopic(publisherId, "status"), Is.EqualTo("opcua.json.status.42"));
        }
    }
}
