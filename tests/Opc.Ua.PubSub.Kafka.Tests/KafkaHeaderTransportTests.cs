// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Tests for promoting DataSet fields into Kafka record headers through
    /// the <see cref="IPubSubHeaderTransport"/> capability on
    /// <see cref="KafkaBrokerTransport"/>.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka field promotion to record headers")]
    [CancelAfter(10000)]
    public sealed class KafkaHeaderTransportTests
    {
        [Test]
        public async Task KafkaBrokerTransport_ImplementsHeaderCapability()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            Assert.That(transport, Is.InstanceOf<IPubSubHeaderTransport>());
        }

        [Test]
        public async Task SendAsync_WithProperties_EmitsRecordHeaders()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            PubSubMessageProperty[] properties =
            [
                new("Temperature", "21.5"),
                new("Unit", "C")
            ];

            await transport.SendAsync(new byte[] { 1, 2, 3 }, KafkaTestHelper.JsonTopic, properties)
                .ConfigureAwait(false);

            KafkaMessage message = KafkaTestHelper.FirstProduced(factory.Adapter);
            Assert.That(message.Headers, Is.Not.Null);
            Assert.That(message.Headers!, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(message.Headers!["Temperature"], Is.EqualTo("21.5"));
                Assert.That(message.Headers!["Unit"], Is.EqualTo("C"));
            });
        }

        [Test]
        public async Task SendAsync_NoProperties_EmitsNoRecordHeaders()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport
                .SendAsync(new byte[] { 1 }, KafkaTestHelper.JsonTopic,
                    default(ArrayOf<PubSubMessageProperty>))
                .ConfigureAwait(false);

            KafkaMessage message = KafkaTestHelper.FirstProduced(factory.Adapter);
            Assert.That(message.Headers, Is.Null);
        }

        [Test]
        public async Task PlainSendAsync_EmitsNoRecordHeaders()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport.SendAsync(new byte[] { 1 }, KafkaTestHelper.JsonTopic)
                .ConfigureAwait(false);

            KafkaMessage message = KafkaTestHelper.FirstProduced(factory.Adapter);
            Assert.That(message.Headers, Is.Null);
        }
    }
}
