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

#if NET8_0_OR_GREATER
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using Testcontainers.Kafka;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Docker-backed Kafka broker integration test using Testcontainers.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("B.2", Summary = "Kafka real broker round trip")]
    [CancelAfter(60000)]
    public sealed class KafkaIntegrationDockerTests
    {
        private KafkaContainer? m_container;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            try
            {
                m_container = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").Build();
                await m_container.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Assert.Ignore($"Docker/Kafka not available: {ex.Message}");
            }
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_container is not null)
            {
                await m_container.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RealBrokerRoundTripsPayloadAsync()
        {
            Assert.That(m_container, Is.Not.Null);
            string bootstrapAddress = m_container!.GetBootstrapAddress();
            string topic = "opcua-json-data-" + Guid.NewGuid().ToString("N");
            string url = "kafka://" + bootstrapAddress;
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["OpcUa:PubSub:Kafka:AutoOffsetReset"] = "Earliest",
                    ["OpcUa:PubSub:Kafka:GroupId"] = "opcua-tests-" + Guid.NewGuid().ToString("N"),
                    ["OpcUa:PubSub:Kafka:ClientId"] = "opcua-kafka-tests"
                })
                .Build();
            var services = new ServiceCollection();
            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddKafkaTransport(configuration));
            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            KafkaPubSubTransportFactory factory = serviceProvider
                .GetServices<IPubSubTransportFactory>()
                .OfType<KafkaPubSubTransportFactory>()
                .Single(f => f.TransportProfileUri == KafkaProfiles.PubSubKafkaJsonTransport);
            PubSubConnectionDataType pubConnection = KafkaTestHelper.NewConnection(url: url, dataTopic: topic);
            PubSubConnectionDataType subConnection = KafkaTestHelper.NewConnection(
                url: url,
                writer: false,
                reader: true,
                dataTopic: topic,
                metadataTopic: topic + ".metadata");
            await using IPubSubTransport publisher = factory.Create(
                pubConnection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            await using IPubSubTransport subscriber = factory.Create(
                subConnection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            if (subscriber is KafkaBrokerTransport kafkaSubscriber)
            {
                kafkaSubscriber.Subscriptions.Add(topic);
            }

            await subscriber.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await publisher.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            byte[] payload = [0x10, 0x20, 0x30, 0x40];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<PubSubTransportFrame?> receiveTask = KafkaTestHelper.ReceiveOneAsync(subscriber, cts.Token);

            await publisher.SendAsync(payload, topic).ConfigureAwait(false);
            PubSubTransportFrame? frame = await receiveTask.ConfigureAwait(false);

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Topic, Is.EqualTo(topic));
            Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(payload));
        }
    }
}
#endif
