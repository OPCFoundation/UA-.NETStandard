// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Lifecycle and state-event tests for the Kafka broker transport.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka transport lifecycle")]
    [CancelAfter(10000)]
    public sealed class KafkaBrokerTransportLifecycleTests
    {
        [Test]
        public async Task OpenCloseCycleSucceedsAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);

            Assert.That(transport.IsConnected, Is.False);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.True);
            Assert.That(factory.Adapter.ConnectCount, Is.EqualTo(1));

            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.False);
            Assert.That(factory.Adapter.DisconnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task OpenOnAlreadyOpenedTransportIsIdempotentAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(factory.Adapter.ConnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CloseOnUnopenedTransportDoesNotThrowAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);

            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
            Assert.That(factory.Adapter.DisconnectCount, Is.Zero);
        }

        [Test]
        public async Task DoubleCloseIsIdempotentAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(factory.Adapter.DisconnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            var factory = new FakeKafkaClientFactory();
            KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport.DisposeAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);

            Assert.That(factory.Adapter.DisconnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StateChangedFiresOnOpenCloseAndAdapterEventsAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            var events = new List<PubSubTransportStateChangedEventArgs>();
            transport.StateChanged += (_, e) => events.Add(e);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            factory.Adapter.RaiseConnectionStateChanged(false, "broker reset");
            factory.Adapter.RaiseConnectionStateChanged(true, "reconnected");
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(events, Has.Count.GreaterThanOrEqualTo(4));
            Assert.That(events[0].IsConnected, Is.True);
            Assert.That(events, Has.Some.Matches<PubSubTransportStateChangedEventArgs>(e =>
                e is not null && !e.IsConnected && e.Reason == "broker reset"));
            Assert.That(events, Has.Some.Matches<PubSubTransportStateChangedEventArgs>(e =>
                e is not null && e.IsConnected && e.Reason == "reconnected"));
            Assert.That(events[^1].IsConnected, Is.False);
        }

        [Test]
        public async Task SendAsyncRequiresTopicAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                async () => await transport.SendAsync(new byte[] { 1, 2, 3 }, topic: null).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task SendAsyncProducesMessageAndIncrementsDiagnosticsAsync()
        {
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(
                factory,
                diagnostics: diagnostics);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            byte[] payload = [9, 8, 7, 6];
            await transport.SendAsync(payload, KafkaTestHelper.JsonTopic).ConfigureAwait(false);

            KafkaMessage message = KafkaTestHelper.FirstProduced(factory.Adapter);
            Assert.That(message.Topic, Is.EqualTo(KafkaTestHelper.JsonTopic));
            Assert.That(message.Value.ToArray(), Is.EqualTo(payload));
            Assert.That(message.ContentType, Is.EqualTo("application/json"));
            Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages), Is.EqualTo(1));
        }

        [Test]
        public async Task ReceiveAsyncDeliversIncomingMessagesAsync()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(
                factory,
                PubSubTransportDirection.Receive);
            transport.Subscriptions.Add(KafkaTestHelper.JsonTopic);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            byte[] payload = [1, 2, 3];
            factory.Adapter.RaiseIncomingMessage(
                new KafkaMessage(KafkaTestHelper.JsonTopic, ReadOnlyMemory<byte>.Empty, payload, "application/json", null),
                DateTimeUtc.From(DateTime.UtcNow));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            PubSubTransportFrame? frame = await KafkaTestHelper.ReceiveOneAsync(transport, cts.Token)
                .ConfigureAwait(false);

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Topic, Is.EqualTo(KafkaTestHelper.JsonTopic));
            Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(payload));
        }
    }
}
